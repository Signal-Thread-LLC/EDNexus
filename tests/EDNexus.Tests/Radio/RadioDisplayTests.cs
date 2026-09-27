using EDNexus.Core.Radio;
using Xunit;

namespace EDNexus.Tests.Radio;

public class RadioDisplayTests
{
    private static readonly RadioStation Station = RadioStationCatalog.Stations[0];

    private static RadioPlayerSnapshot Snap(
        RadioPlaybackStatus status, RadioStation? station = null, int volume = 50, bool muted = false,
        string? error = null, bool enabled = true)
        => new(enabled, station, status, volume, muted, error);

    [Theory]
    [InlineData(RadioPlaybackStatus.Playing, RadioDisplay.PauseGlyph)]
    [InlineData(RadioPlaybackStatus.Buffering, RadioDisplay.StopGlyph)]
    [InlineData(RadioPlaybackStatus.Error, RadioDisplay.StopGlyph)]
    [InlineData(RadioPlaybackStatus.Paused, RadioDisplay.PlayGlyph)]
    [InlineData(RadioPlaybackStatus.Stopped, RadioDisplay.PlayGlyph)]
    public void Play_pause_glyph_shows_what_the_toggle_will_do(RadioPlaybackStatus status, string glyph)
        => Assert.Equal(glyph, RadioDisplay.From(Snap(status, Station)).PlayPauseGlyph);

    [Fact]
    public void Glyph_agrees_with_the_service_toggle_action_for_every_status()
    {
        foreach (var status in Enum.GetValues<RadioPlaybackStatus>())
        {
            var expected = RadioPlayerService.ToggleActionFor(status) switch
            {
                RadioToggleAction.Pause => RadioDisplay.PauseGlyph,
                RadioToggleAction.Stop => RadioDisplay.StopGlyph,
                _ => RadioDisplay.PlayGlyph,
            };
            Assert.Equal(expected, RadioDisplay.From(Snap(status, Station)).PlayPauseGlyph);
        }
    }

    [Theory]
    [InlineData(RadioPlaybackStatus.Playing, "LIVE")]
    [InlineData(RadioPlaybackStatus.Buffering, "BUFFERING")]
    [InlineData(RadioPlaybackStatus.Paused, "PAUSED")]
    [InlineData(RadioPlaybackStatus.Error, "OFFLINE")]
    [InlineData(RadioPlaybackStatus.Stopped, "STOPPED")]
    public void Status_label_names_the_state(RadioPlaybackStatus status, string label)
        => Assert.Equal(label, RadioDisplay.From(Snap(status, Station)).StatusLabel);

    [Fact]
    public void A_never_enabled_radio_reads_off()
        => Assert.Equal("OFF", RadioDisplay.From(Snap(RadioPlaybackStatus.Stopped, enabled: false)).StatusLabel);

    [Fact]
    public void Only_playing_is_live_and_state_flags_are_exclusive()
    {
        foreach (var status in Enum.GetValues<RadioPlaybackStatus>())
        {
            var d = RadioDisplay.From(Snap(status, Station));
            Assert.Equal(status == RadioPlaybackStatus.Playing, d.IsLive);
            Assert.Equal(status == RadioPlaybackStatus.Buffering, d.IsBuffering);
            Assert.Equal(status == RadioPlaybackStatus.Error, d.IsError);
        }
    }

    [Fact]
    public void Error_detail_carries_the_reason_with_a_fallback()
    {
        Assert.Equal("boom", RadioDisplay.From(Snap(RadioPlaybackStatus.Error, Station, error: "boom")).StatusDetail);
        Assert.False(string.IsNullOrWhiteSpace(RadioDisplay.From(Snap(RadioPlaybackStatus.Error, Station)).StatusDetail));
        Assert.Contains("boom", RadioDisplay.From(Snap(RadioPlaybackStatus.Error, Station, error: "boom")).PlayPauseTooltip);
    }

    [Fact]
    public void Playing_detail_is_the_station_blurb_and_no_station_prompts_a_pick()
    {
        Assert.Equal(Station.Description, RadioDisplay.From(Snap(RadioPlaybackStatus.Playing, Station)).StatusDetail);
        Assert.Equal("Pick a station and press play.", RadioDisplay.From(Snap(RadioPlaybackStatus.Stopped)).StatusDetail);
        Assert.Equal("Play the radio", RadioDisplay.From(Snap(RadioPlaybackStatus.Stopped)).PlayPauseTooltip);
    }

    [Theory]
    [InlineData(80, false, RadioDisplay.LoudGlyph, "Mute")]
    [InlineData(50, false, RadioDisplay.LoudGlyph, "Mute")]
    [InlineData(20, false, RadioDisplay.QuietGlyph, "Mute")]
    [InlineData(0, false, RadioDisplay.MutedGlyph, "Mute")]
    [InlineData(80, true, RadioDisplay.MutedGlyph, "Unmute")]
    public void Mute_glyph_tracks_mute_and_level(int volume, bool muted, string glyph, string tooltip)
    {
        var d = RadioDisplay.From(Snap(RadioPlaybackStatus.Playing, Station, volume, muted));
        Assert.Equal(glyph, d.MuteGlyph);
        Assert.Equal(tooltip, d.MuteTooltip);
    }
}
