using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace HorrorHarbor.Data.UserData;

internal static class UserDataDatabaseMigrator
{
    private const string AttachedSchemaName = "encrypted";
    private const string MigrationSuffix = ".encryption-migration";
    private const string ReplacementBackupSuffix = ".encryption-replacement-backup";
    private const string RecoveryStateSuffix = ".encryption-recovery-state.json";

    public static async Task ReconcileAsync(
        string databasePath,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken)
    {
        string migrationPath = databasePath + MigrationSuffix;
        string replacementBackupPath = databasePath + ReplacementBackupSuffix;
        string recoveryStatePath = databasePath + RecoveryStateSuffix;
        bool backupExists = File.Exists(replacementBackupPath);
        bool stateExists = File.Exists(recoveryStatePath);
        if (!backupExists && !stateExists)
        {
            return;
        }

        if (!stateExists)
        {
            throw new IOException(
                "A user-data recovery backup exists without its integrity state and was preserved.");
        }

        MigrationRecoveryState state = ReadRecoveryState(recoveryStatePath);
        bool primaryExists = File.Exists(databasePath);
        if (!backupExists)
        {
            if (!primaryExists)
            {
                throw new IOException(
                    "The user-data recovery state has neither a primary database nor a backup.");
            }

            string primaryHash = HashSource(databasePath);
            if (string.Equals(primaryHash, state.SourceSha256, StringComparison.Ordinal))
            {
                DeleteDatabaseArtifacts(migrationPath);
                DeleteFileDurably(recoveryStatePath);
                return;
            }

            if (string.Equals(primaryHash, state.EncryptedSha256, StringComparison.Ordinal))
            {
                await VerifyMigrationAsync(
                    databasePath,
                    key,
                    state.RowCounts,
                    cancellationToken).ConfigureAwait(false);
                DeleteFileDurably(recoveryStatePath);
                return;
            }

            throw new IOException(
                "The user-data recovery primary does not match its integrity state and was preserved.");
        }

        if (!primaryExists)
        {
            if (!string.Equals(
                    HashSource(replacementBackupPath),
                    state.SourceSha256,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "The user-data recovery backup changed and was preserved.");
            }

            MoveFileDurablyNoOverwrite(replacementBackupPath, databasePath);
            DeleteDatabaseArtifacts(migrationPath);
            DeleteFileDurably(recoveryStatePath);
            return;
        }

        await VerifyMigrationAsync(
            databasePath,
            key,
            state.RowCounts,
            cancellationToken).ConfigureAwait(false);
        using (FileStream primaryGuard = OpenPrimaryValidationGuard(databasePath))
        {
            bool completedMigration =
                string.Equals(
                    HashSource(primaryGuard),
                    state.EncryptedSha256,
                    StringComparison.Ordinal) &&
                !HasSidecars(databasePath) &&
                !HasSidecars(replacementBackupPath);
            if (!completedMigration)
            {
                throw new IOException(
                    "The user-data recovery files are ambiguous and were preserved without overwrite.");
            }

            DeleteExactFileIfHashMatches(
                replacementBackupPath,
                state.SourceSha256);
        }

        DeleteFileDurably(recoveryStatePath);
    }

