namespace HorrorHarbor.Data.UserData;

public interface IUserDataEncryptionKeyProvider
{
    Task<byte[]?> LoadKeyAsync(CancellationToken cancellationToken = default);

    Task<bool> TrySaveKeyAsync(
        ReadOnlyMemory<byte> key,
        CancellationToken cancellationToken = default);
}
