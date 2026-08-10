using System.Security.Cryptography;
using System.Text;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Posters;

namespace HorrorHarbor.App.Infrastructure;

public sealed class PosterCustomizationStore(IUserDataStore userDataStore)
{
    private const string SettingPrefix = "poster.custom.v1.";

    public async Task<PosterGenerationOptions?> LoadAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        string? value = await userDataStore.LoadSettingAsync(
            CreateSettingKey(candidateId),
            cancellationToken).ConfigureAwait(false);
        return PosterGenerationOptions.TryParse(value, out PosterGenerationOptions options) &&
               !options.IsAutomatic
            ? options
            : null;
    }

    public Task SaveAsync(
        string candidateId,
        PosterGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        PosterGenerationOptions normalized = options.Normalize();
        return normalized.IsAutomatic
            ? ClearAsync(candidateId, cancellationToken)
            : userDataStore.SaveSettingAsync(
                CreateSettingKey(candidateId),
                normalized.ToCanonicalString(),
                cancellationToken);
    }

    public Task ClearAsync(
        string candidateId,
        CancellationToken cancellationToken = default) =>
        userDataStore.SaveSettingAsync(
            CreateSettingKey(candidateId),
            string.Empty,
            cancellationToken);

    internal static string CreateSettingKey(string candidateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        byte[] identity = SHA256.HashData(Encoding.UTF8.GetBytes(candidateId.Trim()));
        return SettingPrefix + Convert.ToHexStringLower(identity);
    }
}
