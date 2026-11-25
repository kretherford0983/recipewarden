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

    /// <summary>SEC-43: URL imports per user per hour.</summary>
    public int UrlImportsPerHour { get; set; } = 20;

    /// <summary>SEC-63: sign-in attempts (password and two-factor) per IP per minute.</summary>
    public int SignInsPerMinute { get; set; } = Core.Security.AccountRules.SignInsPerMinute;

    /// <summary>ADR 0028: shopping-list ticks per IP per minute (the one anonymous write).</summary>
    public int ShoppingChecksPerMinute { get; set; } = 120;
}

/// <summary>
/// SEC-63: search 60/min per IP; writes 120/min per user; shopping ticks 120/min per IP (ADR 0028). SEC-43: URL
/// imports 20 an hour per user. Over the limit:
/// 429 with Retry-After.
/// </summary>
internal static class RateLimits
{
    public const string SearchPolicy = "search";
    public const string UrlImportPolicy = "url-import";
    public const string ShoppingCheckPolicy = "shopping-check";
    public const string SignInPolicy = "sign-in";

    /// <summary>The list page handler that ticks an item (ADR 0028); other handlers on that page are not counted here.</summary>
    public const string ShoppingCheckHandler = "Check";

    /// <summary>The import page handler that fetches a URL (SEC-43); other handlers on that page are not counted.</summary>
    public const string UrlImportHandler = "Url";

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

            // SEC-43: only the POST that fetches a page counts; opening the import page does not.
            o.AddPolicy(UrlImportPolicy, ctx =>
                HttpMethods.IsPost(ctx.Request.Method)
                    && string.Equals(ctx.Request.Query["handler"], UrlImportHandler, StringComparison.OrdinalIgnoreCase)
                    && ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id
                    ? RateLimitPartition.GetFixedWindowLimiter("import:" + id, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, options.UrlImportsPerHour),
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    })
                    : RateLimitPartition.GetNoLimiter(string.Empty));

            // ADR 0028: ticking an item is open to anonymous readers, so it is limited per IP.
            o.AddPolicy(ShoppingCheckPolicy, ctx =>
                HttpMethods.IsPost(ctx.Request.Method)
                    && string.Equals(ctx.Request.Query["handler"], ShoppingCheckHandler, StringComparison.OrdinalIgnoreCase)
                    ? RateLimitPartition.GetFixedWindowLimiter("check:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                        _ => Window(options.ShoppingChecksPerMinute))
                    : RateLimitPartition.GetNoLimiter(string.Empty));

            // SEC-63: only the POST that tries a password or a code counts; showing the form does not.
            o.AddPolicy(SignInPolicy, ctx =>
                HttpMethods.IsPost(ctx.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter("signin:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"), _ => Window(options.SignInsPerMinute))
                    : RateLimitPartition.GetNoLimiter(string.Empty));

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
