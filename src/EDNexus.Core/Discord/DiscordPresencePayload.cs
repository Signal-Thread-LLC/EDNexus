namespace EDNexus.Core.Discord;

/// <summary>
/// A fully-resolved Discord Rich Presence snapshot, decoupled from the third-party
/// <c>DiscordRPC.RichPresence</c> shape so <see cref="DiscordPresenceMapper"/> can be unit tested
/// without a live Discord client (or even the DiscordRPC package) in the loop.
/// </summary>
/// <param name="State">Bottom line of the presence card (e.g. "Docked at Jameson Memorial").</param>
/// <param name="Details">Top line of the presence card (e.g. "Flying Anaconda (CMDR-1)").</param>
/// <param name="LargeImageKey">Asset key registered against the Discord application for the large image.</param>
/// <param name="LargeImageText">Tooltip shown when hovering the large image.</param>
/// <param name="SmallImageKey">Asset key registered against the Discord application for the small image.</param>
/// <param name="SmallImageText">Tooltip shown when hovering the small image.</param>
/// <param name="StartedAt">
/// When the current activity began — either the session start or the last system transition — so
/// Discord can render a live "elapsed" counter.
/// </param>
/// <param name="Buttons">Up to two clickable links shown under the presence card.</param>
public sealed record DiscordPresencePayload(
    string? State,
    string? Details,
    string? LargeImageKey,
    string? LargeImageText,
    string? SmallImageKey,
    string? SmallImageText,
    DateTimeOffset? StartedAt,
    IReadOnlyList<DiscordPresenceButton> Buttons)
{
    /// <summary>
    /// Value-equality that ignores <see cref="StartedAt"/> — a running elapsed-time clock must never
    /// by itself count as a "changed" presence, or the throttle would treat every tick as an update.
    /// </summary>
    public bool Equals(DiscordPresencePayload? other) =>
        other is not null
        && State == other.State
        && Details == other.Details
        && LargeImageKey == other.LargeImageKey
        && LargeImageText == other.LargeImageText
        && SmallImageKey == other.SmallImageKey
        && SmallImageText == other.SmallImageText
        && Buttons.SequenceEqual(other.Buttons);

    public override int GetHashCode() =>
        HashCode.Combine(State, Details, LargeImageKey, LargeImageText, SmallImageKey, SmallImageText);
}

/// <summary>One Discord Rich Presence button: a label plus the URL it opens.</summary>
public readonly record struct DiscordPresenceButton(string Label, string Url);
