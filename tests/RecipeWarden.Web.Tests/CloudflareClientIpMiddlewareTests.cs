using System.Net;
using Microsoft.AspNetCore.Http;
using RecipeWarden.Web.Hosting;

namespace RecipeWarden.Web.Tests;

[Trait("Req", "SEC-61")]
public sealed class CloudflareClientIpMiddlewareTests
{
    private static async Task<IPAddress?> RunAsync(string remote, string? cfHeader)
    {
        IPAddress? seen = null;
        var middleware = new CloudflareClientIpMiddleware(ctx =>
        {
            seen = ctx.Connection.RemoteIpAddress;
            ctx.Request.Headers.ContainsKey(CloudflareClientIpMiddleware.HeaderName).ShouldBeFalse();
            return Task.CompletedTask;
        }, [IPAddress.Parse("192.168.1.20")]);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (cfHeader is not null)
        {
            context.Request.Headers[CloudflareClientIpMiddleware.HeaderName] = cfHeader;
        }

        await middleware.InvokeAsync(context);
        return seen;
    }

    [Fact]
    public async Task Client_ip_comes_from_the_header_when_the_connection_is_the_trusted_proxy() =>
        (await RunAsync("192.168.1.20", "203.0.113.9")).ShouldBe(IPAddress.Parse("203.0.113.9"));

    [Fact]
    public async Task IPv4_mapped_proxy_address_is_recognised() =>
        (await RunAsync("::ffff:192.168.1.20", "203.0.113.9")).ShouldBe(IPAddress.Parse("203.0.113.9"));

    [Fact]
    public async Task Header_is_ignored_from_anyone_else() =>
        (await RunAsync("192.168.1.99", "203.0.113.9")).ShouldBe(IPAddress.Parse("192.168.1.99"));

    [Fact]
    public async Task Garbage_header_is_ignored() =>
        (await RunAsync("192.168.1.20", "not-an-ip")).ShouldBe(IPAddress.Parse("192.168.1.20"));
}
