using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Audit;

namespace RecipeWarden.Web.Pages.Admin;

/// <summary>FR-ADM-01, AC-74: the append-only audit log, newest first, with filters. Read-only (GET).</summary>
public sealed class AuditModel(AuditLog log) : PageModel
{
    public AuditPage Result { get; private set; } = new([], 1, false);

    public IReadOnlyList<string> EntityTypes { get; private set; } = [];

    public string? Action { get; private set; }

    public string? UserFilter { get; private set; }

    public string? Entity { get; private set; }

    public DateOnly? From { get; private set; }

    public DateOnly? To { get; private set; }

    public async Task OnGetAsync(string? action, string? user, string? entity, DateOnly? from, DateOnly? to, int page = 1)
    {
        Action = Trim(action, 64);
        UserFilter = Trim(user, 256);
        Entity = Trim(entity, 64);
        From = from;
        To = to;
        Result = await log.QueryAsync(new AuditQuery(Action, UserFilter, Entity, from, to, Math.Clamp(page, 1, 100_000)), HttpContext.RequestAborted);
        EntityTypes = await log.EntityTypesAsync(HttpContext.RequestAborted);
    }

    public string PageUrl(int page) =>
        "/admin/audit?" + string.Join('&', new[]
        {
            Action is null ? null : "action=" + Uri.EscapeDataString(Action),
            UserFilter is null ? null : "user=" + Uri.EscapeDataString(UserFilter),
            Entity is null ? null : "entity=" + Uri.EscapeDataString(Entity),
            From is null ? null : "from=" + From.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            To is null ? null : "to=" + To.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            "page=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }.Where(p => p is not null));

    private static string? Trim(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}
