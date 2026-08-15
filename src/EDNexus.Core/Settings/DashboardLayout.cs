namespace EDNexus.Core.Settings;

/// <summary>
/// The saved arrangement of one dashboard card. Absent from the settings file until the commander
/// changes something, so an untouched install keeps whatever order the app ships with.
/// </summary>
/// <param name="Id">Card key (e.g. "market"), matching <c>CardViewModel.Id</c>.</param>
public sealed class CardLayout
{
    public string Id { get; set; } = "";

    /// <summary>Position in the dashboard flow, ascending.</summary>
    public int Order { get; set; }

    /// <summary>False hides the card entirely.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Card width in pixels. 0 means "use the card's own default".</summary>
    public double Width { get; set; }

    /// <summary>Whether the card is collapsed to its header.</summary>
    public bool Collapsed { get; set; }
}

/// <summary>The commander's dashboard arrangement, persisted through the settings store.</summary>
public sealed class DashboardSettings
{
    /// <summary>Per-card arrangement. Empty until the commander first customises the dashboard.</summary>
    public List<CardLayout> Cards { get; set; } = new();
}

/// <summary>A card as the app defines it, before any saved layout is applied.</summary>
/// <param name="DefaultWidth">The width the card ships with, restored by "reset layout".</param>
public readonly record struct CardDefaults(string Id, double DefaultWidth);

/// <summary>
/// Reconciles a saved dashboard layout with the cards the app actually has.
/// </summary>
/// <remarks>
/// The two drift apart across releases — cards get added and removed — so this is deliberately
/// tolerant in both directions: a saved entry for a card that no longer exists is dropped, and a
/// card with no saved entry keeps its shipped defaults and lands after everything already
/// arranged. That way upgrading never loses a commander's arrangement, and never hides a new card
/// they have not seen yet.
/// </remarks>
public static class DashboardLayout
{
    /// <summary>
    /// Produce the effective layout for <paramref name="known"/>, applying <paramref name="saved"/>
    /// where it still applies. Returns one entry per known card, in display order.
    /// </summary>
    public static IReadOnlyList<CardLayout> Merge(IEnumerable<CardDefaults> known, IEnumerable<CardLayout>? saved)
    {
        var savedById = new Dictionary<string, CardLayout>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in saved ?? Enumerable.Empty<CardLayout>())
            if (entry.Id is { Length: > 0 }) savedById[entry.Id] = entry;

        // New cards sort after every arranged one, in the order the app declared them.
        var nextOrder = savedById.Count == 0 ? 0 : savedById.Values.Max(c => c.Order) + 1;

        var merged = new List<CardLayout>();
        foreach (var card in known)
        {
            if (savedById.TryGetValue(card.Id, out var entry))
            {
                merged.Add(new CardLayout
                {
                    Id = card.Id,
                    Order = entry.Order,
                    Visible = entry.Visible,
                    Width = entry.Width > 0 ? entry.Width : card.DefaultWidth,
                    Collapsed = entry.Collapsed,
                });
            }
            else
            {
                merged.Add(new CardLayout
                {
                    Id = card.Id,
                    Order = nextOrder++,
                    Visible = true,
                    Width = card.DefaultWidth,
                    Collapsed = false,
                });
            }
        }

        return Renumber(merged.OrderBy(c => c.Order).ToList());
    }

    /// <summary>The shipped arrangement — used by "reset layout to default".</summary>
    public static IReadOnlyList<CardLayout> Defaults(IEnumerable<CardDefaults> known)
        => Renumber(known.Select((c, i) => new CardLayout
        {
            Id = c.Id,
            Order = i,
            Visible = true,
            Width = c.DefaultWidth,
            Collapsed = false,
        }).ToList());

    /// <summary>
    /// Move the card with <paramref name="id"/> one place towards the front or back, and return the
    /// re-ordered layout. A card already at the end it is moving towards is returned unchanged.
    /// </summary>
    public static IReadOnlyList<CardLayout> Move(IEnumerable<CardLayout> layout, string id, int delta)
    {
        var list = layout.OrderBy(c => c.Order).ToList();
        var index = list.FindIndex(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || delta == 0) return Renumber(list);

        var target = Math.Clamp(index + delta, 0, list.Count - 1);
        if (target == index) return Renumber(list);

        var moved = list[index];
        list.RemoveAt(index);
        list.Insert(target, moved);
        return Renumber(list);
    }

    /// <summary>Rewrite Order to a dense 0..n-1 sequence matching list position.</summary>
    private static IReadOnlyList<CardLayout> Renumber(List<CardLayout> list)
    {
        for (var i = 0; i < list.Count; i++) list[i].Order = i;
        return list;
    }
}
