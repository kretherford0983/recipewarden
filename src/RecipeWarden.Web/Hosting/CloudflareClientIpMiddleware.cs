using System.Net;

namespace RecipeWarden.Web.Hosting;

/// <summary>
/// SEC-61: when (and only when) the connection came from a trusted proxy, the client IP is taken from
/// CF-Connecting-IP. Runs after the allow-list and UseForwardedHeaders, which must see the proxy's IP.
/// </summary>
internal sealed class CloudflareClientIpMiddleware(RequestDelegate next, IReadOnlyList<IPAddress> trustedProxies)
{
    public const string HeaderName = "CF-Connecting-IP";

    public Task InvokeAsync(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        var fromProxy = remote is not null && trustedProxies.Contains(NetworkOptions.Normalize(remote));

        if (fromProxy &&
            context.Request.Headers.TryGetValue(HeaderName, out var value) &&
            IPAddress.TryParse(value.ToString().Trim(), out var client))
        {
            context.Connection.RemoteIpAddress = client;
        }

        // Never let the header reach the app from anywhere else.
        context.Request.Headers.Remove(HeaderName);
        return next(context);
    }
}
