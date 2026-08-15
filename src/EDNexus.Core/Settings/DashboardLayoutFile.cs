using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDNexus.Core.Settings;

/// <summary>
/// A dashboard arrangement in its portable, shareable form: just the card layout, and nothing else
/// from <see cref="AppSettings"/>.
/// </summary>
/// <remarks>
/// Settings live in per-user local app data rather than a cloud-synced folder, because the file
/// also holds the Inara API key and is rewritten on every layout tweak. Exporting the layout on its
/// own is how an arrangement moves between machines without carrying a credential with it or
/// inviting sync conflicts on a churning file.
/// </remarks>
public sealed class DashboardLayoutDocument
{
    /// <summary>Format version, so a future change can migrate rather than reject.</summary>
    public int Version { get; set; } = DashboardLayoutFile.CurrentVersion;

    /// <summary>Marker identifying this as an EDNexus layout, to reject unrelated JSON politely.</summary>
    public string Kind { get; set; } = DashboardLayoutFile.KindMarker;

    public List<CardLayout> Cards { get; set; } = new();
}

/// <summary>Reads and writes a <see cref="DashboardLayoutDocument"/> as JSON.</summary>
public static class DashboardLayoutFile
{
    public const int CurrentVersion = 1;
    public const string KindMarker = "ednexus.dashboard-layout";

    /// <summary>Suggested file name when exporting.</summary>
    public const string DefaultFileName = "ednexus-dashboard-layout.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Serialise a layout for export.</summary>
    public static string Write(IEnumerable<CardLayout> layout)
        => JsonSerializer.Serialize(
            new DashboardLayoutDocument { Cards = layout.Select(Copy).ToList() }, Options);

    /// <summary>
    /// Parse an exported layout. Returns null for anything that isn't one — a hand-edited file, a
    /// different app's JSON, or a newer format version — so a bad import is a no-op rather than a
    /// wrecked dashboard.
    /// </summary>
    public static IReadOnlyList<CardLayout>? TryRead(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        DashboardLayoutDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<DashboardLayoutDocument>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (doc is null) return null;
        if (!string.Equals(doc.Kind, KindMarker, StringComparison.OrdinalIgnoreCase)) return null;
        if (doc.Version is < 1 or > CurrentVersion) return null;

        // Entries without an id can't be matched to a card, so they'd only corrupt the ordering.
        var cards = doc.Cards.Where(c => !string.IsNullOrWhiteSpace(c.Id)).Select(Copy).ToList();
        return cards.Count == 0 ? null : cards;
    }

    /// <summary>Defensive copy, so an imported document can't alias the live layout objects.</summary>
    private static CardLayout Copy(CardLayout c) => new()
    {
        Id = c.Id,
        Order = c.Order,
        Visible = c.Visible,
        Width = c.Width,
        Collapsed = c.Collapsed,
    };
}
