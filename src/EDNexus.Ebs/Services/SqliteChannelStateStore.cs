using System.Globalization;
using System.Text.Json;

namespace EDNexus.Ebs.Services;

/// <summary>
/// <see cref="IChannelStateStore"/> backed by <see cref="EbsDatabase"/>, so
/// <c>GET /api/initial-state/{channelId}</c> still answers with the last published state after the
/// EBS restarts instead of 404ing until the broadcaster's next update. Rows older than the
/// configured maximum age are treated as absent and pruned, so a snapshot whose clear never
/// arrived does not stay public forever.
/// </summary>
public sealed class SqliteChannelStateStore : IChannelStateStore
{
    private readonly EbsDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan? _maxAge;

    /// <param name="maxAge">Oldest snapshot served; null keeps snapshots until they are removed.</param>
    public SqliteChannelStateStore(EbsDatabase database, TimeProvider? timeProvider = null, TimeSpan? maxAge = null)
    {
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxAge = maxAge;
    }

    /// <inheritdoc />
    public void Set(string channelId, JsonElement state)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO channel_state (channel_id, state_json, updated_at) VALUES ($channel, $state, $updated)
            ON CONFLICT(channel_id) DO UPDATE SET state_json = excluded.state_json, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$channel", channelId);
        command.Parameters.AddWithValue("$state", state.GetRawText());
        command.Parameters.AddWithValue("$updated", Timestamp(_timeProvider.GetUtcNow()));
        command.ExecuteNonQuery();

        PruneExpired(connection, channelId: null);
    }

    /// <inheritdoc />
    public bool TryGet(string channelId, out JsonElement state)
    {
        using var connection = _database.Open();
        PruneExpired(connection, channelId);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state_json FROM channel_state WHERE channel_id = $channel;";
        command.Parameters.AddWithValue("$channel", channelId);

        if (command.ExecuteScalar() is not string json)
        {
            state = default;
            return false;
        }

        using var document = JsonDocument.Parse(json);
        state = document.RootElement.Clone();
        return true;
    }

    /// <inheritdoc />
    public void Remove(string channelId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM channel_state WHERE channel_id = $channel;";
        command.Parameters.AddWithValue("$channel", channelId);
        command.ExecuteNonQuery();
    }

    /// <summary>Deletes snapshots past the maximum age: one channel's, or every channel's when null.</summary>
    private void PruneExpired(Microsoft.Data.Sqlite.SqliteConnection connection, string? channelId)
    {
        if (_maxAge is not { } maxAge) return;

        using var command = connection.CreateCommand();
        command.CommandText = channelId is null
            ? "DELETE FROM channel_state WHERE updated_at < $cutoff;"
            : "DELETE FROM channel_state WHERE channel_id = $channel AND updated_at < $cutoff;";
        // Round-trip ("O") UTC timestamps are fixed-width, so they compare correctly as text.
        command.Parameters.AddWithValue("$cutoff", Timestamp(_timeProvider.GetUtcNow() - maxAge));
        if (channelId is not null) command.Parameters.AddWithValue("$channel", channelId);
        command.ExecuteNonQuery();
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
