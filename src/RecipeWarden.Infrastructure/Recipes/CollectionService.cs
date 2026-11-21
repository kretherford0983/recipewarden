using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Recipes;

/// <summary>FR-ORG-02, AC-20: collections are created, ordered and filled by editors and visible to every reader.</summary>
public sealed class CollectionService(AppDbContext db, TimeProvider clock)
{
    public const string Duplicate = "A collection with that name already exists";
    public const string Changed = "This collection was changed by someone else. Reload and try again.";

    public async Task<IReadOnlyList<CollectionSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var published = db.Recipes.Where(r => r.Status == RecipeStatus.Published);
        return await db.Collections.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CollectionSummary(c.Id, c.Name, c.Slug, c.Description,
                db.CollectionRecipes.Count(cr => cr.CollectionId == c.Id && published.Any(r => r.Id == cr.RecipeId)),
                c.Version))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A collection and its recipes in order; drafts only when <paramref name="includeDrafts"/>.</summary>
    public async Task<CollectionView?> GetBySlugAsync(string slug, bool includeDrafts, CancellationToken cancellationToken = default)
    {
        if (!SlugRules.IsValid(slug))
        {
            return null;
        }

        var c = await db.Collections.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken).ConfigureAwait(false);
        if (c is null)
        {
            return null;
        }

        var recipes = includeDrafts ? db.Recipes : db.Recipes.Where(r => r.Status == RecipeStatus.Published);
        var rows = await (from cr in db.CollectionRecipes
                          join r in recipes on cr.RecipeId equals r.Id
                          where cr.CollectionId == c.Id
                          orderby cr.SortOrder
                          select new { r.Id, r.Slug, r.Title, r.Description, r.TotalMinutes, r.PrepMinutes, r.CookMinutes, r.Rating, r.Status })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var cards = rows.Select(r => RecipeSearchService.Card(r.Id, r.Slug, r.Title, r.Description, r.TotalMinutes, r.PrepMinutes, r.CookMinutes, r.Rating, r.Status)).ToList();
        return new CollectionView(c.Id, c.Name, c.Slug, c.Description, c.Version, cards);
    }

    public async Task<SaveOutcome> CreateAsync(string? name, string? description, string userId, CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(name, description, null, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        var next = await db.Collections.MaxAsync(c => (int?)c.SortOrder, cancellationToken).ConfigureAwait(false) ?? -1;
        var c = new RecipeCollection
        {
            Name = NameRules.Clean(name),
            NormalizedName = NameRules.Normalize(name),
            Slug = await UniqueSlugAsync(name!, null, cancellationToken).ConfigureAwait(false),
            Description = Clean(description),
            SortOrder = next + 1,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedById = userId,
            UpdatedById = userId,
        };
        db.Collections.Add(c);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SaveOutcome { Status = SaveStatus.Saved, Id = c.Id, Slug = c.Slug, Version = c.Version };
    }

    public async Task<SaveOutcome> UpdateAsync(int id, string? name, string? description, int expectedVersion, string userId,
        CancellationToken cancellationToken = default)
    {
        var c = await db.Collections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        if (c is null)
        {
            return SaveOutcome.NotFound;
        }

        if (c.Version != expectedVersion)
        {
            return new SaveOutcome { Status = SaveStatus.Conflict, Id = id, Slug = c.Slug, Version = c.Version,
                Errors = [new PublishProblem(RecipeField.Title, Changed)] };
        }

        if (await ValidateAsync(name, description, id, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        if (c.NormalizedName != NameRules.Normalize(name))
        {
            c.Slug = await UniqueSlugAsync(name!, id, cancellationToken).ConfigureAwait(false);
        }

        c.Name = NameRules.Clean(name);
        c.NormalizedName = NameRules.Normalize(name);
        c.Description = Clean(description);
        c.Version++;
        c.UpdatedAt = clock.GetUtcNow();
        c.UpdatedById = userId;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new SaveOutcome { Status = SaveStatus.Conflict, Id = id, Errors = [new PublishProblem(RecipeField.Title, Changed)] };
        }

        return new SaveOutcome { Status = SaveStatus.Saved, Id = c.Id, Slug = c.Slug, Version = c.Version };
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        await db.Collections.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;

    /// <summary>Moves a collection up (-1) or down (+1) in the list (AC-20 "reordered").</summary>
    public async Task<bool> MoveAsync(int id, int direction, CancellationToken cancellationToken = default)
    {
        var all = await db.Collections.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!Swap(all, all.FindIndex(c => c.Id == id), direction))
        {
            return false;
        }

        for (var i = 0; i < all.Count; i++)
        {
            all[i].SortOrder = i;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> AddRecipeAsync(int collectionId, int recipeId, CancellationToken cancellationToken = default)
    {
        if (!await db.Collections.AnyAsync(c => c.Id == collectionId, cancellationToken).ConfigureAwait(false)
            || !await db.Recipes.AnyAsync(r => r.Id == recipeId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (await db.CollectionRecipes.AnyAsync(cr => cr.CollectionId == collectionId && cr.RecipeId == recipeId, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        var next = await db.CollectionRecipes.Where(cr => cr.CollectionId == collectionId)
            .MaxAsync(cr => (int?)cr.SortOrder, cancellationToken).ConfigureAwait(false) ?? -1;
        db.CollectionRecipes.Add(new CollectionRecipe { CollectionId = collectionId, RecipeId = recipeId, SortOrder = next + 1 });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RemoveRecipeAsync(int collectionId, int recipeId, CancellationToken cancellationToken = default) =>
        await db.CollectionRecipes.Where(cr => cr.CollectionId == collectionId && cr.RecipeId == recipeId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;

    /// <summary>Moves a recipe up (-1) or down (+1) within a collection.</summary>
    public async Task<bool> MoveRecipeAsync(int collectionId, int recipeId, int direction, CancellationToken cancellationToken = default)
    {
        var items = await db.CollectionRecipes.Where(cr => cr.CollectionId == collectionId)
            .OrderBy(cr => cr.SortOrder).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!Swap(items, items.FindIndex(cr => cr.RecipeId == recipeId), direction))
        {
            return false;
        }

        for (var i = 0; i < items.Count; i++)
        {
            items[i].SortOrder = i;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool Swap<T>(List<T> list, int index, int direction)
    {
        var target = index + Math.Sign(direction);
        if (index < 0 || direction == 0 || target < 0 || target >= list.Count)
        {
            return false;
        }

        (list[index], list[target]) = (list[target], list[index]);
        return true;
    }

    private async Task<SaveOutcome?> ValidateAsync(string? name, string? description, int? exceptId, CancellationToken cancellationToken)
    {
        if (NameRules.ValidateCollection(name) is { } error)
        {
            return SaveOutcome.Invalid([new PublishProblem(RecipeField.Title, error)]);
        }

        if ((description?.Length ?? 0) > RecipeEdit.MaxDescriptionLength)
        {
            return SaveOutcome.Invalid([new PublishProblem(RecipeField.Description, "The description is limited to 4000 characters")]);
        }

        var key = NameRules.Normalize(name);
        return await db.Collections.AnyAsync(c => c.NormalizedName == key && c.Id != exceptId, cancellationToken).ConfigureAwait(false)
            ? SaveOutcome.Invalid([new PublishProblem(RecipeField.Title, Duplicate)])
            : null;
    }

    private async Task<string> UniqueSlugAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        var baseSlug = SlugRules.FromText(name, fallback: "collection");
        var taken = (await db.Collections.AsNoTracking().Where(c => c.Slug.StartsWith(baseSlug) && c.Id != exceptId)
            .Select(c => c.Slug).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        return SlugRules.MakeUnique(baseSlug, taken.Contains);
    }

    private static string? Clean(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
