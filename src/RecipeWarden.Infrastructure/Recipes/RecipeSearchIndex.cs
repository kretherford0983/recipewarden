using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Recipes;

/// <summary>
/// FR-SRCH-01: keeps the FTS5 table RecipeSearch in step with published recipes. It is called by the save pipeline
/// inside the same transaction as the recipe change, so a saved recipe is searchable on the next request (AC-01).
/// Drafts and recipes in the trash are not indexed. rowid is the recipe Id. All SQL here is parameterized (SEC-20).
/// </summary>
internal static class RecipeSearchIndex
{
    public static async Task UpdateAsync(AppDbContext db, int recipeId, CancellationToken cancellationToken)
    {
        var recipe = await db.Recipes.AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Tags).ThenInclude(t => t.Tag)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken).ConfigureAwait(false);

        await RemoveAsync(db, recipeId, cancellationToken).ConfigureAwait(false);
        if (recipe is null || recipe.Status != RecipeStatus.Published)
        {
            return;
        }

        var ingredients = string.Join('\n', recipe.Ingredients.OrderBy(i => i.SortOrder).Select(i => i.RawText));
        var steps = string.Join('\n', recipe.Steps.OrderBy(s => s.SortOrder).Select(s => s.Title is null ? s.Text : s.Title + ". " + s.Text));
        var tags = string.Join('\n', recipe.Tags.Select(t => t.Tag!.Name));
        var description = recipe.Description ?? string.Empty;
        var notes = recipe.Notes ?? string.Empty;

        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO RecipeSearch(rowid, Title, Description, Ingredients, Steps, Tags, Notes) VALUES ({recipe.Id}, {recipe.Title}, {description}, {ingredients}, {steps}, {tags}, {notes})",
            cancellationToken).ConfigureAwait(false);
    }

    public static Task RemoveAsync(AppDbContext db, int recipeId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM RecipeSearch WHERE rowid = {recipeId}", cancellationToken);

    /// <summary>Rebuilds the whole index from the recipes table (used after restores and by tests).</summary>
    public static async Task RebuildAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync($"DELETE FROM RecipeSearch", cancellationToken).ConfigureAwait(false);
        var ids = await db.Recipes.AsNoTracking().Where(r => r.Status == RecipeStatus.Published)
            .Select(r => r.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in ids)
        {
            await UpdateAsync(db, id, cancellationToken).ConfigureAwait(false);
        }
    }
}
