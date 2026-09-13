using System.Globalization;
using System.Text;
using EDNexus.Core.State;

namespace EDNexus.Core.Discord;

/// <summary>
/// Pure translation from the live <see cref="CommanderState"/> to a <see cref="DiscordPresencePayload"/>.
/// Kept free of any I/O or Discord client so it can be unit tested by constructing a
/// <see cref="CommanderState"/> and asserting on the mapped fields.
/// </summary>
public static class DiscordPresenceMapper
{
    /// <summary>The "Get EDNexus" button shown on every presence, per issue #49.</summary>
    public static readonly DiscordPresenceButton GetEdNexusButton =
        new("Get EDNexus", "https://github.com/Signal-Thread-LLC/EDNexus");

    private const string DefaultLargeImageKey = "ednexus_logo";
    private const string DefaultLargeImageText = "EDNexus";

    /// <param name="state">The live commander picture. Read-only.</param>
    /// <param name="sessionStartedAt">When this play session began, for the elapsed-time fallback.</param>
    /// <param name="systemEnteredAt">
    /// When the commander arrived in <see cref="CommanderState.StarSystem"/>, so Discord can show
    /// elapsed time "in system" instead of elapsed time for the whole session.
    /// </param>
    public static DiscordPresencePayload Map(
        CommanderState state, DateTimeOffset sessionStartedAt, DateTimeOffset? systemEnteredAt = null)
    {
        var docked = state.Docked;
        var station = state.StationDisplayName;
        var system = state.StarSystem;
        var body = state.Body;

        var stateText = docked && !string.IsNullOrWhiteSpace(station)
            ? $"Docked at {station}"
            : BuildExploringState(system, body);

        var detailsText = BuildDetails(state);

        var largeImageKey = ShipImageKey(state.Ship) ?? DefaultLargeImageKey;
        var largeImageText = state.Ship ?? DefaultLargeImageText;

        var smallImageKey = docked ? "docked" : "cruising";
        var smallImageText = docked ? "Docked" : "In flight";

        var buttons = new List<DiscordPresenceButton> { GetEdNexusButton };
        if (!string.IsNullOrWhiteSpace(state.Name))
            buttons.Add(new DiscordPresenceButton(
                "View on Inara",
                $"https://inara.cz/elite/cmdrs/?search={Uri.EscapeDataString(state.Name!)}"));

        return new DiscordPresencePayload(
            State: stateText,
            Details: detailsText,
            LargeImageKey: largeImageKey,
            LargeImageText: largeImageText,
            SmallImageKey: smallImageKey,
            SmallImageText: smallImageText,
            StartedAt: systemEnteredAt ?? sessionStartedAt,
            Buttons: buttons);
    }

    private static string BuildExploringState(string? system, string? body)
    {
        if (string.IsNullOrWhiteSpace(system)) return "In the black";
        return !string.IsNullOrWhiteSpace(body) && !string.Equals(body, system, StringComparison.OrdinalIgnoreCase)
            ? $"Exploring {system} / {body}"
            : $"Exploring {system}";
    }

    private static string? BuildDetails(CommanderState state)
    {
        // A loaded hold is the more interesting "what are you doing" story than the ship name alone.
        if (state.CargoTons > 0)
            return $"Space Trucking: {state.CargoTons:0}t Cargo";

        if (string.IsNullOrWhiteSpace(state.Ship)) return null;

        return string.IsNullOrWhiteSpace(state.ShipIdent)
            ? $"Flying {state.Ship}"
            : $"Flying {state.Ship} ({state.ShipIdent})";
    }

    /// <summary>
    /// Discord asset keys are lowercase-with-underscores identifiers uploaded to the application's Art
    /// Assets page; this derives one from the journal's ship name (e.g. "Federal Corvette" →
    /// "federal_corvette") so a matching asset just needs to be uploaded under that key.
    /// </summary>
    private static string? ShipImageKey(string? ship)
    {
        if (string.IsNullOrWhiteSpace(ship)) return null;

        var sb = new StringBuilder(ship.Length);
        foreach (var c in ship.ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var key = sb.ToString().Trim('_');
        return key.Length > 0 ? key : null;
    }
}
