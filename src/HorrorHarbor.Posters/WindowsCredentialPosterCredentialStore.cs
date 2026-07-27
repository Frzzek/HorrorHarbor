using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace HorrorHarbor.Posters;

public sealed class WindowsCredentialPosterCredentialStore : IPosterCredentialStore
{
    private const string Target = "HorrorHarbor/TMDB/primary";
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistenceLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBytes = 2560;

    public Task SaveTmdbTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        byte[] bytes = Encoding.Unicode.GetBytes(token.Trim());
        if (bytes.Length > MaximumCredentialBytes || token.Any(char.IsControl))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentException("TMDB read-access token has an invalid format.", nameof(token));
        }

        IntPtr pointer = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = pointer,
                Persist = CredentialPersistenceLocalMachine,
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not save the TMDB credential.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            ZeroAndFree(pointer, bytes.Length);
        }

        return Task.CompletedTask;
    }

    public Task<string?> LoadTmdbTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(Target, CredentialTypeGeneric, 0, out IntPtr credentialPointer))
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? Task.FromResult<string?>(null)
                : throw new Win32Exception(error, "Windows Credential Manager could not load the TMDB credential.");
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(null);
            }

            if (credential.CredentialBlobSize > MaximumCredentialBytes ||
                credential.CredentialBlobSize % sizeof(char) != 0)
            {
                throw new InvalidDataException("The stored TMDB credential has an invalid size.");
            }

            string value = Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char))) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            {
                throw new InvalidDataException("The stored TMDB credential has an invalid format.");
            }

            return Task.FromResult<string?>(value);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task DeleteTmdbTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredDelete(Target, CredentialTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not delete the TMDB credential.");
        }

        return Task.CompletedTask;
    }

    private static void ZeroAndFree(IntPtr pointer, int length)
    {
        for (int index = 0; index < length; index++)
        {
            Marshal.WriteByte(pointer, index, 0);
        }

        Marshal.FreeCoTaskMem(pointer);
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr credentialPointer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
}
