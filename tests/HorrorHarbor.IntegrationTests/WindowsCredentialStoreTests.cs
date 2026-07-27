using System.Runtime.Versioning;
using HorrorHarbor.App.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class WindowsCredentialStoreTests
{
    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task CredentialManagerRoundTripsAndDeletesToken()
    {
        string connectionId = "test-" + Guid.NewGuid().ToString("N");
        const string token = "credential-canary-value";
        var store = new WindowsCredentialPlexCredentialStore();
        try
        {
            await store.SaveTokenAsync(connectionId, token);

            Assert.AreEqual(token, await store.LoadTokenAsync(connectionId));
            await store.DeleteTokenAsync(connectionId);
            Assert.IsNull(await store.LoadTokenAsync(connectionId));
        }
        finally
        {
            await store.DeleteTokenAsync(connectionId);
        }
    }
}
