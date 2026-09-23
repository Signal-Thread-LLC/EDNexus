using EDNexus.Core.Settings;

namespace EDNexus.Core.Discord;

/// <summary>
/// The commander's Discord Rich Presence privacy choices, as consumed by
/// <see cref="DiscordPresenceMapper"/>. A value snapshot of the relevant <see cref="DiscordSettings"/>
/// flags so the mapper stays pure and the service can swap them atomically when Settings change.
/// </summary>
/// <param name="ShowSystem">
/// When false, no star system, body, station, or carrier name is emitted, and the elapsed timer counts
/// from session start rather than from the last jump (which would otherwise reveal jump timing).
/// </param>
/// <param name="ShowCommander">
/// When false, nothing that identifies the commander is emitted: no "View on Inara" button (it embeds
/// the commander name) and no custom ship ident.
/// </param>
public readonly record struct DiscordPrivacyOptions(bool ShowSystem, bool ShowCommander)
{
    /// <summary>Everything visible — the shipped default.</summary>
    public static DiscordPrivacyOptions Default => new(ShowSystem: true, ShowCommander: true);

    /// <summary>Snapshot the privacy flags from the persisted settings.</summary>
    public static DiscordPrivacyOptions From(DiscordSettings settings) =>
        new(settings.ShowSystem, settings.ShowCommander);
}
