using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Audit;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Audit;

/// <summary>Filters for /admin/audit.</summary>
public sealed record AuditQuery(string? Action = null, string? User = null, string? Entity = null, DateOnly? From = null, DateOnly? To = null, int Page = 1);

public sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, int Page, bool HasMore);

/// <summary>
/// FR-ADM-01, AC-74: explicit audit events that are not a single entity write (sign-in, backup run, restore,
/// settings, CLI commands), and the read side for /admin/audit. Details must never hold secrets (SEC-72): callers pass
/// names and outcomes only.
/// </summary>
public sealed class AuditLog(AppDbContext db, IAuditActor actor, TimeProvider clock)
{
    public Task RecordAsync(string action, string? entityType = null, string? entityId = null, object? details = null,
        CancellationToken cancellationToken = default) =>
        RecordAsAsync(actor.Current, action, entityType, entityId, details, cancellationToken);

    /// <summary>Records an event for a given actor (e.g. a failed sign-in, where nobody is signed in yet).</summary>
    public async Task RecordAsAsync(AuditActorInfo who, string action, string? entityType = null, string? entityId = null, object? details = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(who);
        db.AuditEntries.Add(new AuditEntry
        {
            At = clock.GetUtcNow(),
            UserId = who.UserId,
            UserName = AuditRules.Truncate(who.UserName, 256),
            Ip = who.Ip,
            Action = AuditRules.Truncate(action, AuditRules.MaxActionLength),
            EntityType = entityType is null ? null : AuditRules.Truncate(entityType, 64),
            EntityId = entityId is null ? null : AuditRules.Truncate(entityId, AuditRules.MaxEntityIdLength),
            DetailsJson = details is null ? null : AuditRules.Truncate(JsonSerializer.Serialize(details), AuditRules.MaxDetailsLength),
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var a = query.Action.Trim();
            q = q.Where(e => e.Action.StartsWith(a));
        }

        if (!string.IsNullOrWhiteSpace(query.User))
        {
            var u = query.User.Trim();
            q = q.Where(e => e.UserName.Contains(u));
        }

        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            var t = query.Entity.Trim();
            q = q.Where(e => e.EntityType == t);
        }

        // At is ISO-8601 text in UTC (8.1), so comparisons on the converted value order correctly.
        if (query.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(e => e.At >= start);
        }

        if (query.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(e => e.At < end);
        }

        var page = Math.Max(1, query.Page);
        var rows = await q.OrderByDescending(e => e.Id)
            .Skip((page - 1) * AuditRules.PageSize)
            .Take(AuditRules.PageSize + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new AuditPage(rows.Take(AuditRules.PageSize).ToList(), page, rows.Count > AuditRules.PageSize);
    }

    public async Task<IReadOnlyList<AuditEntry>> RecentAsync(int count, CancellationToken cancellationToken = default) =>
        await db.AuditEntries.AsNoTracking().OrderByDescending(e => e.Id).Take(count).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> EntityTypesAsync(CancellationToken cancellationToken = default) =>
        await db.AuditEntries.AsNoTracking().Where(e => e.EntityType != null).Select(e => e.EntityType!).Distinct().OrderBy(t => t)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
