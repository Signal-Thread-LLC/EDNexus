using Microsoft.Data.Sqlite;

namespace EDNexus.Ebs.Services;

/// <summary>
/// The EBS's single SQLite file: owns the connection string and the schema, shared by
/// <see cref="SqliteBroadcasterTokenStore"/> and <see cref="SqliteChannelStateStore"/>. Runs in WAL
/// mode with the default <c>synchronous=FULL</c>, so a committed write survives a process crash and
/// a host power loss alike.
/// </summary>
public sealed class EbsDatabase
{
    /// <summary>File name of the database inside <c>Ebs:DataDirectory</c>.</summary>
    public const string FileName = "ebs.db";

    /// <summary>Schema version stamped into <c>PRAGMA user_version</c>. Bump it and add a migration step when the schema changes.</summary>
    public const int SchemaVersion = 1;

    private readonly string _connectionString;

    /// <summary>Opens (creating if needed) the database at <paramref name="databasePath"/> and brings its schema up to date.</summary>
    public EbsDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

        Migrate();
    }

    /// <summary>Absolute path of the database file.</summary>
    public string DatabasePath { get; }

    /// <summary>Opens a new pooled connection. Callers dispose it.</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Migrate()
    {
        using var connection = Open();

        using (var wal = connection.CreateCommand())
        {
            // Persistent per-file setting: readers don't block the single writer.
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteNonQuery();
        }

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());

        if (version > SchemaVersion)
            throw new InvalidOperationException(
                $"{DatabasePath} has schema version {version}, newer than this build supports ({SchemaVersion}). Refusing to start rather than risk corrupting it.");

        if (version == SchemaVersion)
            return;

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (version < 1)
        {
            // Twitch access/refresh tokens are Data Protection ciphertext; the EBS bearer token itself
            // is only ever stored as a SHA-256 hash. Timestamps are round-trip ("O") ISO-8601 text.
            command.CommandText =
                """
                CREATE TABLE broadcaster_tokens (
                    channel_id            TEXT PRIMARY KEY,
                    token_hash            TEXT NOT NULL UNIQUE,
                    username              TEXT NOT NULL,
                    twitch_access_token   TEXT NOT NULL,
                    twitch_refresh_token  TEXT NOT NULL,
                    twitch_expires_at     TEXT NOT NULL,
                    is_twitch_grant_valid INTEGER NOT NULL,
                    created_at            TEXT NOT NULL,
                    last_refreshed_at     TEXT NULL
                );

                CREATE TABLE channel_state (
                    channel_id TEXT PRIMARY KEY,
                    state_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        command.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
