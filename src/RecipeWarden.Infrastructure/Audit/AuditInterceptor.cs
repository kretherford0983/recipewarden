using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RecipeWarden.Core.Audit;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Audit;

/// <summary>Who is writing: a signed-in user, an anonymous visitor (shopping check-off), the CLI or the app itself.</summary>
public sealed record AuditActorInfo(string? UserId, string UserName, string? Ip);

/// <summary>The current actor. Web reads it from the request; everything else is <see cref="AuditRules.SystemActor"/>.</summary>
public interface IAuditActor
{
    AuditActorInfo Current { get; }
}

/// <summary>Background services and migrations: "system", unless a scope says otherwise (<see cref="AuditScope.As"/>).</summary>
public sealed class SystemAuditActor : IAuditActor
{
    public AuditActorInfo Current => AuditScope.Actor ?? new AuditActorInfo(null, AuditRules.SystemActor, null);
}

/// <summary>
/// Ambient audit settings for the current async flow: suppression for seed data (thousands of catalogue rows are not
/// "writes" by anyone) and an explicit actor for the CLI.
/// </summary>
public static class AuditScope
{
    private static readonly AsyncLocal<int> Suppressed = new();
    private static readonly AsyncLocal<AuditActorInfo?> ActorOverride = new();

    public static bool IsSuppressed => Suppressed.Value > 0;

    public static AuditActorInfo? Actor => ActorOverride.Value;

    public static IDisposable Suppress()
    {
        Suppressed.Value++;
        return new Reset(() => Suppressed.Value--);
    }

    public static IDisposable As(AuditActorInfo actor)
    {
        var previous = ActorOverride.Value;
        ActorOverride.Value = actor;
        return new Reset(() => ActorOverride.Value = previous);
    }

    private sealed class Reset(Action undo) : IDisposable
    {
        private Action? undo = undo;

        public void Dispose()
        {
            undo?.Invoke();
            undo = null;
        }
    }
}

/// <summary>
/// FR-ADM-01, AC-74, ADR 0041: every create, update and delete that goes through EF Core is written to the audit log
/// with the actor, the entity, the time and the IP. Updates list the names of changed properties; no values are ever
/// stored, so password hashes, keys and tokens cannot leak into the log (SEC-72). Entries for new rows are written
/// right after the save, when their ids are known.
/// </summary>
public sealed class AuditInterceptor(IAuditActor actor, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly string[] NameProperties = ["Title", "Name", "Email"];

    private List<Pending>? pending;
    private bool writing;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (Flush(eventData.Context) is { } ctx)
        {
            writing = true;
            try
            {
                ctx.SaveChanges();
            }
            catch (DbUpdateException ex) when (IsMissingAuditTable(ex))
            {
                Discard(ctx);
            }
            finally
            {
                writing = false;
            }
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (Flush(eventData.Context) is { } ctx)
        {
            writing = true;
            try
            {
                await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsMissingAuditTable(ex))
            {
                Discard(ctx);
            }
            finally
            {
                writing = false;
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => pending = null;

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        pending = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes made before the M7 migration has run (only migration-time code does this) have nowhere to be logged.
    /// Every other failure of the audit write is raised.
    /// </summary>
    private static bool IsMissingAuditTable(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.Sqlite.SqliteException s && s.Message.Contains("no such table: AuditEntries", StringComparison.Ordinal);

    private static void Discard(AppDbContext ctx)
    {
        foreach (var e in ctx.ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added).ToList())
        {
            e.State = EntityState.Detached;
        }
    }

    private void Capture(DbContext? context)
    {
        if (context is not AppDbContext || writing || AuditScope.IsSuppressed)
        {
            pending = null;
            return;
        }

        context.ChangeTracker.DetectChanges();
        var list = new List<Pending>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            var change = entry.State switch
            {
                EntityState.Added => AuditChange.Create,
                EntityState.Modified => AuditChange.Update,
                EntityState.Deleted => AuditChange.Delete,
                _ => (AuditChange?)null,
            };
            if (change is null)
            {
                continue;
            }

            var entity = EntityName(entry);
            var changed = change == AuditChange.Update
                ? entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToArray()
                : [];
            if (!AuditRules.ShouldAudit(entity, change.Value, changed))
            {
                continue;
            }

            list.Add(new Pending(entry, entity, change.Value, change == AuditChange.Create ? null : KeyOf(entry), changed, NameOf(entry)));
        }

        pending = list.Count > 0 ? list : null;
    }

    private AppDbContext? Flush(DbContext? context)
    {
        var items = pending;
        pending = null;
        if (items is null || context is not AppDbContext db || writing)
        {
            return null;
        }

        var who = actor.Current;
        var at = clock.GetUtcNow();
        foreach (var p in items)
        {
            db.AuditEntries.Add(new AuditEntry
            {
                At = at,
                UserId = who.UserId,
                UserName = AuditRules.Truncate(who.UserName, 256),
                Ip = who.Ip,
                Action = AuditRules.ActionName(p.Entity, p.Change),
                EntityType = AuditRules.Truncate(p.Entity, 64),
                EntityId = AuditRules.Truncate(p.Key ?? KeyOf(p.Entry), AuditRules.MaxEntityIdLength),
                DetailsJson = Details(p),
            });
        }

        return db;
    }

    private static string? Details(Pending p)
    {
        var details = new Dictionary<string, object>(StringComparer.Ordinal);
        if (p.Changed.Length > 0)
        {
            details["changed"] = p.Changed;
        }

        if (p.Name is not null)
        {
            details["name"] = p.Name;
        }

        if (details.Count == 0)
        {
            return null;
        }

        return AuditRules.Truncate(JsonSerializer.Serialize(details), AuditRules.MaxDetailsLength);
    }

    private static string EntityName(EntityEntry entry)
    {
        var name = entry.Metadata.ClrType.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick > 0 ? name[..tick] : name;
    }

    private static string? KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return null;
        }

        return string.Join('/', key.Properties.Select(p => Convert.ToString(entry.Property(p.Name).CurrentValue, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>A title, name or email helps a reader of the log; it is not a secret. Nothing else is copied.</summary>
    private static string? NameOf(EntityEntry entry)
    {
        foreach (var name in NameProperties)
        {
            if (entry.Metadata.FindProperty(name) is { ClrType: var t } && t == typeof(string))
            {
                var value = (entry.State == EntityState.Deleted ? entry.Property(name).OriginalValue : entry.Property(name).CurrentValue) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return AuditRules.Truncate(value, 200);
                }
            }
        }

        return null;
    }

    private sealed record Pending(EntityEntry Entry, string Entity, AuditChange Change, string? Key, string[] Changed, string? Name);
}
