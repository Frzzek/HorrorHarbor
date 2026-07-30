using System.IO;
using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Data.UserData;
using Microsoft.Data.Sqlite;

namespace HorrorHarbor.StorageProcessProbe;

internal static class Program
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            return args.Length == 7 && string.Equals(args[0], "initialize", StringComparison.Ordinal)
                ? await InitializeAsync(args).ConfigureAwait(false)
                : args.Length == 7 && string.Equals(args[0], "write", StringComparison.Ordinal)
                    ? await WritePlaintextAsync(args).ConfigureAwait(false)
                    : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetType().Name);
            return 1;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static async Task<int> InitializeAsync(string[] args)
    {
        string databasePath = Path.GetFullPath(args[1]);
        string keyPath = Path.GetFullPath(args[2]);
        CreateSignal(args[3]);
        await WaitForSignalAsync(args[4]).ConfigureAwait(false);

        var keyProvider = new DpapiUserDataEncryptionKeyProvider(keyPath);
        using var store = new SqliteUserDataStore(databasePath, keyProvider);
        await store.InitializeAsync().ConfigureAwait(false);
        await store.SaveSettingAsync(args[5], args[6]).ConfigureAwait(false);
        if (!string.Equals(
                await store.LoadSettingAsync(args[5]).ConfigureAwait(false),
                args[6],
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The child-process setting did not round-trip.");
        }

        Console.WriteLine("{\"status\":\"PASS\",\"operation\":\"initialize\"}");
        return 0;
    }

    private static async Task<int> WritePlaintextAsync(string[] args)
    {
        string databasePath = Path.GetFullPath(args[1]);
        string readyPath = Path.GetFullPath(args[2]);
        string releasePath = Path.GetFullPath(args[3]);
        string committedPath = Path.GetFullPath(args[4]);
        string stopPath = Path.GetFullPath(args[5]);
        string ledgerPath = Path.GetFullPath(args[6]);
        SQLitePCL.Batteries_V2.Init();

        CreateSignal(readyPath);
        await WaitForSignalAsync(releasePath).ConfigureAwait(false);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync().ConfigureAwait(false);
        await using var ledger = new FileStream(
            ledgerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(ledger, leaveOpen: true);

        var committed = 0;
        while (!File.Exists(stopPath) && committed < 1_000)
        {
            string key = $"multiprocess.commit.{committed:D4}";
            try
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO user_setting(setting_key, setting_value, updated_utc)
                    VALUES ($key, 'committed', '2026-07-29T00:00:00.0000000Z');
                    """;
                command.Parameters.AddWithValue("$key", key);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                await writer.WriteLineAsync(key).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                ledger.Flush(flushToDisk: true);
                committed++;
                if (committed == 1)
                {
                    CreateSignal(committedPath);
                }
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 5)
            {
                await Task.Delay(5).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 26)
            {
                break;
            }

            await Task.Delay(5).ConfigureAwait(false);
        }

        Console.WriteLine(
            $"{{\"status\":\"PASS\",\"operation\":\"write\",\"committed\":{committed}}}");
        return 0;
    }

    private static async Task WaitForSignalAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        DateTime deadline = DateTime.UtcNow + SignalTimeout;
        while (!File.Exists(fullPath))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("A synthetic process signal timed out.");
            }

            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    private static void CreateSignal(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using FileStream signal = new(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.WriteThrough);
        signal.Flush(flushToDisk: true);
    }
}
