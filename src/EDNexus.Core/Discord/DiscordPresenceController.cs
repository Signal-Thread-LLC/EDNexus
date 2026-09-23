using EDNexus.Core.Settings;
using EDNexus.Core.State;

namespace EDNexus.Core.Discord;

/// <summary>
/// Owns the lifetime of the <see cref="DiscordPresenceService"/> so the commander's Discord settings
/// can be changed live: turning presence off tears the service down (clearing the presence and closing
/// the IPC connection to Discord), turning it back on reconnects with a fresh client, and privacy
/// changes are pushed onto the running service without a reconnect.
/// </summary>
/// <remarks>
/// <see cref="Apply"/> is called from the UI thread while <see cref="CommanderState"/> changes arrive
/// on the journal thread; the swap is serialised here and the service guards its own sends.
/// </remarks>
public sealed class DiscordPresenceController : IDisposable
{
    private readonly CommanderState _state;
    private readonly Func<IDiscordRpcClient> _clientFactory;
    private readonly Func<bool>? _isSuppressed;
    private readonly TimeSpan _minInterval;
    private readonly object _gate = new();

    private DiscordPresenceService? _service;
    private bool _disposed;

    /// <param name="state">The live commander picture to mirror. Never written to.</param>
    /// <param name="clientFactory">
    /// Creates a fresh Discord transport each time presence is (re-)enabled. It must not throw; return
    /// <see cref="NoOpDiscordRpcClient.Instance"/> when the real client can't be constructed.
    /// </param>
    /// <param name="isSuppressed">Forwarded to the service; see <see cref="DiscordPresenceService"/>.</param>
    public DiscordPresenceController(
        CommanderState state, Func<IDiscordRpcClient> clientFactory, Func<bool>? isSuppressed = null)
        : this(state, clientFactory, isSuppressed, DiscordPresenceService.DefaultMinInterval) { }

    /// <summary>Test-only constructor: a shortened throttle window for the services it creates.</summary>
    internal DiscordPresenceController(
        CommanderState state, Func<IDiscordRpcClient> clientFactory, Func<bool>? isSuppressed, TimeSpan minInterval)
    {
        _state = state;
        _clientFactory = clientFactory;
        _isSuppressed = isSuppressed;
        _minInterval = minInterval;
    }

    /// <summary>True while presence is enabled and a service is connected (or trying to connect).</summary>
    public bool IsActive
    {
        get { lock (_gate) return _service is not null; }
    }

    /// <summary>The privacy choices on the running service, or null while presence is off.</summary>
    public DiscordPrivacyOptions? Privacy
    {
        get { lock (_gate) return _service?.Privacy; }
    }

    /// <summary>
    /// Bring the live integration in line with <paramref name="settings"/>: start, stop, or re-apply
    /// privacy. Idempotent — calling it again with unchanged settings does nothing.
    /// </summary>
    public void Apply(DiscordSettings settings)
    {
        var privacy = DiscordPrivacyOptions.From(settings);
        DiscordPresenceService? retired = null;

        lock (_gate)
        {
            if (_disposed) return;

            if (!settings.Enabled)
            {
                retired = _service;
                _service = null;
            }
            else if (_service is null)
            {
                IDiscordRpcClient client;
                try { client = _clientFactory(); }
                catch { client = NoOpDiscordRpcClient.Instance; }   // never let a bad transport throw out
                _service = new DiscordPresenceService(_state, client, _minInterval, _isSuppressed, null, privacy);
            }
            else
            {
                _service.UpdatePrivacy(privacy);
            }
        }

        // Clears the presence and closes the pipe; done outside the lock since it talks to Discord.
        retired?.Dispose();
    }

    public void Dispose()
    {
        DiscordPresenceService? retired;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            retired = _service;
            _service = null;
        }
        retired?.Dispose();
    }
}
