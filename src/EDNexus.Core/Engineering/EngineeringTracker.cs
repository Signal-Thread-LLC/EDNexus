using System.Collections.Concurrent;
using System.Text.Json;
using EDNexus.Core.Journal;
using EDNexus.Core.Odyssey;
using EDNexus.Core.State;

namespace EDNexus.Core.Engineering;

/// <summary>
/// Joins the static <see cref="EngineeringCatalog"/> to the live commander: it tracks which engineers
/// the journal reports as unlocked (and at what rank) via <c>EngineerProgress</c>, and turns a pinned
/// blueprint into a concrete plan — the engineer to visit plus a material checklist annotated with
/// current holdings and where to farm each item. It never mutates <see cref="CommanderState"/>.
/// </summary>
public sealed class EngineeringTracker
{
    private readonly EngineeringCatalog _catalog;
    private readonly OdysseyCatalog _odyssey;
    // engineer in-game name → unlocked rank (1–5). Only unlocked engineers appear here.
    // Odyssey engineers report through the same EngineerProgress event as ship engineers, so one
    // dictionary covers both.
    private readonly ConcurrentDictionary<string, int> _unlockedRanks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after an <c>EngineerProgress</c> event changes the unlocked picture.</summary>
    public event Action? Changed;

    public EngineeringTracker(JournalEventBus bus, EngineeringCatalog? catalog = null, OdysseyCatalog? odyssey = null)
    {
        _catalog = catalog ?? EngineeringCatalog.Default;
        _odyssey = odyssey ?? OdysseyCatalog.Default;
        bus.Subscribe("EngineerProgress", OnEngineerProgress);
    }

    public EngineeringCatalog Catalog => _catalog;
    public OdysseyCatalog Odyssey => _odyssey;

    /// <summary>Unlocked engineers by in-game name → highest rank reached (1–5).</summary>
    public IReadOnlyDictionary<string, int> UnlockedRanks => _unlockedRanks;

    public bool IsUnlocked(Engineer engineer) => _unlockedRanks.ContainsKey(engineer.Name);
    public bool IsUnlocked(OdysseyEngineer engineer) => _unlockedRanks.ContainsKey(engineer.Name);

    private void OnEngineerProgress(JournalEntry e)
    {
        // Two shapes: a startup snapshot with an "Engineers" array, and a live single-engineer update.
        if (e.Raw.TryGetProperty("Engineers", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray()) Apply(item);
        }
        else
        {
            Apply(e.Raw);
        }
        Changed?.Invoke();
    }

