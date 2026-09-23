namespace EDNexus.Core.Radio;

/// <summary>
/// UI-ready text and flags for one <see cref="RadioPlayerSnapshot"/>, shared by the title-bar
/// transport and the Space Radio dashboard card so both describe the player identically. Pure — no
/// UI types — so it is unit-testable from the Core test project.
/// </summary>
/// <param name="StatusLabel">Short upper-case status badge: LIVE, BUFFERING, PAUSED, OFFLINE, STOPPED, or OFF.</param>
/// <param name="StatusDetail">One line under the badge: the station blurb, the error, or a hint.</param>
/// <param name="PlayPauseGlyph">What clicking play/pause will do (see <see cref="RadioPlayerService.ToggleActionFor"/>).</param>
/// <param name="PlayPauseTooltip">Tooltip for the play/pause control, including what a click does.</param>
/// <param name="IsLive">Audio is playing — drives the blinking LIVE dot.</param>
/// <param name="IsBuffering">The stream is connecting or refilling its buffer.</param>
/// <param name="IsError">The stream failed; <see cref="StatusDetail"/> carries the reason.</param>
/// <param name="MuteGlyph">Speaker icon reflecting mute and volume level.</param>
/// <param name="MuteTooltip">Tooltip for the mute button, naming what a click does.</param>
public sealed record RadioDisplay(
    string StatusLabel,
    string StatusDetail,
    string PlayPauseGlyph,
    string PlayPauseTooltip,
    bool IsLive,
    bool IsBuffering,
    bool IsError,
    string MuteGlyph,
    string MuteTooltip)
{
    /// <summary>Glyph for <see cref="RadioToggleAction.Play"/>.</summary>
    public const string PlayGlyph = "▶";

    /// <summary>Glyph for <see cref="RadioToggleAction.Pause"/>.</summary>
    public const string PauseGlyph = "⏸";

    /// <summary>Glyph for <see cref="RadioToggleAction.Stop"/>.</summary>
    public const string StopGlyph = "⏹";

    /// <summary>Speaker glyph when muted or at zero volume.</summary>
    public const string MutedGlyph = "🔇";

    /// <summary>Speaker glyph below half volume.</summary>
    public const string QuietGlyph = "🔉";

    /// <summary>Speaker glyph at half volume and above.</summary>
    public const string LoudGlyph = "🔊";

    /// <summary>Tooltip for the developer-mode SIM marker while the real radio is quiet.</summary>
    public const string SimulationTooltip =
        "Developer mode: the radio controls drive a simulation. No audio plays and nothing is saved.";

    /// <summary>
    /// Tooltip for the developer-mode SIM marker, given the <em>real</em> player's snapshot. If the
    /// real stream is still playing or connecting, say so and how to stop it. Dev mode deliberately
    /// leaves it running: pausing it would clear the saved resume-on-launch intent.
    /// </summary>
    public static string SimulationNote(RadioPlayerSnapshot real)
    {
        if (real.Status is not (RadioPlaybackStatus.Playing or RadioPlaybackStatus.Buffering))
            return SimulationTooltip;

        var what = real.Station?.Name is { } name ? name : "a station";
        return $"Developer mode: the radio controls drive a simulation. The real radio is still playing {what} — "
               + "stop it in Settings → Radio player, or leave developer mode to control it.";
    }

    /// <summary>Describe <paramref name="s"/> for display.</summary>
    public static RadioDisplay From(RadioPlayerSnapshot s)
    {
        var name = s.Station?.Name;

        var glyph = RadioPlayerService.ToggleActionFor(s.Status) switch
        {
            RadioToggleAction.Pause => PauseGlyph,
            RadioToggleAction.Stop => StopGlyph,
            _ => PlayGlyph,
        };

        var tooltip = s.Status switch
        {
            RadioPlaybackStatus.Error => $"{s.LastError ?? "Radio error"} — click to stop",
            RadioPlaybackStatus.Buffering => $"Buffering {name}… — click to stop",
            RadioPlaybackStatus.Playing => $"Playing {name}",
            RadioPlaybackStatus.Paused => $"Paused — {name}",
            _ => name is null ? "Play the radio" : $"Play {name}",
        };

        var label = s.Status switch
        {
            RadioPlaybackStatus.Playing => "LIVE",
            RadioPlaybackStatus.Buffering => "BUFFERING",
            RadioPlaybackStatus.Paused => "PAUSED",
            RadioPlaybackStatus.Error => "OFFLINE",
            _ => s.Enabled ? "STOPPED" : "OFF",
        };

        var detail = s.Status switch
        {
            RadioPlaybackStatus.Error => string.IsNullOrWhiteSpace(s.LastError) ? "The stream could not be played." : s.LastError!,
            RadioPlaybackStatus.Buffering => name is null ? "Connecting…" : $"Connecting to {name}…",
            RadioPlaybackStatus.Paused => name is null ? "Paused." : $"Paused — press play to resume {name}.",
            _ => s.Station?.Description ?? "Pick a station and press play.",
        };

        var silent = s.Muted || s.Volume <= 0;
        var muteGlyph = silent ? MutedGlyph : s.Volume < 50 ? QuietGlyph : LoudGlyph;

        return new RadioDisplay(
            label,
            detail,
            glyph,
            tooltip,
            IsLive: s.Status == RadioPlaybackStatus.Playing,
            IsBuffering: s.Status == RadioPlaybackStatus.Buffering,
            IsError: s.Status == RadioPlaybackStatus.Error,
            muteGlyph,
            s.Muted ? "Unmute" : "Mute");
    }
}
