using EDNexus.Core.Materials;
using EDNexus.Core.State;

namespace EDNexus.Core.Engineering;

/// <summary>One roll a commander intends to make: a blueprint at a grade, optionally more than once.</summary>
/// <param name="Rolls">
/// How many times to apply this grade. Engineering rarely lands a good roll first try, so planning
/// several of the same grade is the normal case, not an edge case.
/// </param>
public sealed record PlannedRoll(string BlueprintId, int Grade, int Rolls = 1);

/// <summary>
/// A queue of intended rolls costed against the commander's material inventory: what every roll
/// needs in total, what is already aboard, and what is still missing.
/// </summary>
public sealed record EngineeringPlan(
    IReadOnlyList<PlannedRoll> Queue,
    IReadOnlyList<MaterialRequirement> Materials)
{
    /// <summary>Materials still short, scarcest grade first — the shopping list.</summary>
    public IReadOnlyList<MaterialRequirement> Shopping { get; } =
        Materials.Where(m => !m.Satisfied)
            .OrderByDescending(m => m.Grade)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Everything the queue needs is already aboard.</summary>
    public bool Ready => Materials.All(m => m.Satisfied);

    /// <summary>Total units still to be found across the whole queue.</summary>
    public int TotalShortfall => Materials.Sum(m => m.Shortfall);
}

/// <summary>
/// Turns a queue of intended engineering rolls into a shopping list: needed minus owned, per
/// material, summed across the queue.
/// </summary>
/// <remarks>
/// A pure projection over the catalog and <see cref="CommanderState"/> — it holds no state and
/// mutates nothing, so the UI can recompute it on any tick and the shortfall tracks materials
/// being collected or spent without any extra plumbing.
/// </remarks>
public static class EngineeringPlanner
{
    /// <summary>Cost a single blueprint grade.</summary>
    public static EngineeringPlan Plan(string blueprintId, int grade, CommanderState state, int rolls = 1)
        => Plan(new[] { new PlannedRoll(blueprintId, grade, rolls) }, state);

    /// <summary>
    /// Cost a whole queue. Materials shared between rolls are summed, so the list answers
    /// "what do I need for all of this", not "what does each step need".
    /// </summary>
    public static EngineeringPlan Plan(IEnumerable<PlannedRoll> queue, CommanderState state, EngineeringCatalog? catalog = null)
    {
        var cat = catalog ?? EngineeringCatalog.Default;
        var rolls = queue.Where(r => r.Rolls > 0).ToList();

        var needed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var roll in rolls)
        {
            var bg = cat.Blueprint(roll.BlueprintId)?.Grade(roll.Grade);
            if (bg is null) continue;   // unknown blueprint or grade contributes nothing

            foreach (var material in bg.Materials)
                needed[material.Symbol] = needed.GetValueOrDefault(material.Symbol) + material.Count * roll.Rolls;
        }

        var materials = needed
            .Select(kv => Requirement(cat, state, kv.Key, kv.Value))
            .OrderByDescending(m => m.Grade)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EngineeringPlan(rolls, materials);
    }

    /// <summary>
    /// How to cover a shortfall at a material trader, cheapest swap first. Empty when the hold
    /// already covers it, or when nothing held can be traded into it.
    /// </summary>
    public static IReadOnlyList<TradeQuote> TradeOptions(
        MaterialRequirement requirement, CommanderState state, int limit = 3)
    {
        if (requirement.Satisfied) return Array.Empty<TradeQuote>();

        var target = EngineeringCatalog.Default.Material(requirement.Symbol);
        if (target is null) return Array.Empty<TradeQuote>();

        var held = MaterialInventory.CountsFor(state, target.Category);
        return MaterialTrader.BestSources(target, requirement.Shortfall, held, limit);
    }

    private static MaterialRequirement Requirement(EngineeringCatalog cat, CommanderState state, string symbol, int needed)
    {
        var info = cat.Material(symbol);
        var category = info?.Category ?? "Unknown";
        return new MaterialRequirement(
            Symbol: symbol,
            Name: info?.Name ?? symbol,
            Category: category,
            Grade: info?.Grade ?? 0,
            Needed: needed,
            Held: HeldCount(state, category, symbol),
            Source: info?.Source is { Length: > 0 } src ? src : "Source unknown — reference data pending.");
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
        return dict is not null && dict.TryGetValue(symbol, out var n) ? n : 0;
    }
}
