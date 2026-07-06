using System.Text.Json;

namespace EliteDangerous.Eddn;

/// <summary>
/// The rolling context EDDN messages need but individual events don't always carry: who is
/// uploading, which game version produced the data, and where the commander currently is. The
/// caller feeds every journal event through <see cref="Observe"/>; the transformer then reads
/// these fields to fill the header and augment messages that are missing location data.
/// </summary>
public sealed class EddnState
{
    /// <summary>Raw commander name, used as the header <c>uploaderID</c> (the relay obfuscates it).</summary>
    public string? CommanderName { get; set; }

    /// <summary>Game version string, from <c>Fileheader</c> / <c>LoadGame</c>.</summary>
    public string? GameVersion { get; set; }

    /// <summary>Game build string, from <c>Fileheader</c> / <c>LoadGame</c>.</summary>
    public string? GameBuild { get; set; }

    /// <summary>Whether the current session is Horizons, from <c>LoadGame</c>. Null until known.</summary>
    public bool? Horizons { get; set; }

    /// <summary>Whether the current session is Odyssey, from <c>LoadGame</c>. Null until known.</summary>
    public bool? Odyssey { get; set; }

    /// <summary>Current star system name, tracked from location-bearing events.</summary>
    public string? StarSystem { get; set; }

    /// <summary>Current system address (64-bit id), tracked from location-bearing events.</summary>
    public long? SystemAddress { get; set; }

    /// <summary>Current galactic coordinates <c>[x, y, z]</c>, tracked from location-bearing events.</summary>
    public double[]? StarPos { get; set; }

    /// <summary>
    /// Updates the rolling context from a journal event. Safe to call for every event — it only
    /// reacts to the ones that carry identity, game version, or location.
    /// </summary>
    public void Observe(JsonElement e)
    {
        if (!e.TryGetProperty("event", out var evEl) || evEl.ValueKind != JsonValueKind.String) return;
        var ev = evEl.GetString();

        switch (ev)
        {
            case "Fileheader":
                GameVersion = Str(e, "gameversion") ?? GameVersion;
                GameBuild = Str(e, "build") ?? GameBuild;
                break;

            case "LoadGame":
                CommanderName = Str(e, "Commander") ?? CommanderName;
                GameVersion = Str(e, "gameversion") ?? GameVersion;
                GameBuild = Str(e, "build") ?? GameBuild;
                if (Bool(e, "Horizons") is bool h) Horizons = h;
                if (Bool(e, "Odyssey") is bool o) Odyssey = o;
                break;

            case "Commander":
                CommanderName = Str(e, "Name") ?? CommanderName;
                break;
        }

        // Advance the location fix only on events that carry coordinates (FSDJump/Location/
        // CarrierJump). Updating the system name/address from a StarPos-less event would leave
        // StarPos pointing at the *previous* system, and the augmentation cross-check would then
        // happily stamp an event with the wrong coordinates. Keep the triple internally consistent.
        if (ReadStarPos(e) is double[] pos)
        {
            StarPos = pos;
            if (Str(e, "StarSystem") is string sys) StarSystem = sys;
            if (Int64(e, "SystemAddress") is long addr) SystemAddress = addr;
        }
    }

    internal static string? Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static long? Int64(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    internal static bool? Bool(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    internal static double[]? ReadStarPos(JsonElement e)
    {
        if (!e.TryGetProperty("StarPos", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var pos = new double[3];
        var i = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (i > 2 || !el.TryGetDouble(out var d)) return null;
            pos[i++] = d;
        }
        return i == 3 ? pos : null;
    }
}
