using RecipeWarden.Infrastructure.Backup;

namespace RecipeWarden.Web.Operations;

/// <summary>
/// BR-63, W7: while a restore runs, every other request gets a 503 maintenance page (so no write can happen);
/// /health still answers so monitoring shows the process is alive. The request performing the restore started
/// before maintenance began and is not affected.
/// </summary>
internal sealed class MaintenanceMiddleware(RequestDelegate next, MaintenanceMode maintenance)
{
    public const string Page = """
        <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Maintenance · RecipeWarden</title><link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='-2.5 -2.8 29 29'%3E%3Crect x='-2.5' y='-2.8' width='29' height='29' rx='7' fill='%23a33f19'/%3E%3Cg fill='none' stroke='%23ffffff' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='M6 13.5h12v2.5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4z'/%3E%3Cpath d='M8.5 13.5V11M15.5 13.5V11'/%3E%3Cpath d='M7 11a3 3 0 0 1 1.5-5.6 3.5 3.5 0 0 1 7 0A3 3 0 0 1 17 11z'/%3E%3C/g%3E%3C/svg%3E"></head><body style="font-family:system-ui,sans-serif;max-width:32rem;margin:4rem auto;padding:0 1rem">
        <h1>Back in a moment</h1><p>RecipeWarden is restoring a backup. Try again in a minute or two.</p></body></html>
        """;

    public async Task InvokeAsync(HttpContext context)
    {
        if (maintenance.IsActive && !context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "60";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(Page, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
