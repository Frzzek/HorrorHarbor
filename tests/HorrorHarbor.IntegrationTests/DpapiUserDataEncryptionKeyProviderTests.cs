using System.Security.Cryptography;
using HorrorHarbor.App.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class DpapiUserDataEncryptionKeyProviderTests
{
    [TestMethod]
    public async Task ProtectedKeyRoundTripsWithoutPersistingPlaintext()
    {
        string directory = CreateTestDirectory();
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var provider = new DpapiUserDataEncryptionKeyProvider(keyPath);

            Assert.IsTrue(await provider.TrySaveKeyAsync(key));
            byte[] protectedBytes = await File.ReadAllBytesAsync(keyPath);
            Assert.IsFalse(protectedBytes.AsSpan().IndexOf(key) >= 0);

            byte[]? loaded = await provider.LoadKeyAsync();
            Assert.IsNotNull(loaded);
            try
            {
                CollectionAssert.AreEqual(key, loaded);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(loaded);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task MalformedProtectedFileIsRejected()
    {
        string directory = CreateTestDirectory();
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        try
        {
            var provider = new DpapiUserDataEncryptionKeyProvider(keyPath);
            await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(64));

            await Assert.ThrowsAsync<CryptographicException>(() => provider.LoadKeyAsync());
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ConcurrentCreationHasOneWinnerAndPreservesItsKey()
    {
        string directory = CreateTestDirectory();
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        byte[] firstKey = RepeatedKey(0x31);
        byte[] secondKey = RepeatedKey(0x72);
        try
        {
            var firstProvider = new DpapiUserDataEncryptionKeyProvider(keyPath);
            var secondProvider = new DpapiUserDataEncryptionKeyProvider(keyPath);

            bool[] results = await Task.WhenAll(
                firstProvider.TrySaveKeyAsync(firstKey),
                secondProvider.TrySaveKeyAsync(secondKey));

            Assert.AreEqual(1, results.Count(static result => result));
            byte[]? loaded = await firstProvider.LoadKeyAsync();
            Assert.IsNotNull(loaded);
            try
            {
                byte[] expected = results[0] ? firstKey : secondKey;
                CollectionAssert.AreEqual(expected, loaded);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(loaded);
            }

            Assert.HasCount(0, Directory.GetFiles(directory, "*.tmp-*"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstKey);
            CryptographicOperations.ZeroMemory(secondKey);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task InvalidKeyLengthIsRejectedWithoutWritingAFile()
    {
        string directory = CreateTestDirectory();
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        try
        {
            var provider = new DpapiUserDataEncryptionKeyProvider(keyPath);
            await Assert.ThrowsAsync<ArgumentException>(
                () => provider.TrySaveKeyAsync(new byte[31]));

            Assert.IsFalse(File.Exists(keyPath));
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task CanceledSaveLeavesNoKeyOrTemporaryFile()
    {
        string directory = CreateTestDirectory();
        string keyPath = Path.Combine(directory, "user-data-key-v1.dpapi");
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var provider = new DpapiUserDataEncryptionKeyProvider(keyPath);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => provider.TrySaveKeyAsync(key, cancellation.Token));

            Assert.IsFalse(File.Exists(keyPath));
            Assert.HasCount(0, Directory.GetFiles(directory, "*.tmp-*"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task PostPublicationDurabilityFailureIsNotMisclassifiedAsContention()
    {
        string directory = CreateTestDirectory();
        string keyPath = Path.Combine(directory, "user-data-key.dpapi");
        byte[] key = RepeatedKey(0xA7);
        try
        {
            var provider = new DpapiUserDataEncryptionKeyProvider(
                keyPath,
                _ => throw new IOException("Synthetic post-publication flush failure."));

            IOException exception = await Assert.ThrowsAsync<IOException>(
                () => provider.TrySaveKeyAsync(key));

            StringAssert.Contains(exception.Message, "Synthetic post-publication");
            Assert.IsTrue(File.Exists(keyPath));
            CollectionAssert.AreEqual(key, await provider.LoadKeyAsync());
            Assert.HasCount(
                0,
                Directory.GetFiles(directory, "*.tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteTestDirectory(directory);
        }
    }

    private static byte[] RepeatedKey(byte value)
    {
        byte[] key = new byte[32];
        Array.Fill(key, value);
        return key;
    }

    private static string CreateTestDirectory() => Path.Combine(
        Path.GetTempPath(),
        "HorrorHarbor.Tests",
        Guid.NewGuid().ToString("N"));

    private static void DeleteTestDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
