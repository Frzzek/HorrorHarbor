using System.Globalization;
using System.Security.Cryptography;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.Validation;
using HorrorHarbor.Core.ValueObjects;
using Microsoft.Data.Sqlite;

namespace HorrorHarbor.Data.UserData;

public sealed class SqliteUserDataStore : IUserDataStore, IDisposable
{
    public const int EncryptionKeyLength = 32;

    private readonly string _databasePath;
    private readonly IUserDataEncryptionKeyProvider _keyProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _encryptionKey;

    public SqliteUserDataStore(IUserDataEncryptionKeyProvider keyProvider)
        : this(UserDataPathResolver.Resolve(), keyProvider)
    {
    }

    public SqliteUserDataStore(string path, IUserDataEncryptionKeyProvider keyProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(keyProvider);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("User data path must include a directory.", nameof(path));
        }

        Directory.CreateDirectory(directory);
        _databasePath = fullPath;
        _keyProvider = keyProvider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _encryptionKey) is not null)
            {
                return;
            }

            await using UserDataInitializationLock initializationLock =
                await UserDataInitializationLock.AcquireAsync(
                    _databasePath,
                    cancellationToken).ConfigureAwait(false);

            byte[]? key = await _keyProvider.LoadKeyAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                bool recoveryExists =
                    File.Exists(_databasePath + ".encryption-replacement-backup") ||
                    File.Exists(_databasePath + ".encryption-recovery-state.json");
                bool databaseExists = File.Exists(_databasePath) && new FileInfo(_databasePath).Length > 0;
                if (key is null)
                {
                    if (recoveryExists ||
                        (databaseExists &&
                        !await IsPlaintextDatabaseAsync(cancellationToken).ConfigureAwait(false))
                    )
                    {
                        throw new CryptographicException(
                            "The encrypted user-data database exists, but its current-user key is unavailable.");
                    }

                    key = await CreateOrLoadKeyAsync(cancellationToken).ConfigureAwait(false);
                }

                ValidateKey(key);
                await UserDataDatabaseMigrator.ReconcileAsync(
                    _databasePath,
                    key,
                    cancellationToken).ConfigureAwait(false);
                databaseExists = File.Exists(_databasePath) && new FileInfo(_databasePath).Length > 0;
                if (databaseExists)
                {
                    bool encrypted = await IsEncryptedDatabaseAsync(key, cancellationToken).ConfigureAwait(false);
                    if (!encrypted)
                    {
                        if (!await IsPlaintextDatabaseAsync(cancellationToken).ConfigureAwait(false))
                        {
                            throw new CryptographicException(
                                "The user-data database could not be decrypted with the protected key.");
                        }

                        await UserDataDatabaseMigrator.MigratePlaintextAsync(
                            _databasePath,
                            key,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                await using SqliteConnection connection =
                    await EncryptedSqliteConnectionFactory.OpenEncryptedAsync(
                        _databasePath,
                        SqliteOpenMode.ReadWriteCreate,
                        key,
                        useWriteAheadLog: true,
                        cancellationToken).ConfigureAwait(false);
                await UserDataSchema.InitializeAsync(connection, cancellationToken).ConfigureAwait(false);
                await UserDataSchema.ValidateAsync(connection, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _encryptionKey, key);
                key = null;
            }
            finally
            {
                if (key is not null)
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
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
        byte[]? key = Interlocked.Exchange(ref _encryptionKey, null);
        if (key is not null)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        _gate.Dispose();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        byte[] key = Volatile.Read(ref _encryptionKey) ??
            throw new InvalidOperationException("The encrypted user-data store has not been initialized.");
        return await EncryptedSqliteConnectionFactory.OpenEncryptedAsync(
            _databasePath,
            SqliteOpenMode.ReadWrite,
            key,
            useWriteAheadLog: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> CreateOrLoadKeyAsync(CancellationToken cancellationToken)
    {
        byte[] generated = RandomNumberGenerator.GetBytes(EncryptionKeyLength);
        try
        {
            if (await _keyProvider.TrySaveKeyAsync(generated, cancellationToken).ConfigureAwait(false))
            {
                byte[] result = generated;
                generated = Array.Empty<byte>();
                return result;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(generated);
        }

        byte[]? existing = await _keyProvider.LoadKeyAsync(cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            throw new CryptographicException("The user-data encryption key could not be created atomically.");
        }

        ValidateKey(existing);
        return existing;
    }

    private async Task<bool> IsEncryptedDatabaseAsync(
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection =
                await EncryptedSqliteConnectionFactory.OpenEncryptedAsync(
                    _databasePath,
                    SqliteOpenMode.ReadWrite,
                    key,
                    useWriteAheadLog: false,
                    cancellationToken).ConfigureAwait(false);
            await UserDataSchema.ValidateAsync(connection, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 26)
        {
            return false;
        }
    }

    private async Task<bool> IsPlaintextDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection =
                await EncryptedSqliteConnectionFactory.OpenPlaintextAsync(
                    _databasePath,
                    cancellationToken).ConfigureAwait(false);
            await UserDataSchema.ValidateAsync(connection, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 26)
        {
            return false;
        }
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != EncryptionKeyLength)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("The user-data encryption key has an invalid length.");
        }
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
