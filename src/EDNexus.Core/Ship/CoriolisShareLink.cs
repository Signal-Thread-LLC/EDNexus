using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EDNexus.Core.Ship;

/// <summary>
/// Builds a shareable coriolis.io build link straight from the journal's <c>Loadout</c> event — no
/// module catalog of our own is needed. Coriolis's importer recognises this shape directly (it detects
/// a raw journal loadout by the presence of a non-empty <c>Modules</c> array) and its desktop-app
/// integrations (e.g. EDMarketConnector) pass the event through unmodified: gzip the compact JSON, then
/// URL-safe base64 it into <c>coriolis.io/import?data=</c>.
/// </summary>
public static class CoriolisShareLink
{
    private const string ImportUrl = "https://coriolis.io/import?data=";

    /// <summary>
    /// Build the share link from the raw text of a <c>Loadout</c> event, or null if it isn't a Loadout
    /// event or doesn't carry a module list Coriolis could import.
    /// </summary>
    public static string? Build(string? loadoutJson)
    {
        if (string.IsNullOrWhiteSpace(loadoutJson)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(loadoutJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.String ||
                ev.GetString() != "Loadout")
                return null;
            if (!root.TryGetProperty("Modules", out var modules) ||
                modules.ValueKind != JsonValueKind.Array || modules.GetArrayLength() == 0)
                return null;

            return ImportUrl + Encode(root.GetRawText());
        }
    }

    private static string Encode(string json)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(json));

        return Convert.ToBase64String(compressed.ToArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "%3D");
    }
}
