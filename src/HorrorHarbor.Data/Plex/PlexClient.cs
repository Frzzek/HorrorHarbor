using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Data.Plex;

public sealed class PlexClient : IPlexClient
{
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    private const int MaximumRetries = 3;
    private readonly HttpClient _httpClient;
    private readonly IPlexHostResolver _hostResolver;

    public PlexClient(HttpClient httpClient)
        : this(httpClient, new SystemPlexHostResolver())
    {
    }

    public PlexClient(HttpClient httpClient, IPlexHostResolver hostResolver)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _hostResolver = hostResolver ?? throw new ArgumentNullException(nameof(hostResolver));
    }

    public async Task<PlexServerIdentity> GetIdentityAsync(
        PlexConnectionSettings settings,
        string token,
        CancellationToken cancellationToken = default)
    {
        Uri baseUri = PlexUriValidator.Validate(settings);
        await PlexUriValidator.EnsureDestinationIsSafeAsync(
            baseUri, settings.AllowInsecureHttp, _hostResolver, cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage response = await SendAsync(
            new Uri(baseUri, "identity"), token, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        XmlDocument document = await ReadXmlAsync(response, cancellationToken).ConfigureAwait(false);
        XmlElement root = document.DocumentElement ?? throw new InvalidDataException("Plex identity response has no root element.");
        string machine = RequiredAttribute(root, "machineIdentifier");
        string name = OptionalAttribute(root, "friendlyName") ?? baseUri.Host;
        string version = OptionalAttribute(root, "version") ?? "unknown";
        return new PlexServerIdentity(machine, name, version);
    }

    public async Task<IReadOnlyList<PlexHistoryItem>> GetHistoryPageAsync(
        PlexConnectionSettings settings,
        string token,
        int start,
        int pageSize,
        DateTime? viewedAfterUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (pageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        Uri baseUri = PlexUriValidator.Validate(settings);
        await PlexUriValidator.EnsureDestinationIsSafeAsync(
            baseUri, settings.AllowInsecureHttp, _hostResolver, cancellationToken).ConfigureAwait(false);
        string relative = $"status/sessions/history/all?X-Plex-Container-Start={start.ToString(CultureInfo.InvariantCulture)}&X-Plex-Container-Size={pageSize.ToString(CultureInfo.InvariantCulture)}";
        if (viewedAfterUtc is not null)
        {
            long unixSeconds = new DateTimeOffset(viewedAfterUtc.Value.ToUniversalTime()).ToUnixTimeSeconds();
            relative += $"&viewedAt%3E%3D={unixSeconds.ToString(CultureInfo.InvariantCulture)}";
        }

        using HttpResponseMessage response = await SendAsync(
            new Uri(baseUri, relative), token, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        XmlDocument document = await ReadXmlAsync(response, cancellationToken).ConfigureAwait(false);
        XmlElement root = document.DocumentElement ?? throw new InvalidDataException("Plex history response has no root element.");
        var results = new List<PlexHistoryItem>();
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node is not XmlElement element || element.Name is not ("Video" or "Directory"))
            {
                continue;
            }

            string? ratingKey = OptionalAttribute(element, "ratingKey");
            string? title = OptionalAttribute(element, "title");
            string? viewedAt = OptionalAttribute(element, "viewedAt");
            if (string.IsNullOrWhiteSpace(ratingKey) || string.IsNullOrWhiteSpace(title) ||
                !long.TryParse(viewedAt, NumberStyles.None, CultureInfo.InvariantCulture, out long viewedAtSeconds))
            {
                continue;
            }

            results.Add(new PlexHistoryItem(
                ratingKey,
                OptionalAttribute(element, "type") ?? "other",
                title,
                ParseNullableInt(OptionalAttribute(element, "year")),
                OptionalAttribute(element, "guid"),
                DateTimeOffset.FromUnixTimeSeconds(viewedAtSeconds).UtcDateTime,
                ParseNullableLong(OptionalAttribute(element, "viewOffset")),
                ParseNullableLong(OptionalAttribute(element, "duration"))));
        }

        return results.AsReadOnly();
    }

    private static HttpRequestMessage CreateRequest(Uri uri, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Length > 1024 || token.Any(char.IsControl))
        {
            throw new ArgumentException("Plex token has an invalid format.", nameof(token));
        }

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Plex-Token", token);
        request.Headers.Add("X-Plex-Product", "HorrorHarbor");
        request.Headers.Add("X-Plex-Version", "2.0");
        request.Headers.Add("X-Plex-Client-Identifier", "HorrorHarbor-Windows");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        string token,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using HttpRequestMessage request = CreateRequest(uri, token);
            HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!IsRetryable(response.StatusCode) || attempt >= MaximumRetries)
            {
                return response;
            }

            TimeSpan delay = GetRetryDelay(response, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is not null)
        {
            return retryAfter.Value > TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : retryAfter.Value;
        }

        return attempt switch
        {
            0 => TimeSpan.FromMilliseconds(250),
            1 => TimeSpan.FromSeconds(1),
            _ => TimeSpan.FromSeconds(3),
        };
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Plex rejected the supplied token.",
            HttpStatusCode.NotFound => "The Plex API endpoint was not found on this server.",
            HttpStatusCode.TooManyRequests => "Plex temporarily rate-limited the request.",
            _ => $"Plex returned HTTP {(int)response.StatusCode}.",
        };
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<XmlDocument> ReadXmlAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException("Plex response exceeds the supported size limit.");
        }

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var boundedStream = new LengthLimitedStream(responseStream, MaximumResponseBytes);
        using XmlReader reader = XmlReader.Create(boundedStream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumResponseBytes,
        });
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        return document;
    }

    private static string RequiredAttribute(XmlElement element, string name) =>
        OptionalAttribute(element, name) ?? throw new InvalidDataException($"Plex response is missing '{name}'.");

    private static string? OptionalAttribute(XmlElement element, string name) =>
        element.HasAttribute(name) ? element.GetAttribute(name) : null;

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

    private static long? ParseNullableLong(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;

    private sealed class LengthLimitedStream(Stream inner, long maximumLength) : Stream
    {
        private long _bytesRead;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _bytesRead; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            Count(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Count(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        private void Count(int count)
        {
            _bytesRead += count;
            if (_bytesRead > maximumLength)
            {
                throw new InvalidDataException("Plex response exceeds the supported size limit.");
            }
        }
    }
}
