using System.Net;
using System.Net.Http.Headers;
using HorrorHarbor.Posters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class TmdbPosterServiceTests
{
    private static readonly PosterDescriptor Descriptor = new(
        "the-collector-2009",
        "The Collector",
        2009,
        "Slasher / Killer",
        "#7F1D1D",
        ["traps", "home invasion", "killer"],
        OriginalLanguage: "en");

    [TestMethod]
    public void Matcher_SelectsExactAndAdjacentYearMatchesAndRejectsAmbiguity()
    {
        var exact = new TmdbMovieResult(1, "The Collector", "The Collector", "en", "2009-07-31", "/exact.jpg");
        var wrong = new TmdbMovieResult(2, "The Collector", "The Collector", "en", "1965-06-17", "/wrong.jpg");
        TmdbMatch? exactMatch = TmdbIdentityMatcher.Select(Descriptor, [wrong, exact]);

        Assert.IsNotNull(exactMatch);
        Assert.AreEqual(1, exactMatch.Movie.Id);
        Assert.AreEqual("title-exact+year-exact+language-exact", exactMatch.Method);

        var adjacent = exact with { Id = 3, ReleaseDate = "2010-01-01", PosterPath = "/adjacent.jpg" };
        TmdbMatch? adjacentMatch = TmdbIdentityMatcher.Select(Descriptor, [adjacent]);
        Assert.IsNotNull(adjacentMatch);
        Assert.AreEqual(3, adjacentMatch.Movie.Id);
        Assert.IsGreaterThanOrEqualTo(0.85, adjacentMatch.Confidence);

        var duplicate = exact with { Id = 4, PosterPath = "/duplicate.jpg" };
        Assert.IsNull(TmdbIdentityMatcher.Select(Descriptor, [exact, duplicate]));
    }

    [TestMethod]
    public async Task Provider_UsesExplicitIdentityProtectsTokenAndReusesValidatedCache()
    {
        string root = CreateTemporaryRoot();
        byte[] poster = ProceduralPosterRenderer.Render(Descriptor, 240, 360);
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.Host == "api.themoviedb.org")
            {
                Assert.AreEqual("/3/movie/5176", request.RequestUri.AbsolutePath);
                Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
                Assert.AreEqual("test-read-token", request.Headers.Authorization?.Parameter);
                return JsonResponse("""
                    {
                      "id": 5176,
                      "title": "The Collector",
                      "original_title": "The Collector",
                      "original_language": "en",
                      "release_date": "2009-07-31",
                      "poster_path": "/collector.jpg"
                    }
                    """);
            }

            Assert.AreEqual("image.tmdb.org", request.RequestUri.Host);
            Assert.IsNull(request.Headers.Authorization);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(poster),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        });

        try
        {
            using var client = new HttpClient(handler);
            using var service = new TmdbPosterService(
                client,
                new FixedCredentialStore("test-read-token"),
                CreateOptions(root));
            PosterDescriptor identified = Descriptor with { TmdbId = 5176 };

            PosterAsset first = await service.GetPosterAsync(identified);
            PosterAsset cached = await service.GetPosterAsync(identified);

            Assert.AreEqual(PosterSourceKind.TmdbOfficial, first.SourceKind);
            Assert.AreEqual("5176", first.Provenance?.ProviderMediaId);
            Assert.AreEqual("catalog-tmdb-id", first.Provenance?.MatchMethod);
            Assert.AreEqual(240, first.PixelWidth);
            Assert.AreEqual(360, first.PixelHeight);
            Assert.AreEqual(first.FilePath, cached.FilePath);
            Assert.AreEqual(2, handler.RequestCount);
            Assert.IsTrue(File.Exists(first.FilePath));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [TestMethod]
    public async Task Provider_DeduplicatesConcurrentSearchAndDownload()
    {
        string root = CreateTemporaryRoot();
        byte[] poster = ProceduralPosterRenderer.Render(Descriptor, 240, 360);
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "api.themoviedb.org"
            ? JsonResponse("""
                {
                  "results": [{
                    "id": 5176,
                    "title": "The Collector",
                    "original_title": "The Collector",
                    "original_language": "en",
                    "release_date": "2009-07-31",
                    "poster_path": "/collector.jpg"
                  }]
                }
                """)
            : ImageResponse(poster));

        try
        {
            using var client = new HttpClient(handler);
            using var service = new TmdbPosterService(
                client,
                new FixedCredentialStore("test-read-token"),
                CreateOptions(root));

            PosterAsset[] results = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => service.GetPosterAsync(Descriptor)));

            Assert.AreEqual(2, handler.RequestCount);
            Assert.AreEqual(1, results.Select(static result => result.FilePath).Distinct().Count());
            Assert.IsTrue(results.All(static result => result.SourceKind == PosterSourceKind.TmdbOfficial));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [TestMethod]
    public async Task Hybrid_UsesGeneratedFallbackWhenCredentialIsMissing()
    {
        string officialRoot = CreateTemporaryRoot();
        string fallbackRoot = CreateTemporaryRoot();
        var handler = new RecordingHandler(_ => throw new AssertFailedException("Network must not be used without a credential."));
        try
        {
            using var client = new HttpClient(handler);
            using var official = new TmdbPosterService(client, new FixedCredentialStore(null), CreateOptions(officialRoot));
            using var fallback = new LocalPosterService(new PosterCacheOptions(fallbackRoot, 240, 360, 2_000_000));
            var hybrid = new HybridPosterService(official, fallback);

            PosterAsset result = await hybrid.GetPosterAsync(Descriptor);

            Assert.AreEqual(PosterSourceKind.GeneratedFallback, result.SourceKind);
            Assert.AreEqual(0, handler.RequestCount);
            Assert.AreEqual("Nocturne Harbor renderer", result.Provenance?.Provider);
        }
        finally
        {
            DeleteTemporaryRoot(officialRoot);
            DeleteTemporaryRoot(fallbackRoot);
        }
    }

    [TestMethod]
    public async Task Provider_RejectsRedirectOversizedPayloadWrongMediaTypeAndCorruptImage()
    {
        await AssertProviderFailureAsync(
            _ => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.invalid/redirect") },
            },
            typeof(HttpRequestException));

        await AssertProviderFailureAsync(
            _ => JsonBytesResponse(new byte[128_001]),
            typeof(InvalidDataException));

        await AssertProviderFailureAsync(
            _ => ImageResponse([0xff, 0xd8, 0xff, 0xd9]),
            typeof(InvalidDataException),
            validApiResponse: true);

        await AssertProviderFailureAsync(
            _ => ImageResponse([0xff, 0xd8, 0x00, 0x00, 0xff, 0xd9]),
            typeof(InvalidDataException),
            validApiResponse: true);

        await AssertProviderFailureAsync(
            _ => ImageResponse(ProceduralPosterRenderer.Render(Descriptor, 240, 360)),
            typeof(InvalidDataException),
            overrideMediaType: "text/html");
    }

    [TestMethod]
    public async Task Provider_RepairsCorruptCacheByRefetchingOfficialArtwork()
    {
        string root = CreateTemporaryRoot();
        byte[] poster = ProceduralPosterRenderer.Render(Descriptor, 240, 360);
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "api.themoviedb.org"
            ? DirectMovieResponse()
            : ImageResponse(poster));

        try
        {
            using var client = new HttpClient(handler);
            using var service = new TmdbPosterService(
                client,
                new FixedCredentialStore("test-read-token"),
                CreateOptions(root));
            PosterDescriptor identified = Descriptor with { TmdbId = 5176 };

            PosterAsset first = await service.GetPosterAsync(identified);
            await File.WriteAllTextAsync(first.FilePath, "corrupt cache entry");
            PosterAsset repaired = await service.GetPosterAsync(identified);

            Assert.AreEqual(4, handler.RequestCount);
            Assert.AreEqual(PosterSourceKind.TmdbOfficial, repaired.SourceKind);
            Assert.AreEqual(first.FilePath, repaired.FilePath);
            Assert.AreEqual(240, repaired.PixelWidth);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [TestMethod]
    public async Task Hybrid_FallsBackForNetworkFailureAndTimeoutButPreservesCallerCancellation()
    {
        await AssertHybridFallbackAsync(new HttpRequestException("simulated network failure"));
        await AssertHybridFallbackAsync(new TaskCanceledException("simulated provider timeout"));

        string officialRoot = CreateTemporaryRoot();
        string fallbackRoot = CreateTemporaryRoot();
        try
        {
            using var client = new HttpClient(new RecordingHandler(_ => throw new TaskCanceledException()));
            using var official = new TmdbPosterService(
                client,
                new FixedCredentialStore("test-read-token"),
                CreateOptions(officialRoot));
            using var fallback = new LocalPosterService(new PosterCacheOptions(fallbackRoot, 240, 360, 2_000_000));
            var hybrid = new HybridPosterService(official, fallback);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => hybrid.GetPosterAsync(Descriptor, cancellation.Token));
        }
        finally
        {
            DeleteTemporaryRoot(officialRoot);
            DeleteTemporaryRoot(fallbackRoot);
        }
    }

    [TestMethod]
    public async Task Provider_RejectsNullResultsWithoutLeavingCacheArtifacts()
    {
        string root = CreateTemporaryRoot();
        var handler = new RecordingHandler(_ => JsonResponse("""{"results":[null]}"""));
        try
        {
            using var client = new HttpClient(handler);
            using var service = new TmdbPosterService(
                client,
                new FixedCredentialStore("test-read-token"),
                CreateOptions(root));

            await Assert.ThrowsAsync<InvalidDataException>(() => service.GetPosterAsync(Descriptor));

            Assert.AreEqual(1, handler.RequestCount);
            Assert.AreEqual(0, Directory.EnumerateFiles(root).Count());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static TmdbPosterOptions CreateOptions(string root) => new(
        root,
        MaximumSearchBytes: 128_000,
        MaximumImageBytes: 2_000_000,
        MaximumPixelWidth: 240,
        MaximumPixelHeight: 360,
        CacheDays: 30);

    private static HttpResponseMessage JsonResponse(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    private static HttpResponseMessage ImageResponse(byte[] encoded)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(encoded),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }

    private static HttpResponseMessage DirectMovieResponse() => JsonResponse("""
        {
          "id": 5176,
          "title": "The Collector",
          "original_title": "The Collector",
          "original_language": "en",
          "release_date": "2009-07-31",
          "poster_path": "/collector.jpg"
        }
        """);

    private static HttpResponseMessage JsonBytesResponse(byte[] content)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    private static async Task AssertProviderFailureAsync(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        Type exceptionType,
        bool validApiResponse = false,
        string? overrideMediaType = null)
    {
        string root = CreateTemporaryRoot();
        var handler = new RecordingHandler(request =>
        {
            HttpResponseMessage response = validApiResponse && request.RequestUri!.Host == "api.themoviedb.org"
                ? DirectMovieResponse()
                : responseFactory(request);
            if (overrideMediaType is not null)
            {
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(overrideMediaType);
            }

            return response;
        });
        try
        {
            using var client = new HttpClient(handler);
            using var service = new TmdbPosterService(
                client,
                new FixedCredentialStore("test-read-token"),
                CreateOptions(root));
            PosterDescriptor descriptor = validApiResponse ? Descriptor with { TmdbId = 5176 } : Descriptor;

            Exception exception = await Assert.ThrowsAsync<Exception>(() => service.GetPosterAsync(descriptor));
            Assert.AreEqual(exceptionType, exception.GetType());
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static async Task AssertHybridFallbackAsync(Exception providerException)
    {
        string officialRoot = CreateTemporaryRoot();
        string fallbackRoot = CreateTemporaryRoot();
        try
        {
            using var client = new HttpClient(new RecordingHandler(_ => throw providerException));
            using var official = new TmdbPosterService(
                client,
                new FixedCredentialStore("test-read-token"),
                CreateOptions(officialRoot));
            using var fallback = new LocalPosterService(new PosterCacheOptions(fallbackRoot, 240, 360, 2_000_000));
            var hybrid = new HybridPosterService(official, fallback);

            PosterAsset result = await hybrid.GetPosterAsync(Descriptor);

            Assert.AreEqual(PosterSourceKind.GeneratedFallback, result.SourceKind);
        }
        finally
        {
            DeleteTemporaryRoot(officialRoot);
            DeleteTemporaryRoot(fallbackRoot);
        }
    }

    private static string CreateTemporaryRoot() =>
        Path.Combine(Path.GetTempPath(), "NocturneHarborTmdbTests", Guid.NewGuid().ToString("N"));

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixedCredentialStore(string? token) : IPosterCredentialStore
    {
        public Task SaveTmdbTokenAsync(string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string?> LoadTmdbTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(token);

        public Task DeleteTmdbTokenAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(responder(request));
        }
    }
}
