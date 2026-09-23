using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EDNexus.Core.Dev;
using EDNexus.Core.Radio;
using EDNexus.Core.State;

namespace EDNexus.App.ViewModels;

/// <summary>
/// Space Radio: the full player for the built-in stations — live/buffering/error status, play/pause,
/// previous/next, a station picker, and volume with mute. Every control goes through the same
/// <see cref="IRadioPlayer"/> methods the title-bar transport uses (<see cref="RadioPlayerService"/>
/// in live use), so play/pause from either place keeps the persisted resume-on-launch intent correct.
/// </summary>
/// <remarks>
/// While developer mode is on, the card drives a <see cref="SimulatedRadioPlayer"/> instead — fed by
/// <see cref="RadioSampleSource"/> through the real bus via the 🎲 — so its states and controls can be
/// exercised without starting audio, touching the network, or rewriting the saved radio settings.
/// The title-bar transport keeps controlling the real player throughout.
/// </remarks>
public sealed partial class RadioCardViewModel : CardViewModel
{
    // Slider drags fire a change per step; coalesce them so the real player (which persists every
    // volume change to settings.json) gets one write per gesture rather than dozens.
    private static readonly TimeSpan VolumeDebounce = TimeSpan.FromMilliseconds(200);

    private SimulatedRadioPlayer? _simulated;
    private DispatcherTimer? _volumeTimer;
    private int? _pendingVolume;
    private bool _syncing;

    public RadioCardViewModel(DashboardContext context) : base(context, "radio", "SPACE RADIO", 452)
    {
        _simulated = CreateSimulation();
    }

    /// <summary>The player the controls act on: the simulation in developer mode, otherwise the real one.</summary>
    private IRadioPlayer Player
        => Context.DevEnabled && _simulated is not null ? _simulated : Context.Host.Radio;

    /// <summary>
    /// The simulation only exists when the dev tools are compiled in. It listens on the current host's
    /// bus, which is where the 🎲 publishes <see cref="RadioSampleSource"/> events.
    /// </summary>
    private SimulatedRadioPlayer? CreateSimulation()
    {
        if (!FeatureFlags.DeveloperTools) return null;
        var sim = new SimulatedRadioPlayer();
        sim.Attach(Context.Host.Bus);
        return sim;
    }

    /// <summary>Every station on offer, in catalog order, for the picker.</summary>
    public IReadOnlyList<RadioStation> Stations => RadioStationCatalog.Stations;

    [ObservableProperty] private RadioStation? _selectedStation;
    [ObservableProperty] private double _volume = 50;
    [ObservableProperty] private bool _isMuted;

    [ObservableProperty] private string _statusLabel = "OFF";
    [ObservableProperty] private string _statusDetail = "Pick a station and press play.";
    [ObservableProperty] private string _playPauseGlyph = RadioDisplay.PlayGlyph;
    [ObservableProperty] private string _playPauseTooltip = "Play the radio";
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private bool _isBuffering;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _muteGlyph = RadioDisplay.LoudGlyph;
    [ObservableProperty] private string _muteTooltip = "Mute";
    [ObservableProperty] private string _volumeText = "50";

    /// <summary>True while the card shows the developer-mode simulation rather than the real player.</summary>
    [ObservableProperty] private bool _isSimulated;

    /// <summary>
    /// The radio has nothing to do with commander state; the tick just re-reads the player snapshot,
    /// which keeps the card in step with the title bar, media keys, and background stream events.
    /// </summary>
    public override void Update(CommanderState state) => Apply(Player.Snapshot);

    /// <summary>
    /// The host was rebuilt (reset to live / leaving developer mode): drop the fabricated player
    /// state and listen on the new bus.
    /// </summary>
    public override void Reset()
    {
        _volumeTimer?.Stop();
        _pendingVolume = null;
        _simulated = CreateSimulation();
    }

    private void Apply(RadioPlayerSnapshot s)
    {
        var d = RadioDisplay.From(s);

        _syncing = true;
        try
        {
            IsSimulated = Context.DevEnabled && _simulated is not null;
            StatusLabel = d.StatusLabel;
            StatusDetail = d.StatusDetail;
            PlayPauseGlyph = d.PlayPauseGlyph;
            PlayPauseTooltip = d.PlayPauseTooltip;
            IsLive = d.IsLive;
            IsBuffering = d.IsBuffering;
            IsError = d.IsError;
            MuteGlyph = d.MuteGlyph;
            MuteTooltip = d.MuteTooltip;
            IsMuted = s.Muted;
            SelectedStation = s.Station;

            // Don't yank the slider back mid-drag: a pending value wins until it has been applied.
            if (_pendingVolume is null)
            {
                Volume = s.Volume;
                VolumeText = s.Volume.ToString();
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Picking a station tunes to it, the same as a radio dial.</summary>
    partial void OnSelectedStationChanged(RadioStation? value)
    {
        if (_syncing || value is null) return;
        _ = Player.PlayAsync(value.Id);
    }

    partial void OnVolumeChanged(double value)
    {
        if (_syncing) return;

        var level = (int)Math.Round(Math.Clamp(value, 0, 100));
        _pendingVolume = level;
        VolumeText = level.ToString();

        _volumeTimer ??= CreateVolumeTimer();
        _volumeTimer.Stop();
        _volumeTimer.Start();
    }

    private DispatcherTimer CreateVolumeTimer()
    {
        var timer = new DispatcherTimer { Interval = VolumeDebounce };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_pendingVolume is not { } level) return;
            _pendingVolume = null;
            _ = Player.SetVolumeAsync(level);
        };
        return timer;
    }

    /// <summary>Same entry point as the title-bar button and the Play/Pause media key.</summary>
    [RelayCommand]
    private Task PlayPause() => Player.TogglePlayPauseAsync();

    [RelayCommand]
    private Task NextStation() => Player.NextStationAsync();

    [RelayCommand]
    private Task PreviousStation() => Player.PreviousStationAsync();

    [RelayCommand]
    private Task ToggleMute() => Player.SetMuteAsync(!Player.Snapshot.Muted);
}
