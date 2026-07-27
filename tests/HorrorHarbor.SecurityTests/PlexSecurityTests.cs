using System.Net;
using System.Xml;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Data.Plex;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.SecurityTests;

[TestClass]
public sealed class PlexSecurityTests
{
    [TestMethod]
    public async Task TokenIsSentInHeaderAndNotRequestUri()
    {
        const string token = "secret-token-value";
        var handler = new CapturingHandler("<MediaContainer machineIdentifier=\"machine\" friendlyName=\"Test Plex\" version=\"1.0\" />");
        using var httpClient = new HttpClient(handler);
        var client = new PlexClient(httpClient);

        PlexServerIdentity identity = await client.GetIdentityAsync(
            new PlexConnectionSettings("https://plex.example:32400"),
            token);

        Assert.AreEqual("machine", identity.MachineIdentifier);
        Assert.IsNotNull(handler.RequestUri);
        Assert.DoesNotContain(token, handler.RequestUri.OriginalString, StringComparison.Ordinal);
        Assert.AreEqual(token, handler.PlexToken);
    }

    [TestMethod]
    public async Task HistoryParserRejectsDocumentTypeDeclarations()
    {
        const string xml = "<!DOCTYPE MediaContainer [<!ENTITY xxe SYSTEM 'file:///c:/windows/win.ini'>]><MediaContainer><Video ratingKey='1' title='&xxe;' viewedAt='1' /></MediaContainer>";
        using var httpClient = new HttpClient(new CapturingHandler(xml));
        var client = new PlexClient(httpClient);

        await Assert.ThrowsAsync<XmlException>(() => client.GetHistoryPageAsync(
            new PlexConnectionSettings("https://plex.example:32400"),
            "token",
            0,
            50,
            null));
    }

    [TestMethod]
    public async Task PlainHttpRequiresExplicitOptIn()
    {
        using var httpClient = new HttpClient(new CapturingHandler("<MediaContainer />"));
        var client = new PlexClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetIdentityAsync(
            new PlexConnectionSettings("http://192.168.1.10:32400"),
            "token"));
    }

    [TestMethod]
    public async Task PlainHttpOptInRejectsHostnamesToPreventDnsRebinding()
    {
        using var httpClient = new HttpClient(new CapturingHandler("<MediaContainer machineIdentifier='machine' />"));
        var client = new PlexClient(httpClient, new FixedHostResolver(IPAddress.Parse("192.168.1.20")));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetIdentityAsync(
            new PlexConnectionSettings("http://plex.lan:32400", AllowInsecureHttp: true),
            "token"));
    }

    [TestMethod]
    public async Task PlainHttpOptInAllowsOnlyPrivateDestination()
    {
        using var httpClient = new HttpClient(new CapturingHandler("<MediaContainer machineIdentifier='machine' />"));
        var resolver = new FixedHostResolver(IPAddress.Parse("203.0.113.10"));
        var client = new PlexClient(httpClient, resolver);

        PlexServerIdentity identity = await client.GetIdentityAsync(
            new PlexConnectionSettings("http://192.168.1.20:32400", AllowInsecureHttp: true),
            "token");

        Assert.AreEqual("machine", identity.MachineIdentifier);
        Assert.AreEqual(0, resolver.CallCount);
    }

    [TestMethod]
    public async Task PlainHttpOptInRejectsPublicIpLiteral()
    {
        using var httpClient = new HttpClient(new CapturingHandler("<MediaContainer machineIdentifier='machine' />"));
        var client = new PlexClient(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetIdentityAsync(
            new PlexConnectionSettings("http://203.0.113.10:32400", AllowInsecureHttp: true),
            "token"));
    }

    private sealed class CapturingHandler(string responseXml) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? PlexToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            PlexToken = request.Headers.TryGetValues("X-Plex-Token", out IEnumerable<string>? values)
                ? values.Single()
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml),
            });
        }
    }

    private sealed class FixedHostResolver(params IPAddress[] addresses) : IPlexHostResolver
    {
        public int CallCount { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.IsFalse(string.IsNullOrWhiteSpace(host));
            CallCount++;
            return Task.FromResult(addresses);
        }
    }
}
