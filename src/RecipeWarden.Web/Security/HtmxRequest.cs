namespace RecipeWarden.Web.Security;

internal static class HtmxRequest
{
    /// <summary>AC-70: htmx and API callers get status codes, not redirects.</summary>
    public static bool WantsStatusCode(HttpRequest request) =>
        request.Headers.ContainsKey("HX-Request") ||
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
}
