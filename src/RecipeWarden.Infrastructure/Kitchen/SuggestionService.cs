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