    public static async Task MigratePlaintextAsync(
        string databasePath,
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken,
        Action? beforeAtomicReplacement = null,
        Action? afterEncryptedPrimaryInstalled = null)
    {
        string migrationPath = databasePath + MigrationSuffix;
        string replacementBackupPath =
            databasePath + ReplacementBackupSuffix;
        string recoveryStatePath = databasePath + RecoveryStateSuffix;
        if (File.Exists(replacementBackupPath) || File.Exists(recoveryStatePath))
        {
            throw new IOException(
                "Preserved user-data recovery artifacts require reconciliation before migration can continue.");
        }

        DeleteDatabaseArtifacts(migrationPath);
        try
        {
            CreateEmptyMigrationFile(migrationPath);
            await using (SqliteConnection checkpointConnection =
                await EncryptedSqliteConnectionFactory.OpenPlaintextAsync(
                    databasePath,
                    cancellationToken).ConfigureAwait(false))
            {
                await UserDataSchema.ValidateAsync(checkpointConnection, cancellationToken).ConfigureAwait(false);
                await CheckpointAndDisableWalAsync(checkpointConnection, cancellationToken).ConfigureAwait(false);
            }

            EnsureSidecarsAbsent(databasePath);
            string sourceHash = HashSource(databasePath);
            UserDataRowCounts sourceCounts;
            await using (SqliteConnection source =
                await EncryptedSqliteConnectionFactory.OpenPlaintextAsync(
                    databasePath,
                    cancellationToken).ConfigureAwait(false))
            {
                sourceCounts = await UserDataSchema.ValidateAsync(source, cancellationToken).ConfigureAwait(false);
                await AttachAndCopyAsync(
                    source,
                    migrationPath,
                    key,
                    sourceCounts,
                    cancellationToken).ConfigureAwait(false);
            }

            using FileStream sourceGuard = OpenSourceReplacementGuard(databasePath);
            if (!string.Equals(sourceHash, HashSource(sourceGuard), StringComparison.Ordinal))
            {
                throw new IOException(
                    "The plaintext user-data database changed during migration and was preserved.");
            }
            await VerifyMigrationAsync(migrationPath, key, sourceCounts, cancellationToken).ConfigureAwait(false);
            FlushToDisk(migrationPath);
            EnsureSidecarsAbsent(databasePath);
            string encryptedHash = HashSource(migrationPath);
            WriteRecoveryState(
                recoveryStatePath,
                new MigrationRecoveryState(sourceHash, encryptedHash, sourceCounts));
            beforeAtomicReplacement?.Invoke();
            MoveFileDurablyNoOverwrite(databasePath, replacementBackupPath);
            bool displacedSourceMatches =
                string.Equals(
                    sourceHash,
                    HashSource(sourceGuard),
                    StringComparison.Ordinal) &&
                string.Equals(
                    sourceHash,
                    HashSource(replacementBackupPath),
                    StringComparison.Ordinal) &&
                !HasSidecars(replacementBackupPath);
            if (!displacedSourceMatches)
            {
                if (!File.Exists(databasePath))
                {
                    MoveFileDurablyNoOverwrite(replacementBackupPath, databasePath);
                    DeleteFileDurably(recoveryStatePath);
                }

                throw new IOException(
                    "The plaintext user-data database path changed during migration and was preserved.");
            }

            MoveFileDurablyNoOverwrite(migrationPath, databasePath);
            afterEncryptedPrimaryInstalled?.Invoke();
            using (FileStream primaryGuard = OpenPrimaryValidationGuard(databasePath))
            {
                bool completedMigration =
                    string.Equals(
                        HashSource(primaryGuard),
                        encryptedHash,
                        StringComparison.Ordinal) &&
                    !HasSidecars(databasePath) &&
                    !HasSidecars(replacementBackupPath);
                if (!completedMigration)
                {
                    throw new IOException(
                        "The encrypted user-data database path changed after migration; all recovery files were preserved.");
                }

                DeleteExactFileIfHashMatches(
                    replacementBackupPath,
                    sourceHash);
            }

            DeleteFileDurably(recoveryStatePath);
        }
        finally
        {
            DeleteDatabaseArtifacts(migrationPath);
            DeleteUnstartedRecoveryStateIfSafe(
                databasePath,
                replacementBackupPath,
                recoveryStatePath);
        }
    }

