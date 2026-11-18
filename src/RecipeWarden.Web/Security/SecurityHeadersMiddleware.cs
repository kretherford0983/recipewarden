namespace RecipeWarden.Web.Security;

/// <summary>SEC-23, SEC-62: security headers on every response.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// SEC-23. No inline script exists anywhere (UX-03), so no nonce is needed (see ADR 0002).
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public const string StrictTransportSecurity = "max-age=31536000";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h.ContentSecurityPolicy = ContentSecurityPolicy;
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h.XFrameOptions = "DENY";

            // HSTS only means something over HTTPS; behind the tunnel IsHttps comes from the trusted X-Forwarded-Proto.
            if (context.Request.IsHttps)
            {
                h.StrictTransportSecurity = StrictTransportSecurity;
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
