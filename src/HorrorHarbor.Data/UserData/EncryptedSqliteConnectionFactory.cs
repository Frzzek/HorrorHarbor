using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace HorrorHarbor.Data.UserData;

internal static class EncryptedSqliteConnectionFactory
{
    static EncryptedSqliteConnectionFactory()
    {
        Batteries_V2.Init();
    }

    public static async Task<SqliteConnection> OpenEncryptedAsync(
        string path,
        SqliteOpenMode mode,
        ReadOnlyMemory<byte> key,
        bool useWriteAheadLog,
        CancellationToken cancellationToken)
    {
        if (key.Length != SqliteUserDataStore.EncryptionKeyLength)
        {
            throw new CryptographicException("The user-data encryption key has an invalid length.");
        }

        SqliteConnection connection = Create(path, mode);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureCipherAsync(connection, null, cancellationToken).ConfigureAwait(false);
            int result = raw.sqlite3_key(connection.Handle, key.Span);
            if (result != raw.SQLITE_OK)
            {
                throw new CryptographicException($"SQLite rejected the user-data encryption key with status {result}.");
            }

            await ConfigureAsync(connection, useWriteAheadLog, cancellationToken).ConfigureAwait(false);
            await ForceSchemaReadAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<SqliteConnection> OpenPlaintextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        SqliteConnection connection = Create(path, SqliteOpenMode.ReadWrite);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",
                cancellationToken).ConfigureAwait(false);
            await ForceSchemaReadAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static void KeyAttachedDatabase(
        SqliteConnection connection,
        string schemaName,
        ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        if (key.Length != SqliteUserDataStore.EncryptionKeyLength)
        {
            throw new CryptographicException("The attached-database encryption key has an invalid length.");
        }

        ConfigureAttachedCipher(connection, schemaName);
        int result = raw.sqlite3_key_v2(connection.Handle, utf8z.FromString(schemaName), key);
        if (result != raw.SQLITE_OK)
        {
            throw new CryptographicException($"SQLite rejected the attached-database key with status {result}.");
        }
    }

    private static SqliteConnection Create(string path, SqliteOpenMode mode) => new(
        new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());

    private static Task ConfigureAsync(
        SqliteConnection connection,
        bool useWriteAheadLog,
        CancellationToken cancellationToken) => ExecuteAsync(
            connection,
            useWriteAheadLog
                ? "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL; PRAGMA journal_mode=WAL;"
                : "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL; PRAGMA journal_mode=DELETE;",
            cancellationToken);

    private static async Task ConfigureCipherAsync(
        SqliteConnection connection,
        string? schemaName,
        CancellationToken cancellationToken)
    {
        string sql = schemaName is null
            ? "PRAGMA cipher='chacha20'; PRAGMA chacha20_plaintext_header_size=0;"
            : throw new ArgumentException("Attached schemas must use the synchronous keying path.", nameof(schemaName));
        await ExecuteAsync(connection, sql, cancellationToken).ConfigureAwait(false);
    }

    private static void ConfigureAttachedCipher(SqliteConnection connection, string schemaName)
    {
        if (!string.Equals(schemaName, "encrypted", StringComparison.Ordinal))
        {
            throw new ArgumentException("The attached encryption schema is unsupported.", nameof(schemaName));
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA encrypted.cipher='chacha20'; PRAGMA encrypted.chacha20_plaintext_header_size=0;";
        command.ExecuteNonQuery();
    }

    private static async Task ForceSchemaReadAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
