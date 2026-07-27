using System.IO;
using System.Security.Cryptography;
using System.Text;
using HorrorHarbor.Core.Abstractions;

namespace HorrorHarbor.App.Infrastructure;

public sealed class DpapiPlexCredentialStore : IPlexCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HorrorHarbor.Plex.v1");
    private readonly string _directory;

    public DpapiPlexCredentialStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HorrorHarbor",
            "credentials");
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveTokenAsync(
        string connectionId,
        string token,
        CancellationToken cancellationToken = default)
    {
        Validate(connectionId, token);
        byte[] plaintext = Encoding.UTF8.GetBytes(token);
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            string path = ResolvePath(connectionId);
            string temporaryPath = path + $".tmp-{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<string?> LoadTokenAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(connectionId);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Task DeleteTokenAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(ResolvePath(connectionId));
        return Task.CompletedTask;
    }

    private string ResolvePath(string connectionId)
    {
        ValidateConnectionId(connectionId);
        return Path.Combine(_directory, connectionId + ".dpapi");
    }

    private static void Validate(string connectionId, string token)
    {
        ValidateConnectionId(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Length > 1024 || token.Any(char.IsControl))
        {
            throw new ArgumentException("Plex token has an invalid format.", nameof(token));
        }
    }

    private static void ValidateConnectionId(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (connectionId.Length > 128 || connectionId.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Connection ID contains unsupported characters.", nameof(connectionId));
        }
    }
}
