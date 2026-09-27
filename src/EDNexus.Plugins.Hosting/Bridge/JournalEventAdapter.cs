using System.Text.Json;
using EDNexus.Core.Journal;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting.Bridge;

/// <summary>
/// The <see cref="IJournalEvent"/> a plugin receives: a read-only view over a host
/// <see cref="JournalEntry"/>. The entry's payload is a detached, immutable <see cref="JsonElement"/>,
/// and the entry itself is never exposed, so a plugin can read fields but not reach engine types.
/// </summary>
internal sealed class JournalEventAdapter(JournalEntry entry, bool isSimulated) : IJournalEvent
{
    private readonly JournalEntry _entry = entry;

    public string Event => _entry.Event;

    public DateTimeOffset Timestamp => _entry.Timestamp;

    public bool IsHistorical => _entry.IsHistorical;

    public bool IsSimulated { get; } = isSimulated;

    public string? GetString(string field) => field is null ? null : _entry.GetString(field);

    public long? GetInt64(string field) => field is null ? null : _entry.GetInt64(field);

    public double? GetDouble(string field) => field is null ? null : _entry.GetDouble(field);

    public bool? GetBool(string field) => field is null ? null : _entry.GetBool(field);

    public string? GetLocalised(string field) => field is null ? null : _entry.GetLocalised(field);

    /// <summary>
    /// Deserializes the payload, returning <see langword="default"/> when it does not fit
    /// <typeparamref name="T"/> — as the SDK contract promises — instead of throwing into the plugin.
    /// </summary>
    public T? Deserialize<T>()
    {
        try { return _entry.Deserialize<T>(); }
        catch (JsonException) { return default; }
        catch (NotSupportedException) { return default; }
        catch (InvalidOperationException) { return default; }
    }

    public override string ToString() => _entry.Event;
}
