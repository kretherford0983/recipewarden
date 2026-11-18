using System.Net;

namespace RecipeWarden.Web.Hosting;

/// <summary>SEC-60, SEC-61: network trust settings, written by the installer (--trusted-proxy, --allowed-ips).</summary>
public sealed class NetworkOptions
{
    public const string SectionName = "Network";

    /// <summary>The cloudflared host(s). Forwarded headers and CF-Connecting-IP are honoured only from these.</summary>
    public IList<string> TrustedProxies { get; } = [];

    /// <summary>Extra direct-access sources (IP or CIDR). Trusted proxies and loopback are always allowed.</summary>
    public IList<string> AllowedSourceIps { get; } = [];

    public IReadOnlyList<IPAddress> ParseTrustedProxies() =>
        TrustedProxies.Select(p => IPAddress.Parse(p.Trim())).Select(Normalize).ToList();

    public IReadOnlyList<IPNetwork> ParseAllowedNetworks()
    {
        var networks = new List<IPNetwork>();
        foreach (var entry in AllowedSourceIps.Concat(TrustedProxies))
        {
            var value = entry.Trim();
            if (value.Contains('/', StringComparison.Ordinal))
            {
                networks.Add(IPNetwork.Parse(value));
                continue;
            }

            var ip = Normalize(IPAddress.Parse(value));
            networks.Add(new IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
        }

        networks.Add(IPNetwork.Parse("127.0.0.0/8"));
        networks.Add(IPNetwork.Parse("::1/128"));
        return networks;
    }

    internal static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
