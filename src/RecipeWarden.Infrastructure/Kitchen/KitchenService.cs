using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Kitchen;

/// <summary>A tool on /manage/kitchen, active or retired.</summary>
public sealed record KitchenToolRow(KitchenToolInfo Tool, string Description, DateTimeOffset AddedAt, DateTimeOffset? RetiredAt);

/// <summary>Result of a kitchen edit: saved, or the messages to show.</summary>
public sealed record KitchenEditResult(bool Ok, IReadOnlyList<string> Errors, bool NotFound = false)
{
    public static KitchenEditResult Success { get; } = new(true, []);
}

/// <summary>
/// FR-TOOL-01/02/03, BR-30, ADR 0017, ADR 0021: My Kitchen. Every add, edit, retire or reactivate increments the
/// inventory version and stores a snapshot of the active tools at that version.
/// </summary>
public sealed class KitchenService(AppDbContext db, TimeProvider clock)
{
    public const string SeedName = "kitchen-v1";

    internal static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<int> CurrentVersionAsync(CancellationToken cancellationToken = default) =>
        await db.InventoryVersions.AsNoTracking().Where(v => v.Id == 1).Select(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>The active tools now (FR-TOOL-03).</summary>
    public async Task<KitchenTools> CurrentAsync(CancellationToken cancellationToken = default) =>
        KitchenTools.From(await ActiveInfosAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>The active tools at an earlier version (ADR 0017). Version 0, or one with no snapshot, is an empty kitchen.</summary>
    public async Task<KitchenTools> AtVersionAsync(int version, CancellationToken cancellationToken = default)
    {
        var json = await db.InventorySnapshots.AsNoTracking().Where(s => s.Version == version).Select(s => s.ToolsJson)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return KitchenTools.From(json is null ? [] : JsonSerializer.Deserialize<List<KitchenToolInfo>>(json, Json) ?? []);
    }

    public async Task<IReadOnlyList<KitchenToolRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tools = await Query().ToListAsync(cancellationToken).ConfigureAwait(false);
        return tools
            .OrderByDescending(t => t.IsActive).ThenBy(t => t.ToolType).ThenByDescending(t => Info(t).CapacityMl ?? 0m).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new KitchenToolRow(Info(t), KitchenRules.Describe(Info(t)), t.AddedAt, t.RetiredAt))
            .ToList();
    }

    public async Task<KitchenEditResult> AddAsync(KitchenToolInfo tool, string? userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var errors = KitchenRules.Validate(tool);
        if (errors.Count > 0)
        {
            return new KitchenEditResult(false, errors);
        }

        var now = clock.GetUtcNow();
        var entity = new KitchenTool { AddedAt = now };
        await CopyAsync(tool, entity, userId, now, cancellationToken).ConfigureAwait(false);
        db.KitchenTools.Add(entity);
        await SaveWithNewVersionAsync(now, cancellationToken).ConfigureAwait(false);
        return KitchenEditResult.Success;
    }

    /// <summary>W5: adds a preset's tools, skipping any the kitchen already has (same type, size and unit).</summary>
    public async Task<KitchenEditResult> AddPresetAsync(string? key, string? userId, CancellationToken cancellationToken = default)
    {
        var preset = KitchenRules.Presets.FirstOrDefault(p => p.Key == key);
        if (preset is null)
        {
            return new KitchenEditResult(false, ["Unknown preset"], NotFound: true);
        }

        var existing = (await Query().ToListAsync(cancellationToken).ConfigureAwait(false)).Select(Info).ToList();
        var now = clock.GetUtcNow();
        var added = 0;
        foreach (var tool in preset.Tools.Where(t => !existing.Any(e => SameTool(e, t))))
        {
            var entity = new KitchenTool { AddedAt = now };
            await CopyAsync(tool, entity, userId, now, cancellationToken).ConfigureAwait(false);
            db.KitchenTools.Add(entity);
            added++;
        }

        // A retired tool of the same kind comes back instead of being added twice.
        foreach (var match in existing.Where(e => !e.IsActive && preset.Tools.Any(t => SameTool(e, t))))
        {
            var entity = await db.KitchenTools.FirstAsync(t => t.Id == match.Id, cancellationToken).ConfigureAwait(false);
            entity.IsActive = true;
            entity.RetiredAt = null;
            entity.UpdatedAt = now;
            entity.UpdatedById = userId;
            added++;
        }

        if (added > 0)
        {
            await SaveWithNewVersionAsync(now, cancellationToken).ConfigureAwait(false);
        }

        return KitchenEditResult.Success;
    }

    public async Task<KitchenEditResult> UpdateAsync(int id, KitchenToolInfo tool, string? userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var entity = await db.KitchenTools.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return new KitchenEditResult(false, ["That tool no longer exists"], NotFound: true);
        }

        var errors = KitchenRules.Validate(tool with { Type = entity.ToolType });
        if (errors.Count > 0)
        {
            return new KitchenEditResult(false, errors);
        }

