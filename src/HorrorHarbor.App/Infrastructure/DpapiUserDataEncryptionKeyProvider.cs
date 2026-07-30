using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HorrorHarbor.Data.UserData;
using Microsoft.Win32.SafeHandles;

namespace HorrorHarbor.App.Infrastructure;

public sealed class DpapiUserDataEncryptionKeyProvider : IUserDataEncryptionKeyProvider
{
    private const int EncryptionKeyLength = 32;
    private const int MaximumProtectedFileLength = 4096;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HorrorHarbor.UserData.v1");
    private readonly string _path;
    private readonly Action<SafeFileHandle> _flushPublishedKey;

    public DpapiUserDataEncryptionKeyProvider()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HorrorHarbor",
            "credentials",
            "user-data-key-v1.dpapi"))
    {
    }

    public DpapiUserDataEncryptionKeyProvider(string path)
        : this(path, FlushPublishedKey)
    {
    }

    internal DpapiUserDataEncryptionKeyProvider(
        string path,
        Action<SafeFileHandle> flushPublishedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(flushPublishedKey);
        _path = Path.GetFullPath(path);
        _flushPublishedKey = flushPublishedKey;
        string? directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The key path must include a directory.", nameof(path));
        }

        Directory.CreateDirectory(directory);
    }

    public async Task<byte[]?> LoadKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        long length = new FileInfo(_path).Length;
        if (length is <= 0 or > MaximumProtectedFileLength)
        {
            throw new CryptographicException("The protected user-data key has an invalid length.");
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] plaintext = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);
            if (plaintext.Length == EncryptionKeyLength)
            {
                return plaintext;
            }

            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException("The protected user-data key has an invalid format.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public async Task<bool> TrySaveKeyAsync(
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        if (key.Length != EncryptionKeyLength)
        {
            throw new ArgumentException("The user-data encryption key must contain 32 bytes.", nameof(key));
        }

        byte[] plaintext = key.ToArray();
        byte[]? protectedBytes = null;
        SafeFileHandle? handle = null;
        bool published = false;
        try
        {
            protectedBytes = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);
            handle = CreateFile(
                _path,
                GenericWrite | DeleteAccess,
                FileShare.None,
                IntPtr.Zero,
                FileMode.CreateNew,
                FileAttributes.Normal | FileFlagOverlapped | FileFlagWriteThrough,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                if (error is ErrorFileExists or ErrorAlreadyExists)
                {
                    return false;
                }

                throw new IOException(
                    "The protected user-data key could not be created safely.",
                    new Win32Exception(error));
            }

            await using (var stream = new FileStream(
                handle,
                FileAccess.Write,
                bufferSize: 4096,
                isAsync: true))
            {
                handle = null;
                try
                {
                    await stream.WriteAsync(protectedBytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                    cancellationToken.ThrowIfCancellationRequested();
                    FlushDirectoryMetadata(_path);
                    published = true;
                    _flushPublishedKey(stream.SafeFileHandle);
                }
                catch when (!published)
                {
                    DeleteExactHandle(stream.SafeFileHandle);
                    throw;
                }
            }

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            if (handle is not null)
            {
                handle.Dispose();
            }
        }
    }

    private static void FlushPublishedKey(SafeFileHandle handle)
    {
        RandomAccess.FlushToDisk(handle);
    }

    private static void DeleteExactHandle(SafeFileHandle handle)
    {
        var disposition = new FileDispositionInformation { DeleteFile = 1 };
        if (!SetFileInformationByHandle(
                handle,
                FileDispositionInfo,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInformation>()))
        {
            throw new IOException(
                "The incomplete protected key could not be deleted safely.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static void FlushDirectoryMetadata(string childPath)
    {
        string? directoryPath = Path.GetDirectoryName(Path.GetFullPath(childPath));
        if (string.IsNullOrEmpty(directoryPath))
        {
            throw new IOException("The protected key does not have a parent directory.");
        }

        using SafeFileHandle directoryHandle = CreateFile(
            directoryPath,
            GenericRead | GenericWrite,
            FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (directoryHandle.IsInvalid || !FlushFileBuffers(directoryHandle))
        {
            throw new IOException(
                "The protected key directory durability barrier failed.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x00010000;
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;
    private const int FileDispositionInfo = 4;
    private const FileAttributes FileFlagOverlapped = (FileAttributes)0x40000000;
    private const FileAttributes FileFlagWriteThrough = unchecked((FileAttributes)0x80000000);
    private const FileAttributes FileFlagBackupSemantics = (FileAttributes)0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation
    {
        public byte DeleteFile;
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
    private static extern bool FlushFileBuffers(SafeFileHandle fileHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        ref FileDispositionInformation fileInformation,
        uint bufferSize);
}
