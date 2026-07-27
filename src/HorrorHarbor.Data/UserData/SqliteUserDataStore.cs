using System.Globalization;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.Validation;
using HorrorHarbor.Core.ValueObjects;
using Microsoft.Data.Sqlite;

namespace HorrorHarbor.Data.UserData;

public sealed class SqliteUserDataStore : IUserDataStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteUserDataStore()
        : this(UserDataPathResolver.Resolve())
    {
    }

    public SqliteUserDataStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("User data path must include a directory.", nameof(path));
        }

        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
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
                """, cancellationToken).ConfigureAwait(false);

            await using SqliteCommand versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "INSERT OR IGNORE INTO schema_version(version, applied_utc) VALUES ($version, $utc);";
            versionCommand.Parameters.AddWithValue("$version", CurrentSchemaVersion);
            versionCommand.Parameters.AddWithValue("$utc", FormatUtc(DateTime.UtcNow));
            await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SavePlexServerAsync(
        string connectionId,
        PlexConnectionSettings settings,
        PlexServerIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ValidateConnectionId(connectionId);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(identity);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO plex_server(connection_id, server_url, allow_insecure_http, machine_identifier, name, version)
            VALUES ($id, $url, $insecure, $machine, $name, $version)
            ON CONFLICT(connection_id) DO UPDATE SET
                server_url = excluded.server_url,
                allow_insecure_http = excluded.allow_insecure_http,
                machine_identifier = excluded.machine_identifier,
                name = excluded.name,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("$id", connectionId);
        command.Parameters.AddWithValue("$url", settings.ServerUrl);
        command.Parameters.AddWithValue("$insecure", settings.AllowInsecureHttp ? 1 : 0);
        command.Parameters.AddWithValue("$machine", identity.MachineIdentifier);
        command.Parameters.AddWithValue("$name", identity.Name);
        command.Parameters.AddWithValue("$version", identity.Version);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<(PlexConnectionSettings Settings, PlexServerIdentity Identity, DateTime? LastSyncedUtc)?> LoadPlexServerAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateConnectionId(connectionId);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT server_url, allow_insecure_http, machine_identifier, name, version, last_synced_utc
            FROM plex_server WHERE connection_id = $id;
            """;
        command.Parameters.AddWithValue("$id", connectionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        DateTime? lastSynced = reader.IsDBNull(5) ? null : ParseUtc(reader.GetString(5));
        return (
            new PlexConnectionSettings(reader.GetString(0), reader.GetInt32(1) == 1),
            new PlexServerIdentity(reader.GetString(2), reader.GetString(3), reader.GetString(4)),
            lastSynced);
    }

    public async Task<PlexSyncResult> ImportPlexHistoryAsync(
        string connectionId,
        IReadOnlyList<PlexHistoryItem> items,
        CancellationToken cancellationToken = default)
    {
        ValidateConnectionId(connectionId);
        ArgumentNullException.ThrowIfNull(items);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var imported = 0;
            DateTime? latest = null;
            foreach (PlexHistoryItem item in items)
            {
                ValidateHistoryItem(item);
                string normalizedTitle = TitleNormalizer.NormalizeForKey(item.Title);
                string exclusionKey = $"{NormalizeMediaType(item.MediaType)}|{normalizedTitle}|{item.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";
                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT OR IGNORE INTO plex_history(
                        connection_id, rating_key, media_type, title, release_year, plex_guid,
                        viewed_at_utc, view_offset_ms, duration_ms, normalized_title, watch_exclusion_key)
                    VALUES ($connection, $rating, $type, $title, $year, $guid, $viewed, $offset, $duration, $normalized, $key);
                    """;
                command.Parameters.AddWithValue("$connection", connectionId);
                command.Parameters.AddWithValue("$rating", item.RatingKey);
                command.Parameters.AddWithValue("$type", NormalizeMediaType(item.MediaType));
                command.Parameters.AddWithValue("$title", item.Title);
                command.Parameters.AddWithValue("$year", (object?)item.ReleaseYear ?? DBNull.Value);
                command.Parameters.AddWithValue("$guid", (object?)item.ProviderIdentifier ?? DBNull.Value);
                command.Parameters.AddWithValue("$viewed", FormatUtc(item.ViewedAtUtc));
                command.Parameters.AddWithValue("$offset", (object?)item.ViewOffsetMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue("$duration", (object?)item.DurationMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue("$normalized", normalizedTitle);
                command.Parameters.AddWithValue("$key", exclusionKey);
                imported += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                latest = latest is null || item.ViewedAtUtc > latest ? item.ViewedAtUtc : latest;
            }

            if (latest is not null)
            {
                await using SqliteCommand syncCommand = connection.CreateCommand();
                syncCommand.Transaction = transaction;
                syncCommand.CommandText = """
                    UPDATE plex_server
                    SET last_synced_utc = CASE
                        WHEN last_synced_utc IS NULL OR last_synced_utc < $latest THEN $latest
                        ELSE last_synced_utc END
                    WHERE connection_id = $id;
                    """;
                syncCommand.Parameters.AddWithValue("$latest", FormatUtc(latest.Value));
                syncCommand.Parameters.AddWithValue("$id", connectionId);
                await syncCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new PlexSyncResult(items.Count, imported, items.Count - imported, latest);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HashSet<string>> GetPlexWatchedExclusionKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT media_type, normalized_title, watch_exclusion_key FROM plex_history;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(2));
            keys.Add($"{reader.GetString(0)}|{reader.GetString(1)}|*");
        }

        return keys;
    }

    public async Task<IReadOnlyList<PlexHistoryItem>> GetPlexHistoryAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<PlexHistoryItem>();
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT rating_key, media_type, title, release_year, plex_guid, viewed_at_utc, view_offset_ms, duration_ms
            FROM plex_history ORDER BY viewed_at_utc DESC;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new PlexHistoryItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                ParseUtc(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7)));
        }

        return results.AsReadOnly();
    }

    public async Task RecordFeedbackAsync(
        string candidateId,
        RecommendationFeedback feedback,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default)
    {
        CandidateIdValidator.Validate(candidateId, nameof(candidateId));
        if (!Enum.IsDefined(feedback))
        {
            throw new ArgumentOutOfRangeException(nameof(feedback));
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO feedback_event(candidate_id, feedback, created_utc) VALUES ($id, $feedback, $utc);";
        command.Parameters.AddWithValue("$id", candidateId);
        command.Parameters.AddWithValue("$feedback", (int)feedback);
        command.Parameters.AddWithValue("$utc", FormatUtc(timestampUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecommendationFeedback?> UndoLatestFeedbackAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        CandidateIdValidator.Validate(candidateId, nameof(candidateId));
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE feedback_event SET is_undone = 1
            WHERE event_id = (
                SELECT event_id FROM feedback_event
                WHERE candidate_id = $id AND is_undone = 0
                ORDER BY event_id DESC LIMIT 1)
            RETURNING feedback;
            """;
        command.Parameters.AddWithValue("$id", candidateId);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null ? null : (RecommendationFeedback)Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public async Task<bool> RestoreLatestFeedbackAsync(
        string candidateId,
        RecommendationFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        CandidateIdValidator.Validate(candidateId, nameof(candidateId));
        if (!Enum.IsDefined(feedback))
        {
            throw new ArgumentOutOfRangeException(nameof(feedback));
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE feedback_event SET is_undone = 0
            WHERE event_id = (
                SELECT event_id FROM feedback_event
                WHERE candidate_id = $id AND feedback = $feedback AND is_undone = 1
                ORDER BY event_id DESC LIMIT 1);
            """;
        command.Parameters.AddWithValue("$id", candidateId);
        command.Parameters.AddWithValue("$feedback", (int)feedback);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task SaveSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateSetting(key, value);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_setting(setting_key, setting_value, updated_utc)
            VALUES ($key, $value, $utc)
            ON CONFLICT(setting_key) DO UPDATE SET
                setting_value = excluded.setting_value,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$utc", FormatUtc(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> LoadSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateSettingKey(key);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT setting_value FROM user_setting WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", key);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FormatUtc(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTime ParseUtc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static string NormalizeMediaType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "movie" => "movie",
        "episode" or "show" => "show",
        _ => "other",
    };

    private static void ValidateConnectionId(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (connectionId.Length > 128 || connectionId.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Connection ID contains unsupported characters.", nameof(connectionId));
        }
    }

    private static void ValidateSetting(string key, string value)
    {
        ValidateSettingKey(key);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 2048)
        {
            throw new ArgumentException("Setting value exceeds the supported length.", nameof(value));
        }
    }

    private static void ValidateSettingKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 128 || key.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Setting key contains unsupported characters.", nameof(key));
        }
    }

    private static void ValidateHistoryItem(PlexHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.RatingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Title);
        if (item.RatingKey.Length > 256 || item.Title.Length > 512 || item.ViewedAtUtc == default)
        {
            throw new ArgumentException("Plex history item exceeds supported limits.", nameof(item));
        }
    }
}
