using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace RecipeWarden.Web.Security;

/// <summary>SEC-63 limits; configurable (section 7.2 lists rate limits under /admin/settings).</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Search and browse requests per IP per minute.</summary>
    public int SearchPerMinute { get; set; } = 60;

    /// <summary>Unsafe requests (POST, PUT, PATCH, DELETE) per signed-in user per minute.</summary>
    public int WritesPerMinute { get; set; } = 120;
}

/// <summary>SEC-63: search 60/min per IP; writes 120/min per user. Over the limit: 429 with Retry-After.</summary>
internal static class RateLimits
{
    public const string SearchPolicy = "search";

    public static IServiceCollection AddRecipeWardenRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new RateLimitOptions();
        configuration.GetSection(RateLimitOptions.SectionName).Bind(options);

        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };

            // Client IP is already the real one here (CloudflareClientIpMiddleware runs first, SEC-61).
            o.AddPolicy(SearchPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => Window(options.SearchPerMinute)));

            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var user = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
                return IsWrite(ctx.Request.Method) && user is not null
                    ? RateLimitPartition.GetFixedWindowLimiter("user:" + user, _ => Window(options.WritesPerMinute))
                    : RateLimitPartition.GetNoLimiter(string.Empty);
            });
        });
    }

    private static bool IsWrite(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static FixedWindowRateLimiterOptions Window(int perMinute) => new()
    {
        PermitLimit = Math.Max(1, perMinute),
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
