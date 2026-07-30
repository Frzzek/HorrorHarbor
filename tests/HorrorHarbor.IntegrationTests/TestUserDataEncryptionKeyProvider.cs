using System.Security.Cryptography;
using HorrorHarbor.Data.UserData;

namespace HorrorHarbor.IntegrationTests;

internal sealed class TestUserDataEncryptionKeyProvider : IUserDataEncryptionKeyProvider, IDisposable
{
    private readonly object _sync = new();
    private byte[]? _key;

    public TestUserDataEncryptionKeyProvider()
    {
    }

    public TestUserDataEncryptionKeyProvider(ReadOnlySpan<byte> key)
    {
        if (key.Length != SqliteUserDataStore.EncryptionKeyLength)
        {
            throw new ArgumentException("Test keys must contain 32 bytes.", nameof(key));
        }

        _key = key.ToArray();
    }

    public Task<byte[]?> LoadKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult(_key?.ToArray());
        }
    }

    public Task<bool> TrySaveKeyAsync(
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (key.Length != SqliteUserDataStore.EncryptionKeyLength)
        {
            throw new ArgumentException("Test keys must contain 32 bytes.", nameof(key));
        }

        lock (_sync)
        {
            if (_key is not null)
            {
                return Task.FromResult(false);
            }

            _key = key.ToArray();
            return Task.FromResult(true);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_key is not null)
            {
                CryptographicOperations.ZeroMemory(_key);
                _key = null;
            }
        }
    }
}
