using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HorrorHarbor.Data.UserData;

internal static class UserDataSchema
{
    public const int CurrentVersion = 1;

    private const string CreateSql = """
        CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER PRIMARY KEY,
            applied_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS plex_server (
            connection_id TEXT PRIMARY KEY,
            server_url TEXT NOT NULL,
            allow_insecure_http INTEGER NOT NULL CHECK (allow_insecure_http IN (0, 1)),
            machine_identifier TEXT NOT NULL,
            name TEXT NOT NULL,
            version TEXT NOT NULL,
            last_synced_utc TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS plex_history (
            history_id INTEGER PRIMARY KEY AUTOINCREMENT,
            connection_id TEXT NOT NULL,
            rating_key TEXT NOT NULL,
            media_type TEXT NOT NULL,
            title TEXT NOT NULL,
            release_year INTEGER NULL,
            plex_guid TEXT NULL,
            viewed_at_utc TEXT NOT NULL,
            view_offset_ms INTEGER NULL,
            duration_ms INTEGER NULL,
            normalized_title TEXT NOT NULL,
            watch_exclusion_key TEXT NOT NULL,
            UNIQUE(connection_id, rating_key, viewed_at_utc),
            FOREIGN KEY(connection_id) REFERENCES plex_server(connection_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_plex_history_viewed_at ON plex_history(viewed_at_utc);
        CREATE TABLE IF NOT EXISTS feedback_event (
            event_id INTEGER PRIMARY KEY AUTOINCREMENT,
            candidate_id TEXT NOT NULL,
            feedback INTEGER NOT NULL,
            created_utc TEXT NOT NULL,
            is_undone INTEGER NOT NULL DEFAULT 0 CHECK (is_undone IN (0, 1))
        );
        CREATE INDEX IF NOT EXISTS ix_feedback_candidate ON feedback_event(candidate_id, event_id DESC);
        CREATE TABLE IF NOT EXISTS user_setting (
            setting_key TEXT PRIMARY KEY,
            setting_value TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );
        """;

    private const string CreateEncryptedSchemaSql = """
        CREATE TABLE encrypted.schema_version (
            version INTEGER PRIMARY KEY,
            applied_utc TEXT NOT NULL
        );
        CREATE TABLE encrypted.plex_server (
            connection_id TEXT PRIMARY KEY,
            server_url TEXT NOT NULL,
            allow_insecure_http INTEGER NOT NULL CHECK (allow_insecure_http IN (0, 1)),
            machine_identifier TEXT NOT NULL,
            name TEXT NOT NULL,
            version TEXT NOT NULL,
            last_synced_utc TEXT NULL
        );
        CREATE TABLE encrypted.plex_history (
            history_id INTEGER PRIMARY KEY AUTOINCREMENT,
            connection_id TEXT NOT NULL,
            rating_key TEXT NOT NULL,
            media_type TEXT NOT NULL,
            title TEXT NOT NULL,
            release_year INTEGER NULL,
            plex_guid TEXT NULL,
            viewed_at_utc TEXT NOT NULL,
            view_offset_ms INTEGER NULL,
            duration_ms INTEGER NULL,
            normalized_title TEXT NOT NULL,
            watch_exclusion_key TEXT NOT NULL,
            UNIQUE(connection_id, rating_key, viewed_at_utc),
            FOREIGN KEY(connection_id) REFERENCES plex_server(connection_id) ON DELETE CASCADE
        );
        CREATE INDEX encrypted.ix_plex_history_viewed_at ON plex_history(viewed_at_utc);
        CREATE TABLE encrypted.feedback_event (
            event_id INTEGER PRIMARY KEY AUTOINCREMENT,
            candidate_id TEXT NOT NULL,
            feedback INTEGER NOT NULL,
            created_utc TEXT NOT NULL,
            is_undone INTEGER NOT NULL DEFAULT 0 CHECK (is_undone IN (0, 1))
        );
        CREATE INDEX encrypted.ix_feedback_candidate ON feedback_event(candidate_id, event_id DESC);
        CREATE TABLE encrypted.user_setting (
            setting_key TEXT PRIMARY KEY,
            setting_value TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );
        """;

