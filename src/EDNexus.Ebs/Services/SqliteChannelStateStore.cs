using System.Globalization;
using System.Text.Json;

namespace EDNexus.Ebs.Services;

/// <summary>
/// <see cref="IChannelStateStore"/> backed by <see cref="EbsDatabase"/>, so
/// <c>GET /api/initial-state/{channelId}</c> still answers with the last published state after the
/// EBS restarts instead of 404ing until the broadcaster's next update.
/// </summary>
public sealed class SqliteChannelStateStore : IChannelStateStore
{
    private readonly EbsDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqliteChannelStateStore(EbsDatabase database, TimeProvider? timeProvider = null)
    {
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        command.Parameters.AddWithValue("$updated", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public bool TryGet(string channelId, out JsonElement state)
    {
        using var connection = _database.Open();
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
}
