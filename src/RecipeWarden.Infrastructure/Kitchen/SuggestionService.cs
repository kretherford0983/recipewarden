using RecipeWarden.Core.Measurement;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Kitchen;

/// <summary>
/// FR-TOOL-05, ADR 0017: editors dismiss a suggestion, or apply it to the recipe. Applying moves the recipe's
/// accepted inventory version to now, so the change becomes the recipe's default view. Reading never writes.
/// </summary>
public sealed class SuggestionService(AppDbContext db, KitchenService kitchen, TimeProvider clock)
{
    public async Task<IReadOnlySet<string>> SettledKeysAsync(int recipeId, CancellationToken cancellationToken = default) =>
        (await db.RecipeSuggestionStates.AsNoTracking().Where(s => s.RecipeId == recipeId).Select(s => s.SuggestionKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

    public Task<bool> DismissAsync(int recipeId, string key, string userId, CancellationToken cancellationToken = default) =>
        SettleAsync(recipeId, key, SuggestionStatus.Dismissed, userId, cancellationToken);

    public Task<bool> ApplyAsync(int recipeId, string key, string userId, CancellationToken cancellationToken = default) =>
        SettleAsync(recipeId, key, SuggestionStatus.Applied, userId, cancellationToken);

    /// <summary>
    /// BR-122: applies a scale suggestion line by line. <paramref name="choices"/> maps ingredient line ids to Weight
    /// (picked) or AsWritten (offered, not picked); then the suggestion is settled as applied.
    /// </summary>
    public async Task<bool> ApplyAsync(int recipeId, string key, IReadOnlyDictionary<int, LineMeasure> choices, string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (!key.StartsWith("weight:", StringComparison.Ordinal))
        {
            return false;
        }

        var ids = choices.Keys.ToList();
        var lines = await db.RecipeIngredients.Where(i => i.RecipeId == recipeId && ids.Contains(i.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var line in lines)
        {
            line.Measure = choices[line.Id];
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await SettleAsync(recipeId, key, SuggestionStatus.Applied, userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SettleAsync(int recipeId, string key, SuggestionStatus status, string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || !key.StartsWith("weight:", StringComparison.Ordinal)
            || !await db.Recipes.AnyAsync(r => r.Id == recipeId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var version = await kitchen.CurrentVersionAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var row = await db.RecipeSuggestionStates.FirstOrDefaultAsync(s => s.RecipeId == recipeId && s.SuggestionKey == key, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new RecipeSuggestionState { RecipeId = recipeId, SuggestionKey = key };
            db.RecipeSuggestionStates.Add(row);
        }

        row.Status = status;
        row.InventoryVersion = version;
        row.At = now;
        row.ById = userId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (status == SuggestionStatus.Applied)
        {
            await db.Recipes.Where(r => r.Id == recipeId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.InventoryVersionSeen, version), cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
