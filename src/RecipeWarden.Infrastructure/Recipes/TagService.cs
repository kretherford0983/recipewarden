using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Recipes;

/// <summary>FR-ORG-01, AC-20: free-form tags, de-duplicated on their normalized name, optionally typed.</summary>
public sealed class TagService(AppDbContext db, TimeProvider clock)
{
    public const string Duplicate = "A tag with that name already exists";

    /// <summary>Tags with the number of published recipes using each, for /manage/tags and browse-by-tag.</summary>
    public async Task<IReadOnlyList<TagSummary>> ListAsync(bool publishedOnly = true, CancellationToken cancellationToken = default)
    {
        var recipes = publishedOnly ? db.Recipes.Where(r => r.Status == RecipeStatus.Published) : db.Recipes;
        var list = await db.Tags.AsNoTracking()
            .Select(t => new TagSummary(t.Id, t.Name, t.Slug, t.TagType,
                db.RecipeTags.Count(rt => rt.TagId == t.Id && recipes.Any(r => r.Id == rt.RecipeId))))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.OrderBy(t => t.TagType.HasValue ? 0 : 1).ThenBy(t => t.TagType).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<TagSummary?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!SlugRules.IsValid(slug))
        {
            return null;
        }

        return await db.Tags.AsNoTracking().Where(t => t.Slug == slug)
            .Select(t => new TagSummary(t.Id, t.Name, t.Slug, t.TagType, 0))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the tags for the given names, creating any that do not exist yet ("Dinner" finds "dinner").</summary>
    internal async Task<IReadOnlyList<Tag>> ResolveAsync(IEnumerable<string> names, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var wanted = names
            .Select(NameRules.Clean)
            .Where(n => n.Length > 0)
            .DistinctBy(NameRules.Normalize, StringComparer.Ordinal)
            .ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var keys = wanted.Select(NameRules.Normalize).ToList();
        var existing = await db.Tags.Where(t => keys.Contains(t.NormalizedName)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Tag>(existing);
        foreach (var name in wanted.Where(n => existing.All(t => t.NormalizedName != NameRules.Normalize(n))))
        {
            var tag = new Tag
            {
                Name = name,
                NormalizedName = NameRules.Normalize(name),
                Slug = await UniqueSlugAsync(name, null, cancellationToken).ConfigureAwait(false),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Tags.Add(tag);
            result.Add(tag);
        }

        return result;
    }

    public async Task<SaveOutcome> CreateAsync(string? name, TagType? type, CancellationToken cancellationToken = default)
    {
        if (NameRules.ValidateTag(name) is { } error)
        {
            return SaveOutcome.Invalid([new PublishProblem(RecipeField.Tags, error)]);
        }

        var key = NameRules.Normalize(name);
        if (await db.Tags.AnyAsync(t => t.NormalizedName == key, cancellationToken).ConfigureAwait(false))
        {
            return SaveOutcome.Invalid([new PublishProblem(RecipeField.Tags, Duplicate)]);
        }

        var now = clock.GetUtcNow();
        var tag = new Tag
        {
            Name = NameRules.Clean(name),
            NormalizedName = key,
            Slug = await UniqueSlugAsync(name!, null, cancellationToken).ConfigureAwait(false),
            TagType = type,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SaveOutcome { Status = SaveStatus.Saved, Id = tag.Id, Slug = tag.Slug };
    }

    /// <summary>Renames or retypes a tag. Recipes using it are re-indexed, because tags are searchable.</summary>
    public async Task<SaveOutcome> UpdateAsync(int id, string? name, TagType? type, CancellationToken cancellationToken = default)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
        if (tag is null)
        {
            return SaveOutcome.NotFound;
        }

        if (NameRules.ValidateTag(name) is { } error)
        {
            return SaveOutcome.Invalid([new PublishProblem(RecipeField.Tags, error)]);
        }

        var key = NameRules.Normalize(name);
        if (await db.Tags.AnyAsync(t => t.NormalizedName == key && t.Id != id, cancellationToken).ConfigureAwait(false))
        {
            return SaveOutcome.Invalid([new PublishProblem(RecipeField.Tags, Duplicate)]);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (tag.NormalizedName != key)
        {
            tag.Slug = await UniqueSlugAsync(name!, id, cancellationToken).ConfigureAwait(false);
        }

        tag.Name = NameRules.Clean(name);
        tag.NormalizedName = key;
        tag.TagType = type;
        tag.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ReindexRecipesAsync(id, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new SaveOutcome { Status = SaveStatus.Saved, Id = tag.Id, Slug = tag.Slug };
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var recipeIds = await db.RecipeTags.Where(rt => rt.TagId == id).Select(rt => rt.RecipeId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        foreach (var recipeId in recipeIds)
        {
            await RecipeSearchIndex.UpdateAsync(db, recipeId, cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted > 0;
    }

    private async Task ReindexRecipesAsync(int tagId, CancellationToken cancellationToken)
    {
        var recipeIds = await db.RecipeTags.Where(rt => rt.TagId == tagId).Select(rt => rt.RecipeId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var recipeId in recipeIds)
        {
            await RecipeSearchIndex.UpdateAsync(db, recipeId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> UniqueSlugAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        var baseSlug = SlugRules.FromText(name, fallback: "tag");
        var taken = (await db.Tags.AsNoTracking().Where(t => t.Slug.StartsWith(baseSlug) && t.Id != exceptId)
            .Select(t => t.Slug).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Concat(db.Tags.Local.Where(t => t.Id != exceptId).Select(t => t.Slug))
            .ToHashSet(StringComparer.Ordinal);
        return SlugRules.MakeUnique(baseSlug, taken.Contains);
    }
}