    private void Apply(JsonElement item)
    {
        var name = item.TryGetProperty("Engineer", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (string.IsNullOrEmpty(name)) return;

        var progress = item.TryGetProperty("Progress", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        if (!string.Equals(progress, "Unlocked", StringComparison.OrdinalIgnoreCase))
        {
            // Invited / Known — not yet usable; drop any stale unlocked entry.
            _unlockedRanks.TryRemove(name, out _);
            return;
        }

        var rank = item.TryGetProperty("Rank", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt32(out var v) ? v : 1;
        _unlockedRanks[name] = rank;
    }

    /// <summary>
    /// Build the plan for a pinned blueprint at a grade: the best engineer to visit (preferring one
    /// already unlocked to the required rank) and the materials it needs joined with current holdings.
    /// Returns null if the blueprint id is unknown or the grade isn't defined.
    /// </summary>
    public PinnedBlueprintPlan? BuildPlan(string blueprintId, int grade, CommanderState state)
    {
        var blueprint = _catalog.Blueprint(blueprintId);
        var bg = blueprint?.Grade(grade);
        if (blueprint is null || bg is null) return null;

        var offering = bg.EngineerIds.Select(_catalog.Engineer).Where(x => x is not null).Cast<Engineer>().ToList();

        // Prefer an engineer already unlocked to at least this grade, then any unlocked, then the first listed.
        var chosen = offering.FirstOrDefault(x => _unlockedRanks.TryGetValue(x.Name, out var rk) && rk >= grade)
            ?? offering.FirstOrDefault(IsUnlocked)
            ?? offering.FirstOrDefault();

        var others = offering.Where(x => !ReferenceEquals(x, chosen)).ToList();

        var materials = bg.Ingredients.Select(symbol =>
        {
            var info = _catalog.Material(symbol);
            var category = info?.Category ?? "Unknown";
            return new MaterialRequirement(
                Symbol: symbol,
                Name: info?.Name ?? symbol,
                Category: category,
                Grade: info?.Grade ?? 0,
                Held: HeldCount(state, category, symbol),
                Source: info?.Source ?? "Source unknown — reference data pending.");
        }).ToList();

        return new PinnedBlueprintPlan(
            blueprint, grade, chosen,
            EngineerUnlocked: chosen is not null && IsUnlocked(chosen),
            OtherEngineers: others,
            Materials: materials);
    }

    private static int HeldCount(CommanderState state, string category, string symbol)
    {
        var dict = category.ToLowerInvariant() switch
        {
            "raw" => state.Materials.Raw,
            "manufactured" => state.Materials.Manufactured,
            "encoded" => state.Materials.Encoded,
            _ => null,
        };
        return dict is not null && dict.TryGetValue(symbol, out var v) ? v : 0;
    }

    // --- Odyssey: on-foot suit/weapon grade upgrades. ---

    /// <summary>
    /// Build the plan for a pinned suit upgrade: the cumulative material checklist and credit cost from
    /// the currently-equipped grade (or 1, if this isn't the equipped suit) up to the target grade, plus
    /// the modifications the suit can take at that grade. Returns null for an unknown suit/grade, or a
    /// suit with no upgrade path (the Flight Suit).
    /// </summary>
    public SuitUpgradePlan? BuildSuitUpgradePlan(string suitId, int targetGrade, CommanderState state)
    {
        var suit = _odyssey.Suit(suitId);
        if (suit is null || !suit.IsUpgradeable || targetGrade is < 1 or > 5) return null;

        // SuitSymbol carries the "_classN" suffix (e.g. "tacticalsuit_class3"); the catalog's SuitSymbol
        // is just the prefix, so this must be a prefix match, not an exact one.
        var fromGrade = state.SuitSymbol is not null && state.SuitSymbol.StartsWith(suit.SuitSymbol, StringComparison.OrdinalIgnoreCase)
            ? Math.Max(1, state.SuitClass) : 1;

        var (credits, estimated, materials) = SumSteps(suit.GradeSteps, fromGrade, targetGrade, state);
        var slotIndex = Math.Clamp(targetGrade - 1, 0, suit.ModSlotsByGrade.Count - 1);
        var slots = suit.ModSlotsByGrade.Count > 0 ? suit.ModSlotsByGrade[slotIndex] : 0;
        var mods = ResolveMods(_odyssey.SuitMods, slots);

        return new SuitUpgradePlan(suit, fromGrade, targetGrade, credits, estimated, materials, mods);
    }

    /// <summary>
    /// Build the plan for a pinned weapon upgrade. Weapons have no live "current grade" detection (no
    /// per-weapon loadout event is tracked), so this always plans from grade 1.
    /// </summary>
    public WeaponUpgradePlan? BuildWeaponUpgradePlan(string weaponId, int targetGrade, CommanderState state)
    {
        var weapon = _odyssey.Weapon(weaponId);
        if (weapon is null || targetGrade is < 1 or > 5) return null;

        const int fromGrade = 1;
        var (credits, estimated, materials) = SumSteps(weapon.GradeSteps, fromGrade, targetGrade, state);
        var slotIndex = Math.Clamp(targetGrade - 1, 0, weapon.ModSlotsByGrade.Count - 1);
        var slots = weapon.ModSlotsByGrade.Count > 0 ? weapon.ModSlotsByGrade[slotIndex] : 0;
        var mods = ResolveMods(_odyssey.WeaponMods, slots);

        return new WeaponUpgradePlan(weapon, fromGrade, targetGrade, credits, estimated, materials, mods);
    }

    /// <summary>Sum the grade steps strictly after <paramref name="fromGrade"/> up to and including <paramref name="toGrade"/>.</summary>
    private (long Credits, bool Estimated, IReadOnlyList<UpgradeRequirement> Materials) SumSteps(
        IReadOnlyList<GradeStep> allSteps, int fromGrade, int toGrade, CommanderState state)
    {
        var steps = allSteps.Where(s => s.Grade > fromGrade && s.Grade <= toGrade).ToList();

        long credits = 0;
        var estimated = false;
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            credits += step.Credits;
            estimated |= step.CreditsEstimated;
            foreach (var m in step.Materials)
                totals[m.Symbol] = totals.TryGetValue(m.Symbol, out var v) ? v + m.Count : m.Count;
        }

        var materials = totals.Select(kv =>
        {
            var info = _odyssey.Material(kv.Key);
            var category = info?.Category ?? "Unknown";
            return new UpgradeRequirement(
                Symbol: kv.Key,
                Name: info?.Name ?? kv.Key,
                Category: category,
                Needed: kv.Value,
                Held: OnFootHeldCount(state, category, kv.Key),
                Source: info?.Source ?? "Source unknown — reference data pending.");
        }).OrderBy(m => m.Category).ThenBy(m => m.Name).ToList();

        return (credits, estimated, materials);
    }

    private IReadOnlyList<ModOption> ResolveMods(IReadOnlyList<Modification> mods, int slots)
    {
        if (slots <= 0) return Array.Empty<ModOption>();

        return mods.Select(mod =>
        {
            var offering = mod.EngineerIds.Select(_odyssey.Engineer).Where(e => e is not null).Cast<OdysseyEngineer>().ToList();
            var chosen = offering.FirstOrDefault(IsUnlocked) ?? offering.FirstOrDefault();
            return new ModOption(mod, chosen, chosen is not null && IsUnlocked(chosen));
        }).ToList();
    }

    private static int OnFootHeldCount(CommanderState state, string category, string symbol)
    {
        var dict = category.ToLowerInvariant() switch
        {
            "item" => state.OnFoot.Items,
            "component" => state.OnFoot.Components,
            "data" => state.OnFoot.Data,
            "consumable" => state.OnFoot.Consumables,
            _ => null,
        };
        return dict is not null && dict.TryGetValue(symbol, out var v) ? v : 0;
    }
}
