using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Data.UserData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class UserDataMultiprocessIntegrationTests
{
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConcurrentProcessesInitializeOneEncryptedDatabaseWithOneProtectedKey()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        string releasePath = Path.Combine(directory, "release.signal");
        string firstReady = Path.Combine(directory, "first.ready");
        string secondReady = Path.Combine(directory, "second.ready");
        try
        {
            RunningProbe first = StartProbe(
                "initialize", databasePath, keyPath, firstReady, releasePath,
                "multiprocess.first", "first");
            RunningProbe second = StartProbe(
                "initialize", databasePath, keyPath, secondReady, releasePath,
                "multiprocess.second", "second");
            await WaitForFilesAsync(firstReady, secondReady);
            CreateSignal(releasePath);

            ProbeResult[] results = await Task.WhenAll(first.CompleteAsync(), second.CompleteAsync());
            Assert.IsTrue(results.All(static result => result.ExitCode == 0), FormatResults(results));

            var keyProvider = new DpapiUserDataEncryptionKeyProvider(keyPath);
            using var store = new SqliteUserDataStore(databasePath, keyProvider);
            await store.InitializeAsync();
            Assert.AreEqual("first", await store.LoadSettingAsync("multiprocess.first"));
            Assert.AreEqual("second", await store.LoadSettingAsync("multiprocess.second"));
            Assert.IsFalse(File.Exists(databasePath + ".initialization.lock"));
            AssertNoMigrationArtifacts(databasePath);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConcurrentPlaintextWriterCannotLoseCommittedRowsDuringMigration()
    {
        string directory = CreateTestDirectory();
        string databasePath = Path.Combine(directory, "user-data.db");
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        string writerReady = Path.Combine(directory, "writer.ready");
        string writerRelease = Path.Combine(directory, "writer.release");
        string writerCommitted = Path.Combine(directory, "writer.committed");
        string writerStop = Path.Combine(directory, "writer.stop");
        string ledgerPath = Path.Combine(directory, "writer-ledger.txt");
        string migratorReady = Path.Combine(directory, "migrator.ready");
        string migratorRelease = Path.Combine(directory, "migrator.release");
        RunningProbe? writer = null;
        try
        {
            await UserDataEncryptionIntegrationTests.CreatePlaintextVersionOneDatabaseAsync(
                databasePath,
                includeUnknownTable: false);
            writer = StartProbe(
                "write", databasePath, writerReady, writerRelease,
                writerCommitted, writerStop, ledgerPath);
            await WaitForFilesAsync(writerReady);
            CreateSignal(writerRelease);
            await WaitForFilesAsync(writerCommitted);

            RunningProbe migrator = StartProbe(
                "initialize", databasePath, keyPath, migratorReady, migratorRelease,
                "multiprocess.migrator", "completed");
            await WaitForFilesAsync(migratorReady);
            CreateSignal(migratorRelease);
            ProbeResult migrationResult = await migrator.CompleteAsync();
            CreateSignal(writerStop);
            ProbeResult writerResult = await writer.CompleteAsync();
            writer = null;

            Assert.AreEqual(0, writerResult.ExitCode, FormatResults([writerResult]));
            string[] committedKeys = await File.ReadAllLinesAsync(ledgerPath);
            Assert.IsNotEmpty(committedKeys);
            Assert.HasCount(committedKeys.Length, committedKeys.Distinct(StringComparer.Ordinal));

            byte[] prefix = new byte[SqliteHeader.Length];
            await using (FileStream database = File.OpenRead(databasePath))
            {
                Assert.AreEqual(prefix.Length, await database.ReadAsync(prefix));
            }

            if (prefix.AsSpan().SequenceEqual(SqliteHeader))
            {
                Assert.AreNotEqual(0, migrationResult.ExitCode, "Migration unexpectedly reported success for plaintext output.");
                await AssertPlaintextSettingsAsync(databasePath, committedKeys);
            }
            else
            {
                Assert.AreEqual(0, migrationResult.ExitCode, FormatResults([migrationResult]));
                await AssertEncryptedSettingsAsync(databasePath, keyPath, committedKeys);
            }

            AssertNoMigrationArtifacts(databasePath);
            Assert.IsFalse(File.Exists(databasePath + ".initialization.lock"));
        }
        finally
        {
            if (writer is not null)
            {
                if (!File.Exists(writerStop))
                {
                    CreateSignal(writerStop);
                }

                await writer.CompleteAsync();
            }

            DeleteTestDirectory(directory);
        }
    }

    private static async Task AssertEncryptedSettingsAsync(
        string databasePath,
        string keyPath,
        IEnumerable<string> keys)
    {
        var keyProvider = new DpapiUserDataEncryptionKeyProvider(keyPath);
        using var store = new SqliteUserDataStore(databasePath, keyProvider);
        await store.InitializeAsync();
        foreach (string key in keys)
        {
            Assert.AreEqual("committed", await store.LoadSettingAsync(key), $"Missing committed key: {key}");
        }
    }

    private static async Task AssertPlaintextSettingsAsync(
        string databasePath,
        IEnumerable<string> keys)
    {
        SQLitePCL.Batteries_V2.Init();
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        foreach (string key in keys)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT setting_value FROM user_setting WHERE setting_key = $key;";
            command.Parameters.AddWithValue("$key", key);
            Assert.AreEqual(
                "committed",
                Convert.ToString(
                    await command.ExecuteScalarAsync(),
                    CultureInfo.InvariantCulture));
        }
    }

    private static RunningProbe StartProbe(params string[] arguments)
    {
        string root = FindRepositoryRoot();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("The test build configuration could not be resolved.");
        string probePath = Path.Combine(
            root,
            "tests",
            "HorrorHarbor.StorageProcessProbe",
            "bin",
            configuration,
            "net10.0-windows",
            "HorrorHarbor.StorageProcessProbe.dll");
        Assert.IsTrue(File.Exists(probePath), "The storage child-process probe was not built.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = root,
        };
        startInfo.ArgumentList.Add(probePath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The storage child-process probe could not start.");
        return new RunningProbe(process);
    }

    private static async Task WaitForFilesAsync(params string[] paths)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (paths.Any(path => !File.Exists(path)))
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("A storage child-process signal timed out.");
            }

            await Task.Delay(20);
        }
    }

    private static void CreateSignal(string path)
    {
        using FileStream signal = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.WriteThrough);
        signal.Flush(flushToDisk: true);
    }

    private static string CreateTestDirectory()
    {
        string directory = Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "test-fixtures",
            "storage-multiprocess",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HorrorHarbor.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The HorrorHarbor repository root could not be resolved.");
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

    private static string FormatResults(IEnumerable<ProbeResult> results) => string.Join(
        Environment.NewLine,
        results.Select(static result =>
            $"Exit={result.ExitCode}; stdout={result.StandardOutput}; stderr={result.StandardError}"));

    private static void DeleteTestDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RunningProbe(Process process)
    {
        private readonly Task<string> _standardOutput = process.StandardOutput.ReadToEndAsync();
        private readonly Task<string> _standardError = process.StandardError.ReadToEndAsync();

        public async Task<ProbeResult> CompleteAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("The storage child-process probe timed out.");
            }

            string output = await _standardOutput;
            string error = await _standardError;
            int exitCode = process.ExitCode;
            process.Dispose();
            return new ProbeResult(exitCode, output.Trim(), error.Trim());
        }
    }

    private sealed record ProbeResult(int ExitCode, string StandardOutput, string StandardError);
}
