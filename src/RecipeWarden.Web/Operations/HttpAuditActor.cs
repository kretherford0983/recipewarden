using System.Security.Claims;
using RecipeWarden.Infrastructure.Audit;

namespace RecipeWarden.Web.Operations;

/// <summary>
/// FR-ADM-01, ADR 0041: the actor of a write made during a request — the signed-in user, or "anonymous" for the one
/// anonymous write (shopping check-off) — with the client IP (already taken from CF-Connecting-IP when the request came
/// through the tunnel, SEC-61). Outside a request (background services) it is "system".
/// </summary>
internal sealed class HttpAuditActor(IHttpContextAccessor accessor) : IAuditActor
{
    private static readonly SystemAuditActor System = new();

    public AuditActorInfo Current
    {
        get
        {
            if (AuditScope.Actor is { } explicitActor)
            {
                return explicitActor;
            }

            var ctx = accessor.HttpContext;
            if (ctx is null)
            {
                return System.Current;
            }

            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            return ctx.User.Identity?.IsAuthenticated == true
                ? new AuditActorInfo(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), ctx.User.Identity.Name ?? "unknown", ip)
                : new AuditActorInfo(null, "anonymous", ip);
        }
    }
}
