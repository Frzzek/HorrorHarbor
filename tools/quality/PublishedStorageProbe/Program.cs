using System.IO;
using System.Security.Cryptography;
using System.Text;
using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Data.UserData;
using Microsoft.Data.Sqlite;

namespace HorrorHarbor.PublishedStorageProbe;

internal static class Program
{
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");
    private static readonly byte[] Canary = Encoding.UTF8.GetBytes(
        "HORRORHARBOR_" + "SYNTHETIC_" + "PRIVATE_" + "CANARY");

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Published storage probe argument validation failed.");
            return 2;
        }

        string? fixtureRoot = null;
        try
        {
            fixtureRoot = CreateOwnedFixtureRoot();
            VerifyCandidateFile("HorrorHarbor.dll", args[0]);
            VerifyCandidateFile("HorrorHarbor.Data.dll", args[1]);
            VerifyCandidateFile("sqlite3mc.dll", args[2]);
            VerifyAssemblyOrigin(typeof(DpapiUserDataEncryptionKeyProvider).Assembly.Location);
            VerifyAssemblyOrigin(typeof(SqliteUserDataStore).Assembly.Location);
            VerifyAssemblyOrigin(typeof(SqliteConnection).Assembly.Location);
            VerifyAssemblyOrigin(typeof(SQLitePCL.Batteries_V2).Assembly.Location);

            await VerifyNewEncryptedDatabaseAsync(Path.Combine(fixtureRoot, "new")).ConfigureAwait(false);
            await VerifyPlaintextMigrationAsync(Path.Combine(fixtureRoot, "migration")).ConfigureAwait(false);
            Console.WriteLine(
                "{\"policy\":\"HorrorHarbor exact published storage runtime v1\",\"status\":\"PASS\",\"checks\":12}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Published storage probe failed safely: {exception.GetType().Name}.");
            return 1;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (fixtureRoot is not null && Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static async Task VerifyNewEncryptedDatabaseAsync(string directory)
    {
        string databasePath = Path.Combine(directory, "user-data.db");
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        string canary = Encoding.UTF8.GetString(Canary);
        var keyProvider = new DpapiUserDataEncryptionKeyProvider(keyPath);
        using (var store = new SqliteUserDataStore(databasePath, keyProvider))
        {
            await store.InitializeAsync().ConfigureAwait(false);
            await store.SaveSettingAsync("synthetic.canary", canary).ConfigureAwait(false);
            Ensure(
                string.Equals(
                    await store.LoadSettingAsync("synthetic.canary").ConfigureAwait(false),
                    canary,
                    StringComparison.Ordinal),
                "Encrypted database round-trip failed.");
        }

        await VerifyProtectedKeyAsync(keyProvider, keyPath).ConfigureAwait(false);
        VerifyOpaqueArtifacts(databasePath);
        await VerifyKeylessReadIsRejectedAsync(databasePath).ConfigureAwait(false);
    }

    private static async Task VerifyPlaintextMigrationAsync(string directory)
    {
        string databasePath = Path.Combine(directory, "user-data.db");
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        string canary = Encoding.UTF8.GetString(Canary);
        await CreatePlaintextVersionOneDatabaseAsync(databasePath, canary).ConfigureAwait(false);
        Ensure(Contains(File.ReadAllBytes(databasePath), SqliteHeader), "Plaintext fixture was not plaintext.");

        var keyProvider = new DpapiUserDataEncryptionKeyProvider(keyPath);
        using (var store = new SqliteUserDataStore(databasePath, keyProvider))
        {
            await store.InitializeAsync().ConfigureAwait(false);
            Ensure(
                string.Equals(
                    await store.LoadSettingAsync("synthetic.canary").ConfigureAwait(false),
                    canary,
                    StringComparison.Ordinal),
                "Migrated database did not preserve its row.");
        }

        await VerifyProtectedKeyAsync(keyProvider, keyPath).ConfigureAwait(false);
        VerifyOpaqueArtifacts(databasePath);
        VerifyNoMigrationArtifacts(databasePath);
        await VerifyKeylessReadIsRejectedAsync(databasePath).ConfigureAwait(false);
    }

    private static async Task CreatePlaintextVersionOneDatabaseAsync(
        string databasePath,
        string canary)
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
        await connection.OpenAsync().ConfigureAwait(false);
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
            INSERT INTO user_setting(setting_key, setting_value, updated_utc)
                VALUES ('synthetic.canary', $canary, '2026-07-28T00:00:00.0000000Z');
            """;
        command.Parameters.AddWithValue("$canary", canary);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task VerifyProtectedKeyAsync(
        DpapiUserDataEncryptionKeyProvider keyProvider,
        string keyPath)
    {
        byte[] key = await keyProvider.LoadKeyAsync().ConfigureAwait(false) ??
            throw new InvalidOperationException("The DPAPI key was not created.");
        byte[] protectedBytes = await File.ReadAllBytesAsync(keyPath).ConfigureAwait(false);
        try
        {
            Ensure(key.Length == SqliteUserDataStore.EncryptionKeyLength, "The DPAPI key length is invalid.");
            Ensure(!Contains(protectedBytes, key), "The plaintext key was persisted.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static async Task VerifyKeylessReadIsRejectedAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync().ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        try
        {
            await command.ExecuteScalarAsync().ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 26)
        {
            return;
        }

        throw new InvalidOperationException("A keyless database read was not rejected.");
    }

    private static void VerifyOpaqueArtifacts(string databasePath)
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
            Ensure(bytes.Length > 0, "An inspected database artifact was empty.");
            Ensure(!Contains(bytes, SqliteHeader), "A plaintext SQLite header was present.");
            Ensure(!Contains(bytes, Canary), "The synthetic canary was present in database pages.");
        }
    }

    private static void VerifyNoMigrationArtifacts(string databasePath)
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
            Ensure(!File.Exists(path), "A stale migration artifact remained.");
        }
    }

    private static void VerifyCandidateFile(string fileName, string expectedSha256)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        Ensure(File.Exists(path), "A required candidate runtime file was missing.");
        string actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Ensure(
            string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase),
            "A candidate runtime hash did not match.");
    }

    private static void VerifyAssemblyOrigin(string location)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(location));
        Ensure(
            string.Equals(
                directory?.TrimEnd(Path.DirectorySeparatorChar),
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase),
            "A runtime assembly was loaded outside the candidate clone.");
    }

    private static string CreateOwnedFixtureRoot()
    {
        var runtimeDirectory = new DirectoryInfo(
            Path.GetFullPath(AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar));
        DirectoryInfo workRoot = runtimeDirectory.Parent ??
            throw new InvalidOperationException("The validation work root is unavailable.");
        DirectoryInfo validationRoot = workRoot.Parent ??
            throw new InvalidOperationException("The validation directory is unavailable.");
        DirectoryInfo artifactsRoot = validationRoot.Parent ??
            throw new InvalidOperationException("The artifacts directory is unavailable.");
        DirectoryInfo repositoryRoot = artifactsRoot.Parent ??
            throw new InvalidOperationException("The repository root is unavailable.");
        Ensure(
            string.Equals(runtimeDirectory.Name, "runtime", StringComparison.Ordinal) &&
            workRoot.Name.StartsWith("published-storage-probe-", StringComparison.Ordinal) &&
            string.Equals(validationRoot.Name, "validation", StringComparison.Ordinal) &&
            string.Equals(artifactsRoot.Name, "artifacts", StringComparison.Ordinal) &&
            File.Exists(Path.Combine(repositoryRoot.FullName, "HorrorHarbor.sln")),
            "The probe is outside the verified validation worktree.");
        foreach (DirectoryInfo directory in new[]
        {
            repositoryRoot,
            artifactsRoot,
            validationRoot,
            workRoot,
            runtimeDirectory,
        })
        {
            directory.Refresh();
            Ensure(
                (directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "The probe refuses a reparse-point validation path.");
        }

        string path = Path.Combine(workRoot.FullName, "fixtures-" + Guid.NewGuid().ToString("N"));
        Ensure(!Directory.Exists(path) && !File.Exists(path), "The probe fixture path already exists.");
        DirectoryInfo fixture = Directory.CreateDirectory(path);
        fixture.Refresh();
        Ensure(
            (fixture.Attributes & FileAttributes.ReparsePoint) == 0,
            "The probe fixture path is a reparse point.");
        return fixture.FullName;
    }

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

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
