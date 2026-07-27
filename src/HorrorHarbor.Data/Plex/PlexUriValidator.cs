using System.Net;
using System.Net.Sockets;
using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Data.Plex;

public static class PlexUriValidator
{
    public static Uri Validate(PlexConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ServerUrl);
        if (!Uri.TryCreate(settings.ServerUrl.Trim(), UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Query.Length > 0 ||
            uri.Fragment.Length > 0)
        {
            throw new ArgumentException("Plex server URL must be an absolute URL without credentials, query, or fragment.", nameof(settings));
        }

        bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        bool isAllowedHttp = settings.AllowInsecureHttp &&
                             string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isAllowedHttp)
        {
            throw new ArgumentException("HTTPS is required unless trusted-network HTTP is explicitly enabled.", nameof(settings));
        }

        if (isAllowedHttp && !IPAddress.TryParse(uri.DnsSafeHost, out _))
        {
            throw new ArgumentException(
                "Plain HTTP requires a private, link-local, or loopback IP address literal. " +
                "Hostnames are blocked to prevent DNS-rebinding attacks.",
                nameof(settings));
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/", UriKind.Absolute);
    }

    public static async Task EnsureDestinationIsSafeAsync(
        Uri uri,
        bool allowInsecureHttp,
        IPlexHostResolver resolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(resolver);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!allowInsecureHttp)
        {
            throw new InvalidOperationException("Plain HTTP is not enabled for this Plex connection.");
        }

        if (!IPAddress.TryParse(uri.DnsSafeHost, out IPAddress? literal))
        {
            throw new InvalidOperationException(
                "Plain HTTP Plex connections require an IP address literal.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        IPAddress[] addresses = [literal];
        if (addresses.Length == 0 || addresses.Any(static address => !IsPrivateAddress(address)))
        {
            throw new InvalidOperationException(
                "Plain HTTP Plex connections are restricted to private, link-local, or loopback destinations.");
        }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        byte first = address.GetAddressBytes()[0];
        return (first & 0xFE) == 0xFC;
    }
}
