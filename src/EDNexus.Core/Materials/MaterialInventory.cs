using System.Collections.Concurrent;
using EDNexus.Core.Engineering;
using EDNexus.Core.State;

namespace EDNexus.Core.Materials;

/// <summary>
/// Joins the material catalog to what the commander is carrying, producing the full inventory
/// picture: every material, its grade and cap, and how close to full it is.
/// </summary>
/// <remarks>
/// Pure projection over <see cref="CommanderState"/> — it holds no state of its own and mutates
/// nothing, so the UI can rebuild it on any tick.
/// </remarks>
public static class MaterialInventory
{
    /// <summary>The three inventory categories in the order the game presents them.</summary>
    public static readonly IReadOnlyList<string> Categories = new[] { "Raw", "Manufactured", "Encoded" };

    /// <summary>
    /// Build the full picture for one category. Every catalogued material appears, held or not, so
    /// the view can show what is missing as readily as what is stocked.
    /// </summary>
    public static MaterialCategoryView Category(CommanderState state, string category)
    {
        var counts = CountsFor(state, category);
        var stocks = EngineeringCatalog.Default.Materials
            .Where(m => string.Equals(m.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Grade)
            .Select(m => new MaterialStock(m, counts.GetValueOrDefault(m.Symbol)))
            .ToList();

        return new MaterialCategoryView(category, stocks);
    }

    /// <summary>Every category, in game order.</summary>
    public static IReadOnlyList<MaterialCategoryView> All(CommanderState state)
        => Categories.Select(c => Category(state, c)).ToList();

    /// <summary>
    /// Materials sitting at their cap, rarest first. These are the ones worth spending at a trader:
    /// until they come down, every further pickup of them is discarded.
    /// </summary>
    public static IReadOnlyList<MaterialStock> AtCap(CommanderState state)
        => All(state)
            .SelectMany(c => c.Stocks)
            .Where(s => s.IsFull)
            .OrderByDescending(s => s.Grade)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Held counts for a category, keyed by journal symbol.</summary>
    public static IReadOnlyDictionary<string, int> CountsFor(CommanderState state, string category)
        => Bucket(state, category);

    private static ConcurrentDictionary<string, int> Bucket(CommanderState state, string category)
        => category.ToLowerInvariant() switch
        {
            "raw" => state.Materials.Raw,
            "manufactured" => state.Materials.Manufactured,
            _ => state.Materials.Encoded,
        };
}