        var now = clock.GetUtcNow();
        await CopyAsync(tool with { Type = entity.ToolType, IsActive = entity.IsActive }, entity, userId, now, cancellationToken).ConfigureAwait(false);
        await SaveWithNewVersionAsync(now, cancellationToken).ConfigureAwait(false);
        return KitchenEditResult.Success;
    }

    /// <summary>FR-TOOL-02: retire or re-activate; the tool is kept either way.</summary>
    public async Task<KitchenEditResult> SetActiveAsync(int id, bool active, string? userId, CancellationToken cancellationToken = default)
    {
        var entity = await db.KitchenTools.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return new KitchenEditResult(false, ["That tool no longer exists"], NotFound: true);
        }

        if (entity.IsActive == active)
        {
            return KitchenEditResult.Success;
        }

        var now = clock.GetUtcNow();
        entity.IsActive = active;
        entity.RetiredAt = active ? null : now;
        entity.UpdatedAt = now;
        entity.UpdatedById = userId;
        await SaveWithNewVersionAsync(now, cancellationToken).ConfigureAwait(false);
        return KitchenEditResult.Success;
    }

    /// <summary>ADR 0021: the standard set as version 1, once per database.</summary>
    internal async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.SeedRuns.AnyAsync(r => r.Name == SeedName, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var now = clock.GetUtcNow();
        foreach (var tool in KitchenRules.StandardSet)
        {
            var entity = new KitchenTool { AddedAt = now };
            await CopyAsync(tool, entity, null, now, cancellationToken).ConfigureAwait(false);
            db.KitchenTools.Add(entity);
        }

        db.SeedRuns.Add(new SeedRun { Name = SeedName, AppliedAt = now });
        await SaveWithNewVersionAsync(now, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private IQueryable<KitchenTool> Query() =>
        db.KitchenTools.AsNoTracking().Include(t => t.CapacityUnit).Include(t => t.ResolutionUnit);

    private async Task<List<KitchenToolInfo>> ActiveInfosAsync(CancellationToken cancellationToken) =>
        (await Query().Where(t => t.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(Info).ToList();

    /// <summary>BR-30: save the change, increment the version and snapshot the active tools, in one transaction.</summary>
    private async Task SaveWithNewVersionAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.InventoryVersions.FirstOrDefaultAsync(v => v.Id == 1, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new InventoryVersion { Id = 1 };
            db.InventoryVersions.Add(row);
        }

        row.Version++;
        row.ChangedAt = now;
        var active = await ActiveInfosAsync(cancellationToken).ConfigureAwait(false);
        db.InventorySnapshots.Add(new InventorySnapshot
        {
            Version = row.Version,
            ToolsJson = JsonSerializer.Serialize(active, Json),
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CopyAsync(KitchenToolInfo tool, KitchenTool entity, string? userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        entity.Name = tool.Name.Trim();
        entity.ToolType = tool.Type;
        entity.QuantityOwned = tool.QuantityOwned;
        entity.DefaultFillPct = tool.DefaultFillPct;
        entity.CapacityAmount = tool.CapacityAmount;
        entity.CapacityUnitId = await UnitIdAsync(tool.CapacityUnit, cancellationToken).ConfigureAwait(false);
        entity.ResolutionAmount = tool.Resolution;
        entity.ResolutionUnitId = await UnitIdAsync(tool.ResolutionUnit, cancellationToken).ConfigureAwait(false);
        entity.SupportedUnits = tool.SupportedUnits is { Count: > 0 } units ? string.Join(',', units) : null;
        entity.PanShape = tool.PanShape;
        entity.PanWidth = tool.PanWidth;
        entity.PanLength = tool.PanShape is PanShape.Round or PanShape.Square ? tool.PanWidth : tool.PanLength;
        entity.IsActive = tool.IsActive;
        entity.UpdatedAt = now;
        entity.UpdatedById = userId;
    }

    private async Task<int?> UnitIdAsync(string? code, CancellationToken cancellationToken) =>
        code is null ? null : await db.Units.Where(u => u.Code == code).Select(u => (int?)u.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    internal static KitchenToolInfo Info(KitchenTool t) => new(
        t.Id, t.Name, t.ToolType, t.CapacityAmount, t.CapacityUnit?.Code, t.ResolutionAmount, t.ResolutionUnit?.Code,
        t.SupportedUnits?.Split(',', StringSplitOptions.RemoveEmptyEntries), t.QuantityOwned, t.DefaultFillPct,
        t.PanShape, t.PanWidth, t.PanLength, t.IsActive);

    private static bool SameTool(KitchenToolInfo a, KitchenToolInfo b) =>
        a.Type == b.Type && a.CapacityUnit == b.CapacityUnit && a.CapacityAmount is { } x && b.CapacityAmount is { } y
            ? Math.Abs(x - y) < 0.0001m
            : a.Type == b.Type && a.Type is ToolType.Scale or ToolType.Pan && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