    private static async Task AttachAndCopyAsync(
        SqliteConnection source,
        string migrationPath,
        ReadOnlyMemory<byte> key,
        UserDataRowCounts sourceCounts,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand attachCommand = source.CreateCommand())
        {
            attachCommand.CommandText = "ATTACH DATABASE $path AS encrypted;";
            attachCommand.Parameters.AddWithValue("$path", migrationPath);
            await attachCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        bool attached = true;
        try
        {
            EncryptedSqliteConnectionFactory.KeyAttachedDatabase(
                source,
                AttachedSchemaName,
                key.Span);
            await using (SqliteCommand schemaReadCommand = source.CreateCommand())
            {
                schemaReadCommand.CommandText = "SELECT COUNT(*) FROM encrypted.sqlite_master;";
                await schemaReadCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }

            await UserDataSchema.CreateEncryptedAttachedAsync(source, cancellationToken).ConfigureAwait(false);
            bool transactionActive = false;
            try
            {
                await ExecuteAsync(source, "BEGIN EXCLUSIVE;", cancellationToken).ConfigureAwait(false);
                transactionActive = true;
                await ExecuteAsync(
                    source,
                    UserDataSchema.CopyToEncryptedSchemaSql,
                    cancellationToken).ConfigureAwait(false);

                UserDataRowCounts copiedCounts =
                    await UserDataSchema.ReadCountsAsync(source, AttachedSchemaName, cancellationToken)
                        .ConfigureAwait(false);
                if (copiedCounts != sourceCounts)
                {
                    throw new InvalidDataException(
                        "The encrypted migration row counts do not match the plaintext source.");
                }

                await ExecuteAsync(source, "COMMIT;", cancellationToken).ConfigureAwait(false);
                transactionActive = false;

                await ExecuteAsync(source, "DETACH DATABASE encrypted;", cancellationToken).ConfigureAwait(false);
                attached = false;
            }
            finally
            {
                if (transactionActive)
                {
                    await ExecuteAsync(source, "ROLLBACK;", CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (attached)
            {
                await source.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task VerifyMigrationAsync(
        string migrationPath,
        ReadOnlyMemory<byte> key,
        UserDataRowCounts expectedCounts,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection =
            await EncryptedSqliteConnectionFactory.OpenEncryptedAsync(
                migrationPath,
                SqliteOpenMode.ReadWrite,
                key,
                useWriteAheadLog: false,
                cancellationToken).ConfigureAwait(false);
        UserDataRowCounts actualCounts =
            await UserDataSchema.ValidateAsync(connection, cancellationToken).ConfigureAwait(false);
        if (actualCounts != expectedCounts)
        {
            throw new InvalidDataException("The encrypted migration failed its independent row-count verification.");
        }
    }

    private static async Task CheckpointAndDisableWalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand checkpointCommand = connection.CreateCommand())
        {
            checkpointCommand.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await using SqliteDataReader reader =
                await checkpointCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.GetInt32(0) != 0)
            {
                throw new IOException("The plaintext user-data WAL is busy and cannot be migrated safely.");
            }
        }

        await using SqliteCommand journalCommand = connection.CreateCommand();
        journalCommand.CommandText = "PRAGMA journal_mode=DELETE;";
        string? mode = Convert.ToString(
            await journalCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(mode, "delete", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The plaintext user-data journal mode could not be made migration-safe.");
        }
    }

    private static void FlushToDisk(string path)
    {
        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read);
        RandomAccess.FlushToDisk(handle);
    }

    private static FileStream OpenSourceReplacementGuard(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read | FileShare.Delete,
        bufferSize: 4096,
        FileOptions.SequentialScan);

    private static FileStream OpenPrimaryValidationGuard(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.SequentialScan);

    private static string HashSource(FileStream source)
    {
        source.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(source));
        source.Position = 0;
        return hash;
    }

    private static string HashSource(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void DeleteExactFileIfHashMatches(
        string path,
        string expectedHash)
    {
        using SafeFileHandle handle = CreateFile(
            path,
            GenericRead | DeleteAccess,
            FileShare.Read,
            IntPtr.Zero,
            FileMode.Open,
            FileAttributes.Normal | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException(
                "The validated user-data recovery backup could not be opened safely.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfo,
                out FileAttributeTagInformation attributes,
                (uint)Marshal.SizeOf<FileAttributeTagInformation>()))
        {
            throw new IOException(
                "The validated user-data recovery backup type could not be inspected safely.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
        if ((attributes.FileAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The user-data recovery backup became a reparse point and was preserved.");
        }

        using (var stream = new FileStream(handle, FileAccess.Read, bufferSize: 4096, isAsync: false))
        {
            if (!string.Equals(
                    HashSource(stream),
                    expectedHash,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "The user-data recovery backup changed and was preserved.");
            }

            var disposition = new FileDispositionInformation { DeleteFile = 1 };
            if (!SetFileInformationByHandle(
                    handle,
                    FileDispositionInfo,
                    ref disposition,
                    (uint)Marshal.SizeOf<FileDispositionInformation>()))
            {
                throw new IOException(
                    "The validated user-data recovery backup could not be deleted safely.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }

        FlushDirectoryMetadata(path);
    }

    private static void WriteRecoveryState(
        string path,
        MigrationRecoveryState state)
    {
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(state);
        using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
        FlushDirectoryMetadata(path);
    }

    private static MigrationRecoveryState ReadRecoveryState(string path)
    {
        byte[] content = File.ReadAllBytes(path);
        if (content.Length is <= 0 or > 4096)
        {
            throw new InvalidDataException(
                "The user-data migration recovery state has an invalid length.");
        }

        MigrationRecoveryState? state =
            JsonSerializer.Deserialize<MigrationRecoveryState>(content);
        if (state is null ||
            state.SourceSha256.Length != 64 ||
            state.EncryptedSha256.Length != 64)
        {
            throw new InvalidDataException(
                "The user-data migration recovery state has an invalid format.");
        }

        return state;
    }

    private static void DeleteUnstartedRecoveryStateIfSafe(
        string databasePath,
        string replacementBackupPath,
        string recoveryStatePath)
    {
        if (!File.Exists(recoveryStatePath) ||
            File.Exists(replacementBackupPath) ||
            !File.Exists(databasePath))
        {
            return;
        }

        MigrationRecoveryState state = ReadRecoveryState(recoveryStatePath);
        if (string.Equals(
                HashSource(databasePath),
                state.SourceSha256,
                StringComparison.Ordinal))
        {
            DeleteFileDurably(recoveryStatePath);
        }
    }

    private static void MoveFileDurablyNoOverwrite(string sourcePath, string destinationPath)
    {
        if (!MoveFileEx(sourcePath, destinationPath, MoveFileWriteThrough))
        {
            throw new IOException(
                "A user-data recovery file could not be moved durably without overwrite.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static void DeleteFileDurably(string path)
    {
        File.Delete(path);
        FlushDirectoryMetadata(path);
    }

    private static void FlushDirectoryMetadata(string childPath)
    {
        string? directoryPath = Path.GetDirectoryName(Path.GetFullPath(childPath));
        if (string.IsNullOrEmpty(directoryPath))
        {
            throw new IOException("The user-data artifact does not have a parent directory.");
        }

        using SafeFileHandle directoryHandle = CreateFile(
            directoryPath,
            GenericRead | GenericWrite,
            FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (directoryHandle.IsInvalid)
        {
            throw new IOException(
                "The user-data directory could not be opened for a durability barrier.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
        if (!FlushFileBuffers(directoryHandle))
        {
            throw new IOException(
                "The user-data directory durability barrier failed.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static void EnsureSidecarsAbsent(string path)
    {
        foreach (string sidecar in new[] { path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(sidecar))
            {
                throw new IOException(
                    "A plaintext user-data sidecar appeared during migration and was preserved.");
            }
        }
    }

    private static bool HasSidecars(string path) =>
        File.Exists(path + "-wal") ||
        File.Exists(path + "-shm") ||
        File.Exists(path + "-journal");

    private static void CreateEmptyMigrationFile(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.WriteThrough);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteDatabaseArtifacts(string path)
    {
        File.Delete(path);
        DeleteSidecars(path);
    }

    private static void DeleteSidecars(string path)
    {
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        File.Delete(path + "-journal");
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

    private sealed record MigrationRecoveryState(
        string SourceSha256,
        string EncryptedSha256,
        UserDataRowCounts RowCounts);

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint MoveFileWriteThrough = 0x00000008;
    private const int FileDispositionInfo = 4;
    private const int FileAttributeTagInfo = 9;
    private const FileAttributes FileFlagOpenReparsePoint = (FileAttributes)0x00200000;
    private const FileAttributes FileFlagBackupSemantics = (FileAttributes)0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation
    {
        public byte DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInformation
    {
        public FileAttributes FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        FileAttributes flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        out FileAttributeTagInformation fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string newFileName,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle fileHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        ref FileDispositionInformation fileInformation,
        uint bufferSize);
}