    public const string CopyToEncryptedSchemaSql = """
        DELETE FROM encrypted.schema_version;
        INSERT INTO encrypted.schema_version(version, applied_utc)
            SELECT version, applied_utc FROM main.schema_version;
        INSERT INTO encrypted.plex_server(
            connection_id, server_url, allow_insecure_http, machine_identifier,
            name, version, last_synced_utc)
            SELECT connection_id, server_url, allow_insecure_http, machine_identifier,
                   name, version, last_synced_utc
            FROM main.plex_server;
        INSERT INTO encrypted.plex_history(
            history_id, connection_id, rating_key, media_type, title, release_year,
            plex_guid, viewed_at_utc, view_offset_ms, duration_ms, normalized_title,
            watch_exclusion_key)
            SELECT history_id, connection_id, rating_key, media_type, title, release_year,
                   plex_guid, viewed_at_utc, view_offset_ms, duration_ms, normalized_title,
                   watch_exclusion_key
            FROM main.plex_history;
        INSERT INTO encrypted.feedback_event(
            event_id, candidate_id, feedback, created_utc, is_undone)
            SELECT event_id, candidate_id, feedback, created_utc, is_undone
            FROM main.feedback_event;
        INSERT INTO encrypted.user_setting(setting_key, setting_value, updated_utc)
            SELECT setting_key, setting_value, updated_utc
            FROM main.user_setting;
        DELETE FROM encrypted.sqlite_sequence
            WHERE name IN ('plex_history', 'feedback_event');
        INSERT INTO encrypted.sqlite_sequence(name, seq)
            SELECT name, seq FROM main.sqlite_sequence
            WHERE name IN ('plex_history', 'feedback_event');
        """;

    public static async Task InitializeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand createCommand = connection.CreateCommand())
        {
            createCommand.Transaction = transaction;
            createCommand.CommandText = CreateSql;
            await createCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (SqliteCommand versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText =
                "INSERT OR IGNORE INTO schema_version(version, applied_utc) VALUES ($version, $utc);";
            versionCommand.Parameters.AddWithValue("$version", CurrentVersion);
            versionCommand.Parameters.AddWithValue(
                "$utc",
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<UserDataRowCounts> ValidateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        string? runtimeVersion = Convert.ToString(
            await ScalarAsync(connection, "SELECT sqlite3mc_version();", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(runtimeVersion) ||
            !runtimeVersion.StartsWith("SQLite3 Multiple Ciphers ", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The encrypted SQLite runtime is unavailable.");
        }

        string? integrity = Convert.ToString(
            await ScalarAsync(connection, "PRAGMA quick_check;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (!string.Equals(integrity, "ok", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The user-data database failed its SQLite integrity check.");
        }

        SchemaObject[] expectedSchema = await CreateExpectedSchemaAsync(cancellationToken).ConfigureAwait(false);
        SchemaObject[] actualSchema = await ReadSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!actualSchema.SequenceEqual(expectedSchema))
        {
            throw new InvalidDataException("The user-data database has an unsupported schema layout.");
        }

        long versionCount = Convert.ToInt64(
            await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM schema_version WHERE version = 1;",
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        long totalVersionCount = Convert.ToInt64(
            await ScalarAsync(connection, "SELECT COUNT(*) FROM schema_version;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (versionCount != 1 || totalVersionCount != 1)
        {
            throw new InvalidDataException("The user-data database schema version is unsupported.");
        }

        await using (SqliteCommand foreignKeyCommand = connection.CreateCommand())
        {
            foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
            await using SqliteDataReader reader =
                await foreignKeyCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("The user-data database failed its foreign-key check.");
            }
        }

        return await ReadCountsAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public static async Task CreateEncryptedAttachedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = CreateEncryptedSchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<UserDataRowCounts> ReadCountsAsync(
        SqliteConnection connection,
        string? schemaName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = schemaName switch
        {
            null => """
                SELECT
                    (SELECT COUNT(*) FROM schema_version),
                    (SELECT COUNT(*) FROM plex_server),
                    (SELECT COUNT(*) FROM plex_history),
                    (SELECT COUNT(*) FROM feedback_event),
                    (SELECT COUNT(*) FROM user_setting);
                """,
            "encrypted" => """
                SELECT
                    (SELECT COUNT(*) FROM encrypted.schema_version),
                    (SELECT COUNT(*) FROM encrypted.plex_server),
                    (SELECT COUNT(*) FROM encrypted.plex_history),
                    (SELECT COUNT(*) FROM encrypted.feedback_event),
                    (SELECT COUNT(*) FROM encrypted.user_setting);
                """,
            _ => throw new ArgumentException("The user-data schema name is unsupported.", nameof(schemaName)),
        };
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The user-data row counts could not be read.");
        }

        return new UserDataRowCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SchemaObject[]> CreateExpectedSchemaAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = CreateSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReadSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SchemaObject[]> ReadSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var objects = new List<SchemaObject>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT type, name, tbl_name, sql
            FROM sqlite_schema
            WHERE type IN ('table', 'index', 'view', 'trigger')
              AND substr(name, 1, 7) <> 'sqlite_'
            ORDER BY type, name;
            """;
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            objects.Add(new SchemaObject(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                NormalizeSql(reader.GetString(3))));
        }

        return objects.ToArray();
    }

    private static string NormalizeSql(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record SchemaObject(string Type, string Name, string TableName, string Sql);
}

internal sealed record UserDataRowCounts(
    long SchemaVersions,
    long PlexServers,
    long PlexHistory,
    long FeedbackEvents,
    long UserSettings);
