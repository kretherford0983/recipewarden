using System.Net;
using System.Net.Sockets;

namespace RecipeWarden.Core.Security;

/// <summary>The result of checking an import address: the parsed URI, or why it is refused.</summary>
public sealed record UrlCheck(Uri? Uri, string? Error)
{
    public bool IsAllowed => Uri is not null && Error is null;
}

/// <summary>
/// SEC-40, SEC-41, AC-13: which addresses URL import may reach. Only http/https on ports 80 and 443, no user names
/// or passwords in the address, and never an address on the local machine, the home network or a cloud metadata
/// service. The fetcher resolves DNS first and checks every address with <see cref="IsBlocked"/> before connecting,
/// and again on every redirect.
/// </summary>
public static class UrlRules
{
    public const int MaxUrlLength = 2048;
    public const int MaxRedirects = 3;

    public const string NotAnAddress = "Enter the full web address, starting with https://";
    public const string WrongScheme = "Only http:// and https:// addresses can be imported";
    public const string WrongPort = "Only addresses on the standard web ports (80 and 443) can be imported";
    public const string HasCredentials = "Addresses with a user name or password cannot be imported";
    public const string TooLong = "The address is too long";
    public const string PrivateAddress = "That address points to this server or your home network, so it cannot be imported";
    public const string TooManyRedirects = "The site redirected too many times";

    public static UrlCheck Check(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length > MaxUrlLength)
        {
            return new UrlCheck(null, TooLong);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return new UrlCheck(null, NotAnAddress);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return new UrlCheck(null, WrongScheme);
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return new UrlCheck(null, NotAnAddress);
        }

        return Check(uri);
    }

    /// <summary>Checks an absolute URI, e.g. the target of a redirect.</summary>
    public static UrlCheck Check(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return new UrlCheck(null, NotAnAddress);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return new UrlCheck(null, WrongScheme);
        }

        if (uri.Port is not (80 or 443))
        {
            return new UrlCheck(null, WrongPort);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return new UrlCheck(null, HasCredentials);
        }

        if (string.IsNullOrEmpty(uri.IdnHost))
        {
            return new UrlCheck(null, NotAnAddress);
        }

        // A literal IP in the address is checked here; host names are checked after DNS.
        if (IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var literal) && IsBlocked(literal))
        {
            return new UrlCheck(null, PrivateAddress);
        }

        if (uri.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return new UrlCheck(null, PrivateAddress);
        }

        return new UrlCheck(uri, null);
    }

    /// <summary>
    /// SEC-41: loopback, unspecified, private (RFC 1918), link-local (incl. 169.254.169.254), CGNAT, multicast,
    /// broadcast, reserved and documentation ranges, IPv6 ULA, link-local and multicast, and IPv4 addresses written
    /// as IPv6 (mapped, NAT64, 6to4) when the IPv4 address is blocked.
    /// </summary>
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsBlockedV4(address.GetAddressBytes());
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlockedV4(address.MapToIPv4().GetAddressBytes());
        }

        var b = address.GetAddressBytes();
        if (IPAddress.IPv6Loopback.Equals(address) || IPAddress.IPv6None.Equals(address) || IPAddress.IPv6Any.Equals(address))
        {
            return true;
        }

        // IPv4-compatible (::a.b.c.d, deprecated): first 12 bytes zero.
        if (b.Take(12).All(x => x == 0))
        {
            return true;
        }

        return (b[0] & 0xfe) == 0xfc // fc00::/7 unique local
            || (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) // fe80::/10 link-local
            || (b[0] == 0xfe && (b[1] & 0xc0) == 0xc0) // fec0::/10 site-local (deprecated)
            || b[0] == 0xff // ff00::/8 multicast
            || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) // 2001:db8::/32 documentation
            || (b[0] == 0x01 && b[1] == 0x00 && b.Skip(2).Take(6).All(x => x == 0)) // 100::/64 discard
            || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.Skip(4).Take(8).All(x => x == 0)
                && IsBlockedV4(b[12..16])) // 64:ff9b::/96 NAT64
            || (b[0] == 0x20 && b[1] == 0x02 && IsBlockedV4(b[2..6])); // 2002::/16 6to4
    }

    private static bool IsBlockedV4(byte[] b) =>
        b[0] == 0 // 0.0.0.0/8 "this network"
        || b[0] == 10 // 10/8 private
        || b[0] == 127 // loopback
        || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // 100.64/10 CGNAT
        || (b[0] == 169 && b[1] == 254) // link-local, incl. cloud metadata 169.254.169.254
        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) // 172.16/12 private
        || (b[0] == 192 && b[1] == 0 && b[2] == 0) // 192.0.0/24 IETF
        || (b[0] == 192 && b[1] == 0 && b[2] == 2) // TEST-NET-1
        || (b[0] == 192 && b[1] == 88 && b[2] == 99) // 6to4 relay anycast
        || (b[0] == 192 && b[1] == 168) // 192.168/16 private
        || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // benchmarking
        || (b[0] == 198 && b[1] == 51 && b[2] == 100) // TEST-NET-2
        || (b[0] == 203 && b[1] == 0 && b[2] == 113) // TEST-NET-3
        || b[0] >= 224; // multicast, reserved, broadcast
}
