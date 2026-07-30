using System.Security.Cryptography;
using System.Text;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Data.UserData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class UserDataEncryptionIntegrationTests
{
    private const string Canary = "HORRORHARBOR_SYNTHETIC_PRIVATE_CANARY";
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");
    private static readonly byte[] CanaryBytes = Encoding.UTF8.GetBytes(Canary);

    [TestMethod]
    public async Task NewDatabaseIsOpaqueAndRoundTripsOnlyWithTheProtectedKey()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        try
        {
            using var keyProvider = new TestUserDataEncryptionKeyProvider();
            using (var store = new SqliteUserDataStore(databasePath, keyProvider))
            {
                await store.InitializeAsync();
                await store.SaveSettingAsync("synthetic.canary", Canary);
            }

            AssertOpaqueDatabaseArtifacts(databasePath);
            await AssertUnkeyedOpenIsRejectedAsync(databasePath);

            using var reopened = new SqliteUserDataStore(databasePath, keyProvider);
            await reopened.InitializeAsync();
            Assert.AreEqual(Canary, await reopened.LoadSettingAsync("synthetic.canary"));
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task WrongKeyIsRejectedWithoutChangingTheEncryptedDatabase()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] correctKey = RepeatedKey(0x31);
        byte[] wrongKey = RepeatedKey(0x7A);
        try
        {
            using (var correctProvider = new TestUserDataEncryptionKeyProvider(correctKey))
            using (var store = new SqliteUserDataStore(databasePath, correctProvider))
            {
                await store.InitializeAsync();
                await store.SaveSettingAsync("synthetic.canary", Canary);
            }

            string originalHash = GetSha256(databasePath);
            using var wrongProvider = new TestUserDataEncryptionKeyProvider(wrongKey);
            using var wrongStore = new SqliteUserDataStore(databasePath, wrongProvider);
            await Assert.ThrowsAsync<CryptographicException>(() => wrongStore.InitializeAsync());

            Assert.AreEqual(originalHash, GetSha256(databasePath));
            AssertOpaqueDatabaseArtifacts(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(correctKey);
            CryptographicOperations.ZeroMemory(wrongKey);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task MissingKeyIsRejectedWithoutSilentlyGeneratingAReplacement()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x42);
        try
        {
            using (var originalProvider = new TestUserDataEncryptionKeyProvider(key))
            using (var store = new SqliteUserDataStore(databasePath, originalProvider))
            {
                await store.InitializeAsync();
                await store.SaveSettingAsync("synthetic.canary", Canary);
            }

            string originalHash = GetSha256(databasePath);
            using var missingProvider = new TestUserDataEncryptionKeyProvider();
            using var missingKeyStore = new SqliteUserDataStore(databasePath, missingProvider);
            await Assert.ThrowsAsync<CryptographicException>(() => missingKeyStore.InitializeAsync());

            Assert.IsNull(await missingProvider.LoadKeyAsync());
            Assert.AreEqual(originalHash, GetSha256(databasePath));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task PlaintextVersionOneDatabaseMigratesAtomicallyAndPreservesRows()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x53);
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(databasePath, includeUnknownTable: false);
            byte[] plaintextBytes = await File.ReadAllBytesAsync(databasePath);
            Assert.IsTrue(Contains(plaintextBytes, SqliteHeader));
            Assert.IsTrue(Contains(plaintextBytes, CanaryBytes));

            using var keyProvider = new TestUserDataEncryptionKeyProvider(key);
            using (var store = new SqliteUserDataStore(databasePath, keyProvider))
            {
                await store.InitializeAsync();
                Assert.AreEqual(Canary, await store.LoadSettingAsync("synthetic.canary"));
                IReadOnlyList<PlexHistoryItem> history = await store.GetPlexHistoryAsync();
                Assert.HasCount(1, history);
                Assert.AreEqual(Canary, history[0].Title);
            }

            AssertOpaqueDatabaseArtifacts(databasePath);
            Assert.IsFalse(File.Exists(databasePath + ".encryption-migration"));
            await AssertUnkeyedOpenIsRejectedAsync(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task UnknownPlaintextSchemaIsRejectedAndOriginalFileIsPreserved()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x64);
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(databasePath, includeUnknownTable: true);
            string originalHash = GetSha256(databasePath);

            using var keyProvider = new TestUserDataEncryptionKeyProvider(key);
            using var store = new SqliteUserDataStore(databasePath, keyProvider);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());

            Assert.AreEqual(originalHash, GetSha256(databasePath));
            Assert.IsFalse(File.Exists(databasePath + ".encryption-migration"));
            byte[] originalBytes = await File.ReadAllBytesAsync(databasePath);
            Assert.IsTrue(Contains(originalBytes, SqliteHeader));
            Assert.IsTrue(Contains(originalBytes, CanaryBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task PlaintextSchemaDriftIsRejectedWithoutDiscardingUnknownColumnData()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x6B);
        const string unsupportedValue = "UNSUPPORTED_PRIVATE_SENTINEL";
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(
                databasePath,
                includeUnknownTable: false,
                includeUnknownColumn: true);
            string originalHash = GetSha256(databasePath);

            using var keyProvider = new TestUserDataEncryptionKeyProvider(key);
            using var store = new SqliteUserDataStore(databasePath, keyProvider);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());

            Assert.AreEqual(originalHash, GetSha256(databasePath));
            Assert.AreEqual(
                unsupportedValue,
                await ReadPlaintextColumnAsync(
                    databasePath,
                    "unsupported_private_value",
                    "synthetic.canary"));
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task SqliteLikePrefixSchemaBypassIsRejectedWithoutDiscardingUnknownTableData()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x6C);
        const string unsupportedValue = "SQLITEX_PRIVATE_SENTINEL";
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(
                databasePath,
                includeUnknownTable: false,
                includeUnknownColumn: false,
                includeSqliteLikePrefixTable: true);
            string originalHash = GetSha256(databasePath);

            using var keyProvider = new TestUserDataEncryptionKeyProvider(key);
            using var store = new SqliteUserDataStore(databasePath, keyProvider);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());

            Assert.AreEqual(originalHash, GetSha256(databasePath));
            Assert.AreEqual(
                unsupportedValue,
                await ReadSqliteLikePrefixTableAsync(databasePath));
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ConcurrentInitializationIsIdempotent()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        try
        {
            using var keyProvider = new TestUserDataEncryptionKeyProvider();
            using var store = new SqliteUserDataStore(databasePath, keyProvider);

            await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => store.InitializeAsync()));
            await store.SaveSettingAsync("synthetic.canary", Canary);

            Assert.AreEqual(Canary, await store.LoadSettingAsync("synthetic.canary"));
            AssertOpaqueDatabaseArtifacts(databasePath);
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task CanceledInitializationCreatesNeitherDatabaseNorKey()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        try
        {
            using var keyProvider = new TestUserDataEncryptionKeyProvider();
            using var store = new SqliteUserDataStore(databasePath, keyProvider);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => store.InitializeAsync(cancellation.Token));

            Assert.IsFalse(File.Exists(databasePath));
            Assert.IsNull(await keyProvider.LoadKeyAsync());
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task StaleMigrationArtifactsAreRemovedBeforeMigration()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x75);
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(databasePath, includeUnknownTable: false);
            string migrationPath = databasePath + ".encryption-migration";
            foreach (string path in new[]
            {
                migrationPath,
                migrationPath + "-wal",
                migrationPath + "-shm",
                migrationPath + "-journal",
            })
            {
                await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(128));
            }

            using var keyProvider = new TestUserDataEncryptionKeyProvider(key);
            using var store = new SqliteUserDataStore(databasePath, keyProvider);
            await store.InitializeAsync();

            Assert.AreEqual(Canary, await store.LoadSettingAsync("synthetic.canary"));
            AssertOpaqueDatabaseArtifacts(databasePath);
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ReplacementFailurePreservesReadablePlaintextOriginal()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x86);
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(databasePath, includeUnknownTable: false);
            using var keyProvider = new TestUserDataEncryptionKeyProvider(key);
            using var store = new SqliteUserDataStore(databasePath, keyProvider);
            using (FileStream replacementBlocker = new(
                databasePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            {
                await Assert.ThrowsAsync<IOException>(() => store.InitializeAsync());
            }

            Assert.IsTrue(Contains(await File.ReadAllBytesAsync(databasePath), SqliteHeader));
            Assert.AreEqual(Canary, await ReadPlaintextSettingAsync(databasePath, "synthetic.canary"));
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task PathReplacementRaceRestoresConcurrentlyCommittedPlaintextDatabase()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        string displacedOriginalPath = Path.Combine(directory, "displaced-original.db");
        byte[] key = RepeatedKey(0x8A);
        const string committedKey = "synthetic.concurrent-path-replacement";
        const string committedValue = "CONCURRENTLY_COMMITTED_REPLACEMENT";
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(
                databasePath,
                includeUnknownTable: false);

            await Assert.ThrowsAsync<IOException>(
                () => UserDataDatabaseMigrator.MigratePlaintextAsync(
                    databasePath,
                    key,
                    CancellationToken.None,
                    () =>
                    {
                        File.Move(databasePath, displacedOriginalPath);
                        File.Copy(displacedOriginalPath, databasePath);
                        using SqliteConnection replacement =
                            CreatePlaintextConnection(databasePath);
                        replacement.Open();
                        using SqliteCommand command = replacement.CreateCommand();
                        command.CommandText =
                            "INSERT INTO user_setting(setting_key, setting_value, updated_utc) " +
                            "VALUES ($key, $value, '2026-07-29T00:00:00.0000000Z');";
                        command.Parameters.AddWithValue("$key", committedKey);
                        command.Parameters.AddWithValue("$value", committedValue);
                        command.ExecuteNonQuery();
                    }));

            Assert.AreEqual(
                committedValue,
                await ReadPlaintextSettingAsync(databasePath, committedKey));
            Assert.AreEqual(Canary, await ReadPlaintextSettingAsync(databasePath, "synthetic.canary"));
            Assert.IsFalse(File.Exists(databasePath + ".encryption-replacement-backup"));
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task PostInstallPathReplacementIsPreservedWithoutUnsafeRollback()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        string displacedEncryptedPath = Path.Combine(directory, "displaced-encrypted.db");
        byte[] key = RepeatedKey(0x8B);
        const string committedKey = "synthetic.post-install-path-replacement";
        const string committedValue = "POST_INSTALL_CONCURRENT_COMMIT";
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(
                databasePath,
                includeUnknownTable: false);

            await Assert.ThrowsAsync<IOException>(
                () => UserDataDatabaseMigrator.MigratePlaintextAsync(
                    databasePath,
                    key,
                    CancellationToken.None,
                    beforeAtomicReplacement: null,
                    afterEncryptedPrimaryInstalled: () =>
                    {
                        File.Move(databasePath, displacedEncryptedPath);
                        CreatePlaintextVersionOneDatabaseAsync(
                            databasePath,
                            includeUnknownTable: false).GetAwaiter().GetResult();
                        using SqliteConnection replacement =
                            CreatePlaintextConnection(databasePath);
                        replacement.Open();
                        using SqliteCommand command = replacement.CreateCommand();
                        command.CommandText =
                            "INSERT INTO user_setting(setting_key, setting_value, updated_utc) " +
                            "VALUES ($key, $value, '2026-07-29T00:00:00.0000000Z');";
                        command.Parameters.AddWithValue("$key", committedKey);
                        command.Parameters.AddWithValue("$value", committedValue);
                        command.ExecuteNonQuery();
                    }));

            Assert.AreEqual(
                committedValue,
                await ReadPlaintextSettingAsync(databasePath, committedKey));
            Assert.IsTrue(File.Exists(databasePath + ".encryption-replacement-backup"));
            Assert.IsTrue(File.Exists(databasePath + ".encryption-recovery-state.json"));
            Assert.IsTrue(File.Exists(displacedEncryptedPath));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task InterruptedCompletedMigrationIsReconciledWithoutLeavingPlaintextBackup()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x8C);
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(
                databasePath,
                includeUnknownTable: false);

            await Assert.ThrowsAsync<IOException>(
                () => UserDataDatabaseMigrator.MigratePlaintextAsync(
                    databasePath,
                    key,
                    CancellationToken.None,
                    beforeAtomicReplacement: null,
                    afterEncryptedPrimaryInstalled: () =>
                        throw new IOException("Synthetic process interruption.")));

            Assert.IsTrue(File.Exists(databasePath + ".encryption-replacement-backup"));
            Assert.IsTrue(File.Exists(databasePath + ".encryption-recovery-state.json"));

            using var keyProvider = new TestUserDataEncryptionKeyProvider(key);
            using var store = new SqliteUserDataStore(databasePath, keyProvider);
            await store.InitializeAsync();

            Assert.AreEqual(Canary, await store.LoadSettingAsync("synthetic.canary"));
            AssertNoMigrationArtifacts(databasePath);
            AssertOpaqueDatabaseArtifacts(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task BusyPlaintextWalFailsWithoutLosingCommittedData()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        byte[] key = RepeatedKey(0x97);
        try
        {
            await CreatePlaintextVersionOneDatabaseAsync(databasePath, includeUnknownTable: false);
            await using (SqliteConnection reader = CreatePlaintextConnection(databasePath))
            {
                await reader.OpenAsync();
                await ExecutePlaintextAsync(reader, "PRAGMA journal_mode=WAL;");
                await using SqliteTransaction snapshot =
                    reader.BeginTransaction(deferred: true);
                await using (SqliteCommand readCommand = reader.CreateCommand())
                {
                    readCommand.Transaction = snapshot;
                    readCommand.CommandText =
                        "SELECT setting_value FROM user_setting WHERE setting_key = 'synthetic.canary';";
                    Assert.AreEqual(Canary, await readCommand.ExecuteScalarAsync());
                }

                await using (SqliteConnection writer = CreatePlaintextConnection(databasePath))
                {
                    await writer.OpenAsync();
                    await ExecutePlaintextAsync(
                        writer,
                        "INSERT INTO user_setting(setting_key, setting_value, updated_utc) " +
                        "VALUES ('synthetic.after-snapshot', 'committed', '2026-07-28T00:00:01.0000000Z');");
                }

                using var keyProvider = new TestUserDataEncryptionKeyProvider(key);
                using var store = new SqliteUserDataStore(databasePath, keyProvider);
                await Assert.ThrowsAsync<IOException>(() => store.InitializeAsync());
            }

            Assert.AreEqual(Canary, await ReadPlaintextSettingAsync(databasePath, "synthetic.canary"));
            Assert.AreEqual(
                "committed",
                await ReadPlaintextSettingAsync(databasePath, "synthetic.after-snapshot"));
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    internal static async Task CreatePlaintextVersionOneDatabaseAsync(
        string databasePath,
        bool includeUnknownTable,
        bool includeUnknownColumn = false,
        bool includeSqliteLikePrefixTable = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        SQLitePCL.Batteries_V2.Init();
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=DELETE;
            PRAGMA foreign_keys=ON;
            CREATE TABLE schema_version (
                version INTEGER PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            CREATE TABLE plex_server (
                connection_id TEXT PRIMARY KEY,
                server_url TEXT NOT NULL,
                allow_insecure_http INTEGER NOT NULL CHECK (allow_insecure_http IN (0, 1)),
                machine_identifier TEXT NOT NULL,
                name TEXT NOT NULL,
                version TEXT NOT NULL,
                last_synced_utc TEXT NULL
            );
            CREATE TABLE plex_history (
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
            CREATE INDEX ix_plex_history_viewed_at ON plex_history(viewed_at_utc);
            CREATE TABLE feedback_event (
                event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                candidate_id TEXT NOT NULL,
                feedback INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                is_undone INTEGER NOT NULL DEFAULT 0 CHECK (is_undone IN (0, 1))
            );
            CREATE INDEX ix_feedback_candidate ON feedback_event(candidate_id, event_id DESC);
            CREATE TABLE user_setting (
                setting_key TEXT PRIMARY KEY,
                setting_value TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            INSERT INTO schema_version(version, applied_utc)
                VALUES (1, '2026-07-28T00:00:00.0000000Z');
            INSERT INTO plex_server(
                connection_id, server_url, allow_insecure_http, machine_identifier,
                name, version, last_synced_utc)
                VALUES ('primary', 'https://plex.example:32400', 0, 'synthetic-machine',
                        'Synthetic Plex', '1.0', '2026-07-28T00:00:00.0000000Z');
            INSERT INTO plex_history(
                connection_id, rating_key, media_type, title, release_year, plex_guid,
                viewed_at_utc, view_offset_ms, duration_ms, normalized_title,
                watch_exclusion_key)
                VALUES ('primary', 'synthetic-rating', 'movie',
                        'HORRORHARBOR_SYNTHETIC_PRIVATE_CANARY', 2026,
                        'plex://movie/synthetic', '2026-07-28T00:00:00.0000000Z',
                        1000, 5000, 'horrorharbor synthetic private canary',
                        'movie|horrorharbor synthetic private canary|2026');
            INSERT INTO feedback_event(candidate_id, feedback, created_utc, is_undone)
                VALUES ('synthetic-candidate', 1, '2026-07-28T00:00:00.0000000Z', 0);
            INSERT INTO user_setting(setting_key, setting_value, updated_utc)
                VALUES ('synthetic.canary', 'HORRORHARBOR_SYNTHETIC_PRIVATE_CANARY',
                        '2026-07-28T00:00:00.0000000Z');
            """ +
            (includeUnknownTable ? "CREATE TABLE unexpected_private_table(value TEXT);" : string.Empty) +
            (includeUnknownColumn
                ? "ALTER TABLE user_setting ADD COLUMN unsupported_private_value TEXT;" +
                  "UPDATE user_setting SET unsupported_private_value = 'UNSUPPORTED_PRIVATE_SENTINEL' " +
                  "WHERE setting_key = 'synthetic.canary';"
                : string.Empty) +
            (includeSqliteLikePrefixTable
                ? "CREATE TABLE sqlitex_private(secret TEXT);" +
                  "INSERT INTO sqlitex_private(secret) VALUES ('SQLITEX_PRIVATE_SENTINEL');"
                : string.Empty);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertUnkeyedOpenIsRejectedAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
            () => command.ExecuteScalarAsync());
        Assert.AreEqual(26, exception.SqliteErrorCode);
    }

    private static SqliteConnection CreatePlaintextConnection(string databasePath) => new(
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());

    private static async Task ExecutePlaintextAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadPlaintextSettingAsync(
        string databasePath,
        string settingKey)
    {
        await using SqliteConnection connection = CreatePlaintextConnection(databasePath);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT setting_value FROM user_setting WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", settingKey);
        return Convert.ToString(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ReadPlaintextColumnAsync(
        string databasePath,
        string columnName,
        string settingKey)
    {
        if (!string.Equals(columnName, "unsupported_private_value", StringComparison.Ordinal))
        {
            throw new ArgumentException("The synthetic column name is unsupported.", nameof(columnName));
        }

        await using SqliteConnection connection = CreatePlaintextConnection(databasePath);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT unsupported_private_value FROM user_setting WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", settingKey);
        return Convert.ToString(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ReadSqliteLikePrefixTableAsync(string databasePath)
    {
        await using SqliteConnection connection = CreatePlaintextConnection(databasePath);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT secret FROM sqlitex_private;";
        return Convert.ToString(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AssertNoMigrationArtifacts(string databasePath)
    {
        string migrationPath = databasePath + ".encryption-migration";
        string replacementBackupPath = databasePath + ".encryption-replacement-backup";
        string recoveryStatePath = databasePath + ".encryption-recovery-state.json";
        foreach (string path in new[]
        {
            migrationPath,
            migrationPath + "-wal",
            migrationPath + "-shm",
            migrationPath + "-journal",
            replacementBackupPath,
            replacementBackupPath + "-wal",
            replacementBackupPath + "-shm",
            replacementBackupPath + "-journal",
            recoveryStatePath,
        })
        {
            Assert.IsFalse(File.Exists(path), $"Migration artifact remained: {Path.GetFileName(path)}.");
        }
    }

    private static void AssertOpaqueDatabaseArtifacts(string databasePath)
    {
        string[] paths =
        {
            databasePath,
            databasePath + "-wal",
            databasePath + "-shm",
            databasePath + "-journal",
        };
        foreach (string path in paths.Where(File.Exists))
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.IsFalse(Contains(bytes, SqliteHeader), $"SQLite header leaked in {Path.GetFileName(path)}.");
            Assert.IsFalse(Contains(bytes, CanaryBytes), $"Synthetic canary leaked in {Path.GetFileName(path)}.");
        }
    }

    private static string CreateTestDirectory() => Path.Combine(
        Path.GetTempPath(),
        "HorrorHarbor.Tests",
        Guid.NewGuid().ToString("N"));

    private static void DeleteTestDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] RepeatedKey(byte value)
    {
        byte[] key = new byte[SqliteUserDataStore.EncryptionKeyLength];
        Array.Fill(key, value);
        return key;
    }

    private static string GetSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.Slice(index, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}
