namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// A read-only view of a single journal (or status) event, handed to plugins in place of the
/// host's internal <c>JournalEntry</c>. Exposes the same defensive scalar accessors so plugins
/// never need to know the underlying JSON representation, and never get a mutable handle to it.
/// </summary>
public interface IJournalEvent
{
    /// <summary>The journal <c>event</c> field, e.g. <c>"FSDJump"</c> or <c>"MarketSell"</c>.</summary>
    string Event { get; }

    /// <summary>The event's <c>timestamp</c> field, or <see cref="DateTimeOffset.MinValue"/> if absent/unparsable.</summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// True when this event was replayed from an existing journal file at startup rather than
    /// observed live. Plugins should typically suppress alerts/notifications for historical events.
    /// </summary>
    bool IsHistorical { get; }

    /// <summary>Reads a string field, or <see langword="null"/> if it is missing or not a string.</summary>
    string? GetString(string field);

    /// <summary>Reads an integer field, or <see langword="null"/> if it is missing or not a number.</summary>
    long? GetInt64(string field);

    /// <summary>Reads a floating-point field, or <see langword="null"/> if it is missing or not a number.</summary>
    double? GetDouble(string field);

    /// <summary>Reads a boolean field, or <see langword="null"/> if it is missing or not a boolean.</summary>
    bool? GetBool(string field);

    /// <summary>
    /// Reads the user-facing "<paramref name="field"/>_Localised" variant of a field, falling back
    /// to the raw field when no localised form exists. Prefer this over <see cref="GetString"/> for
    /// anything shown to the user.
    /// </summary>
    string? GetLocalised(string field);

    /// <summary>
    /// Deserializes the entire event payload into <typeparamref name="T"/>, or <see langword="null"/>
    /// if it does not match the shape of <typeparamref name="T"/>. Use the scalar accessors instead
    /// where possible — journal event shapes change between game updates, and reading only the
    /// fields you need is more resilient than deserializing the whole payload.
    /// </summary>
    T? Deserialize<T>();
}
