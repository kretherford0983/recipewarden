using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>SEC-60, SEC-61 (app side of AC-93): connection allow-list and CF-Connecting-IP handling.</summary>
[Trait("Req", "SEC-60")]
[Trait("Req", "SEC-61")]
[Trait("Req", "AC-93")]
public sealed class SEC_60_NetworkTrustTests : IClassFixture<SEC_60_NetworkTrustTests.TunnelFactory>
{
    private readonly TunnelFactory factory;

    public SEC_60_NetworkTrustTests(TunnelFactory factory) => this.factory = factory;

    /// <summary>App bound to a LAN IP with cloudflared in its own container at 192.168.1.20.</summary>
    public sealed class TunnelFactory : IDisposable
    {
        public TunnelFactory()
        {
            Inner = new RecipeWardenFactory();
            Inner.Settings["Network:TrustedProxies:0"] = "192.168.1.20";
            Inner.Settings["Network:AllowedSourceIps:0"] = "10.0.0.0/24";
        }

        public RecipeWardenFactory Inner { get; }

        public void Dispose() => Inner.Dispose();
    }

    private async Task<HttpResponseMessage> GetFromAsync(string remoteIp, params (string Name, string Value)[] headers)
    {
        using var client = factory.Inner.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(RecipeWardenFactory.RemoteIpHeader, remoteIp);
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Tunnel_host_is_allowed() =>
        (await GetFromAsync("192.168.1.20")).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Allowed_lan_range_is_allowed() =>
        (await GetFromAsync("10.0.0.7")).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Loopback_is_always_allowed() =>
        (await GetFromAsync("127.0.0.1")).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Any_other_lan_address_is_refused() =>
        (await GetFromAsync("192.168.1.99")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Forwarded_headers_from_an_untrusted_but_allowed_source_are_ignored()
    {
        var response = await GetFromAsync("10.0.0.7", ("X-Forwarded-Proto", "https"));
        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Forwarded_proto_from_the_tunnel_host_is_honoured()
    {
        var response = await GetFromAsync("192.168.1.20", ("X-Forwarded-Proto", "https"), ("CF-Connecting-IP", "203.0.113.9"));
        response.Headers.Contains("Strict-Transport-Security").ShouldBeTrue();
    }

    [Fact]
    public async Task Spoofed_CF_Connecting_IP_cannot_bypass_the_allow_list() =>
        (await GetFromAsync("192.168.1.99", ("CF-Connecting-IP", "192.168.1.20"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
}
