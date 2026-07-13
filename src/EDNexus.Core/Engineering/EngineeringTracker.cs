using System.Collections.Concurrent;
using System.Text.Json;
using EDNexus.Core.Journal;
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
    // engineer in-game name → unlocked rank (1–5). Only unlocked engineers appear here.
    private readonly ConcurrentDictionary<string, int> _unlockedRanks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after an <c>EngineerProgress</c> event changes the unlocked picture.</summary>
    public event Action? Changed;

    public EngineeringTracker(JournalEventBus bus, EngineeringCatalog? catalog = null)
    {
        _catalog = catalog ?? EngineeringCatalog.Default;
        bus.Subscribe("EngineerProgress", OnEngineerProgress);
    }

    public EngineeringCatalog Catalog => _catalog;

    /// <summary>Unlocked engineers by in-game name → highest rank reached (1–5).</summary>
    public IReadOnlyDictionary<string, int> UnlockedRanks => _unlockedRanks;

    public bool IsUnlocked(Engineer engineer) => _unlockedRanks.ContainsKey(engineer.Name);

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
}
