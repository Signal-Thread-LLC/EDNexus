using EDNexus.Core.Journal;
using EDNexus.Core.Settings;
using EliteDangerous.Eddn;

namespace EDNexus.Core.Reporting;

/// <summary>
/// Connects the journal event bus to the reusable <see cref="EliteDangerous.Eddn"/> client. It warms
/// the EDDN context from every event (including historical replay) so location augmentation is ready,
/// but only uploads <b>live</b> events, and only while the EDDN reporter is enabled in settings.
/// </summary>
public sealed class EddnBridge : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<bool> _isSuppressed;
    private readonly EddnState _state = new();
    private readonly EddnJournalTransformer _transformer;
    private readonly EddnUploader _uploader;

    public EddnBridge(JournalEventBus bus, AppSettings settings, EddnUploader uploader,
        EddnJournalTransformer transformer, Func<bool>? isSuppressed = null)
    {
        _settings = settings;
        _uploader = uploader;
        _transformer = transformer;
        _isSuppressed = isSuppressed ?? (static () => false);

        bus.SubscribeAny(OnEvent);
    }

    private void OnEvent(JournalEntry e)
    {
        // While developer mode is fabricating events onto the bus, stay completely out of the way:
        // don't upload and don't even warm the rolling context, so synthetic data can't leak to EDDN
        // or corrupt the location fix used to augment real events later.
        if (_isSuppressed()) return;

        // Warm the rolling context from all events — even historical — so a live event that arrives
        // before the next FSDJump/LoadGame can still be augmented and identified.
        _state.Observe(e.Raw);

        if (e.IsHistorical) return;                       // never re-upload replayed history
        if (!_settings.Reporting.Eddn.Enabled) return;    // opt-in, read live so toggles take effect

        var message = _transformer.Transform(e.Raw, _state);
        if (message is not null) _uploader.Enqueue(message);
    }

    public ValueTask DisposeAsync() => _uploader.DisposeAsync();
}
