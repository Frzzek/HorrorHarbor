using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HorrorHarbor.Core.Abstractions;

namespace HorrorHarbor.App.Infrastructure;

public sealed class WindowsCredentialPlexCredentialStore : IPlexCredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistenceLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBytes = 2560;

    public Task SaveTokenAsync(
        string connectionId,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateConnectionId(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        byte[] tokenBytes = Encoding.Unicode.GetBytes(token);
        if (tokenBytes.Length > MaximumCredentialBytes || token.Any(char.IsControl))
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
            throw new ArgumentException("Plex token has an invalid format.", nameof(token));
        }

        IntPtr tokenPointer = Marshal.AllocCoTaskMem(tokenBytes.Length);
        try
        {
            Marshal.Copy(tokenBytes, 0, tokenPointer, tokenBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = GetTarget(connectionId),
                CredentialBlobSize = (uint)tokenBytes.Length,
                CredentialBlob = tokenPointer,
                Persist = CredentialPersistenceLocalMachine,
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not save the Plex credential.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
            ZeroAndFree(tokenPointer, tokenBytes.Length);
        }

        return Task.CompletedTask;
    }

    public Task<string?> LoadTokenAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateConnectionId(connectionId);
        if (!CredRead(GetTarget(connectionId), CredentialTypeGeneric, 0, out IntPtr credentialPointer))
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? Task.FromResult<string?>(null)
                : throw new Win32Exception(error, "Windows Credential Manager could not load the Plex credential.");
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(null);
            }

            string token = Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char))) ?? string.Empty;
            return Task.FromResult<string?>(token);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task DeleteTokenAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateConnectionId(connectionId);
        if (!CredDelete(GetTarget(connectionId), CredentialTypeGeneric, 0) &&
            Marshal.GetLastWin32Error() != ErrorNotFound)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not delete the Plex credential.");
        }

        return Task.CompletedTask;
    }

    private static string GetTarget(string connectionId) => $"HorrorHarbor/Plex/{connectionId}";

    private static void ValidateConnectionId(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (connectionId.Length > 128 || connectionId.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Connection ID contains unsupported characters.", nameof(connectionId));
        }
    }

    private static void ZeroAndFree(IntPtr pointer, int length)
    {
        for (var index = 0; index < length; index++)
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
