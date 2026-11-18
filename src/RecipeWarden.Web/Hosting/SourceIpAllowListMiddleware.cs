using System.Net;

namespace RecipeWarden.Web.Hosting;

/// <summary>
/// SEC-60: refuses connections whose remote IP is not the tunnel host, loopback or an allowed address.
/// Runs first, on the connection IP, before any forwarded header is considered.
/// </summary>
internal sealed partial class SourceIpAllowListMiddleware(RequestDelegate next, IReadOnlyList<IPNetwork> allowed, ILogger<SourceIpAllowListMiddleware> logger)
{
    public Task InvokeAsync(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is not null)
        {
            var ip = NetworkOptions.Normalize(remote);
            if (allowed.Any(n => n.Contains(ip)))
            {
                return next(context);
            }
        }

        LogRefused(logger, remote?.ToString() ?? "(none)");
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused connection from {RemoteIp}: not in Network:AllowedSourceIps")]
    private static partial void LogRefused(ILogger logger, string remoteIp);
}
