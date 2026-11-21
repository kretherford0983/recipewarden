using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Recipes;

/// <summary>
/// FR-REC-01/02/04: the recipe save pipeline. Every write to a recipe goes through here, so the Core rules
/// (BR-01 to BR-06, BR-10/11) apply the same way from any page, and the audit log (FR-ADM-01, M7) has one place to
/// hook in. The search index is updated in the same transaction.
/// </summary>
public sealed class RecipeService(AppDbContext db, TimeProvider clock, TagService tags)
{
    public const string SourceChangeNeedsAdmin = "Only an Admin can change the source of a web recipe";

    public async Task<SaveOutcome> CreateAsync(RecipeEdit edit, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var parsed = Parse(edit);
        if (Check(edit, parsed) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        var recipe = new Recipe { CreatedAt = now, CreatedById = userId, Version = 0 };
        var taken = await TakenSlugsAsync(SlugRules.FromText(edit.Title), null, cancellationToken).ConfigureAwait(false);
        recipe.Slug = SlugRules.MakeUnique(SlugRules.FromText(edit.Title), taken.Contains);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ApplyAsync(recipe, edit, parsed, userId, now, cancellationToken).ConfigureAwait(false);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecipeSearchIndex.UpdateAsync(db, recipe.Id, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Saved(recipe, parsed.Check);
    }

    /// <summary>BR-06: <paramref name="expectedVersion"/> is the version the editor loaded; a newer save wins and this one is refused with a diff.</summary>
    public async Task<SaveOutcome> UpdateAsync(int id, RecipeEdit edit, int expectedVersion, string userId, bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var recipe = await LoadAggregate(db.Recipes).FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (recipe is null)
        {
            return SaveOutcome.NotFound;
        }

        if (recipe.Version != expectedVersion)
        {
            return Conflict(edit, recipe);
        }

        if (!SourceRules.CanChangeType(recipe.SourceType, edit.SourceType, isAdmin))
        {
            return SaveOutcome.Invalid([new PublishProblem(RecipeField.SourceType, SourceChangeNeedsAdmin)]);
        }

        var parsed = Parse(edit);
        if (Check(edit, parsed) is { } invalid)
        {
            return invalid;
        }

        var now = clock.GetUtcNow();
        var wasPublished = recipe.Status == RecipeStatus.Published;
        var ownAliases = await db.RecipeSlugAliases.Where(a => a.RecipeId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var newBase = SlugRules.FromText(edit.Title);
        var taken = await TakenSlugsAsync(newBase, id, cancellationToken).ConfigureAwait(false);
        var change = SlugRules.PlanTitleChange(recipe.Slug, wasPublished ? RecipeStatus.Published : RecipeStatus.Draft, edit.Title, taken.Contains);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (change.Changed)
        {
            if (change.AliasToKeep is { } alias && ownAliases.All(a => a.Slug != alias))
            {
                db.RecipeSlugAliases.Add(new RecipeSlugAlias { Slug = alias, RecipeId = id, CreatedAt = now });
            }

            // Changing back to an earlier title: that slug becomes current again.
            db.RecipeSlugAliases.RemoveRange(ownAliases.Where(a => a.Slug == change.NewSlug));
            recipe.Slug = change.NewSlug;
        }

        db.IngredientGroups.RemoveRange(recipe.IngredientGroups);
        db.RecipeIngredients.RemoveRange(recipe.Ingredients);
        db.Steps.RemoveRange(recipe.Steps);
        db.RecipeTags.RemoveRange(recipe.Tags);
        recipe.IngredientGroups.Clear();
        recipe.Ingredients.Clear();
        recipe.Steps.Clear();
        recipe.Tags.Clear();
        await ApplyAsync(recipe, edit, parsed, userId, now, cancellationToken).ConfigureAwait(false);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone saved between our read and our write.
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            var current = await LoadAggregate(db.Recipes.AsNoTracking()).FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
            return current is null ? SaveOutcome.NotFound : Conflict(edit, current);
        }

        await RecipeSearchIndex.UpdateAsync(db, recipe.Id, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Saved(recipe, parsed.Check);
    }

    public async Task<RecipeForEdit?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var r = await LoadAggregate(db.Recipes.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        return r is null ? null : new RecipeForEdit(r.Id, r.Slug, r.Status, r.Version, ToEdit(r));
    }

    /// <summary>FR-VIEW-01. Drafts are found only when <paramref name="canSeeDrafts"/> (ADR 0009); old slugs redirect (ADR 0008).</summary>
    public async Task<RecipeLookup> GetForReadingAsync(string slug, bool canSeeDrafts, CancellationToken cancellationToken = default)
    {
        if (!SlugRules.IsValid(slug))
        {
            return RecipeLookup.None;
        }

        var visible = canSeeDrafts ? db.Recipes : db.Recipes.Where(r => r.Status == RecipeStatus.Published);
        var recipe = await LoadAggregate(visible.AsNoTracking()).FirstOrDefaultAsync(r => r.Slug == slug, cancellationToken).ConfigureAwait(false);
        if (recipe is null)
        {
            var current = await (from a in db.RecipeSlugAliases
                                 join r in visible on a.RecipeId equals r.Id
                                 where a.Slug == slug
                                 select r.Slug).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            return current is null ? RecipeLookup.None : new RecipeLookup(null, current);
        }

        var collections = await (from cr in db.CollectionRecipes
                                 join c in db.Collections on cr.CollectionId equals c.Id
                                 where cr.RecipeId == recipe.Id
                                 orderby c.SortOrder, c.Name
                                 select new CollectionLink(c.Id, c.Name, c.Slug)).ToListAsync(cancellationToken).ConfigureAwait(false);

        return new RecipeLookup(ToView(recipe, collections), null);
    }

    /// <summary>BR-05: moves a recipe to the trash. Returns false if it does not exist (or is already in the trash).</summary>
    public async Task<bool> DeleteAsync(int id, string userId, CancellationToken cancellationToken = default)
    {
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (recipe is null)
        {
            return false;
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        recipe.DeletedAt = clock.GetUtcNow();
        recipe.DeletedById = userId;
        recipe.Version++;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecipeSearchIndex.RemoveAsync(db, id, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>BR-05: restores a recipe from the trash within 30 days.</summary>
    public async Task<bool> RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        var recipe = await db.Recipes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt != null, cancellationToken).ConfigureAwait(false);
        if (recipe is null || !TrashRules.CanRestore(recipe.DeletedAt!.Value, clock.GetUtcNow()))
        {
            return false;
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        recipe.DeletedAt = null;
        recipe.DeletedById = null;
        recipe.Version++;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecipeSearchIndex.UpdateAsync(db, id, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>BR-05: an Admin purges a recipe in the trash at once. Callers check the policy too (SEC-14).</summary>
    public async Task<bool> PurgeAsync(int id, IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        if (!TrashRules.CanPurgeImmediately(roles))
        {
            throw new UnauthorizedAccessException("Only an Admin can purge a recipe immediately (BR-05).");
        }

        var deleted = await db.Recipes.IgnoreQueryFilters()
            .Where(r => r.Id == id && r.DeletedAt != null)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return deleted > 0;
    }

    /// <summary>BR-05: purges recipes that have been in the trash for 30 days. Returns how many were purged.</summary>
    public async Task<int> PurgeDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var trash = await db.Recipes.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.DeletedAt != null)
            .Select(r => new { r.Id, r.DeletedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var due = trash.Where(r => TrashRules.IsPurgeDue(r.DeletedAt!.Value, now)).Select(r => r.Id).ToList();
        if (due.Count == 0)
        {
            return 0;
        }

        return await db.Recipes.IgnoreQueryFilters().Where(r => due.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrashItem>> ListTrashAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.Recipes.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.DeletedAt != null)
            .Select(r => new { r.Id, r.Title, r.DeletedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return items
            .OrderByDescending(r => r.DeletedAt)
            .Select(r => new TrashItem(r.Id, r.Title, r.DeletedAt!.Value, TrashRules.PurgesOn(r.DeletedAt!.Value)))
            .ToList();
    }

    /// <summary>Recreates the search index from the database (after a restore, or if it was lost).</summary>
    public Task RebuildSearchIndexAsync(CancellationToken cancellationToken = default) =>
        RecipeSearchIndex.RebuildAsync(db, cancellationToken);

    /// <summary>The editor's text, rebuilt from the stored recipe (headings for groups and step sections).</summary>
    public static RecipeEdit ToEdit(Recipe r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new RecipeEdit
        {
            Title = r.Title,
            Description = r.Description,
            Servings = r.ServingsEstimated ? null : r.Servings,
            ServingsMax = r.ServingsEstimated ? null : r.ServingsMax,
            PrepMinutes = r.PrepMinutes,
            PrepMinutesMax = r.PrepMinutesMax,
            CookMinutes = r.CookMinutes,
            CookMinutesMax = r.CookMinutesMax,
            TotalMinutes = r.TotalMinutes,
            TotalMinutesMax = r.TotalMinutesMax,
            IsPrecision = r.IsPrecision,
            Notes = r.Notes,
            SourceType = r.SourceType,
            SourceName = r.SourceName,
            SourceUrl = r.SourceUrl,
            SourceAuthor = r.SourceAuthor,
            IngredientsText = IngredientsText(r),
            StepsText = StepTextRules.Join(r.Steps.OrderBy(s => s.SortOrder).Select(s => new StepText(s.SectionName, s.Text))),
            Tags = r.Tags.Select(t => t.Tag!.Name).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            WantsPublish = r.Status == RecipeStatus.Published,
        };
    }

    private static string IngredientsText(Recipe r)
    {
        var lines = new List<string>();
        int? group = null;
        foreach (var i in r.Ingredients.OrderBy(i => i.SortOrder))
        {
            if (i.GroupId != group && i.Group is not null)
            {
                lines.Add(i.Group.Name + ":");
            }

            group = i.GroupId;
            lines.Add(i.RawText);
        }

        return string.Join('\n', lines);
    }

    private sealed record ParsedEdit(IReadOnlyList<IngredientLine> Ingredients, IReadOnlyList<StepText> Steps, PublishCheck Check);

    private static ParsedEdit Parse(RecipeEdit edit)
    {
        var ingredients = IngredientLineParser.ParseList(edit.IngredientsText);
        var steps = StepTextRules.Split(edit.StepsText);
        var check = PublishRules.Check(edit.Title, ingredients.Count(l => !l.IsHeading), steps.Count);
        return new ParsedEdit(ingredients, steps, check);
    }

    private static SaveOutcome? Check(RecipeEdit edit, ParsedEdit parsed)
    {
        var errors = parsed.Check.Errors.Concat(edit.Validate()).ToList();
        return errors.Count > 0 ? SaveOutcome.Invalid(errors) : null;
    }

    private async Task ApplyAsync(Recipe recipe, RecipeEdit edit, ParsedEdit parsed, string userId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var servings = ServingsRules.Resolve(edit.Servings);
        recipe.Title = (edit.Title ?? string.Empty).Trim();
        recipe.Description = Clean(edit.Description);
        recipe.Servings = servings.Servings;
        recipe.ServingsMax = servings.Estimated ? null : edit.ServingsMax;
        recipe.ServingsEstimated = servings.Estimated;
        recipe.PrepMinutes = edit.PrepMinutes;
        recipe.PrepMinutesMax = edit.PrepMinutes is null ? null : edit.PrepMinutesMax;
        recipe.CookMinutes = edit.CookMinutes;
        recipe.CookMinutesMax = edit.CookMinutes is null ? null : edit.CookMinutesMax;
        recipe.TotalMinutes = edit.TotalMinutes;
        recipe.TotalMinutesMax = edit.TotalMinutes is null ? null : edit.TotalMinutesMax;
        recipe.IsPrecision = edit.IsPrecision;
        recipe.Notes = Clean(edit.Notes);
        recipe.SourceType = edit.SourceType;
        recipe.SourceName = Clean(edit.SourceName);
        recipe.SourceUrl = Clean(edit.SourceUrl);
        recipe.SourceAuthor = Clean(edit.SourceAuthor);
        recipe.Status = PublishRules.Decide(edit.WantsPublish, parsed.Check);
        recipe.UpdatedAt = now;
        recipe.UpdatedById = userId;
        recipe.Version++;

        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Code, u => u.Id, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        IngredientGroup? group = null;
        var order = 0;
        foreach (var line in parsed.Ingredients)
        {
            if (line.IsHeading)
            {
                group = new IngredientGroup { Name = line.GroupHeading!, SortOrder = recipe.IngredientGroups.Count };
                recipe.IngredientGroups.Add(group);
                continue;
            }

            var p = line.Ingredient!;
            recipe.Ingredients.Add(new RecipeIngredient
            {
                Group = group,
                SortOrder = order++,
                RawText = p.RawText,
                Quantity = p.Quantity,
                QuantityMax = p.QuantityMax,
                QuantityText = p.QuantityText,
                UnitId = p.UnitCode is not null && units.TryGetValue(p.UnitCode, out var unitId) ? unitId : null,
                Name = p.Name,
                Preparation = p.Preparation,
                State = p.State,
                IsOptional = p.IsOptional,
                Note = p.Note,
                ParseConfidence = p.Confidence,
            });
        }

        for (var i = 0; i < parsed.Steps.Count; i++)
        {
            recipe.Steps.Add(new RecipeStep { SortOrder = i, Text = parsed.Steps[i].Text, SectionName = parsed.Steps[i].SectionName });
        }

        foreach (var tag in await tags.ResolveAsync(edit.Tags, now, cancellationToken).ConfigureAwait(false))
        {
            recipe.Tags.Add(new RecipeTag { TagId = tag.Id, Tag = tag });
        }
    }

    private async Task<HashSet<string>> TakenSlugsAsync(string baseSlug, int? exceptRecipeId, CancellationToken cancellationToken)
    {
        var recipes = await db.Recipes.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Slug.StartsWith(baseSlug) && r.Id != exceptRecipeId)
            .Select(r => r.Slug).ToListAsync(cancellationToken).ConfigureAwait(false);
        var aliases = await db.RecipeSlugAliases.AsNoTracking()
            .Where(a => a.Slug.StartsWith(baseSlug) && a.RecipeId != exceptRecipeId)
            .Select(a => a.Slug).ToListAsync(cancellationToken).ConfigureAwait(false);
        return recipes.Concat(aliases).ToHashSet(StringComparer.Ordinal);
    }

    private static IQueryable<Recipe> LoadAggregate(IQueryable<Recipe> recipes) => recipes
        .Include(r => r.IngredientGroups)
        .Include(r => r.Ingredients).ThenInclude(i => i.Group)
        .Include(r => r.Ingredients).ThenInclude(i => i.Unit)
        .Include(r => r.Steps)
        .Include(r => r.Tags).ThenInclude(t => t.Tag)
        .AsSplitQuery();

    private static SaveOutcome Saved(Recipe recipe, PublishCheck check) => new()
    {
        Status = SaveStatus.Saved,
        Id = recipe.Id,
        Slug = recipe.Slug,
        RecipeStatus = recipe.Status,
        Version = recipe.Version,
        PublishProblems = check.Problems,
    };

    private static SaveOutcome Conflict(RecipeEdit mine, Recipe theirs) => new()
    {
        Status = SaveStatus.Conflict,
        Id = theirs.Id,
        Slug = theirs.Slug,
        RecipeStatus = theirs.Status,
        Version = theirs.Version,
        Differences = RecipeDiff.Compare(Snapshot(mine), Snapshot(ToEdit(theirs))),
    };

    private static RecipeSnapshot Snapshot(RecipeEdit e) => new()
    {
        Title = e.Title?.Trim(),
        Description = Clean(e.Description),
        Servings = e.Servings,
        ServingsMax = e.ServingsMax,
        PrepMinutes = e.PrepMinutes,
        PrepMinutesMax = e.PrepMinutesMax,
        CookMinutes = e.CookMinutes,
        CookMinutesMax = e.CookMinutesMax,
        TotalMinutes = e.TotalMinutes,
        TotalMinutesMax = e.TotalMinutesMax,
        IsPrecision = e.IsPrecision,
        Notes = Clean(e.Notes),
        SourceType = e.SourceType,
        SourceName = Clean(e.SourceName),
        SourceUrl = Clean(e.SourceUrl),
        SourceAuthor = Clean(e.SourceAuthor),
        Ingredients = Lines(e.IngredientsText),
        Steps = Lines(e.StepsText),
        Tags = e.Tags,
    };

    private static string[] Lines(string? text) =>
        (text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

    private static string? Clean(string? text)
    {
        var trimmed = text?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static RecipeView ToView(Recipe r, IReadOnlyList<CollectionLink> collections)
    {
        var groups = new List<IngredientGroupView>();
        foreach (var chunk in ChunkBy(r.Ingredients.OrderBy(i => i.SortOrder), i => i.GroupId))
        {
            groups.Add(new IngredientGroupView(chunk[0].Group?.Name, chunk.Select(ToLine).ToList()));
        }

        var sections = ChunkBy(r.Steps.OrderBy(s => s.SortOrder), s => s.SectionName)
            .Select(c => new StepSectionView(c[0].SectionName, c.Select(s => s.Text).ToList()))
            .ToList();

        var total = r.TotalMinutes is not null
            ? (r.TotalMinutes, r.TotalMinutesMax)
            : TimeText.Sum(r.PrepMinutes, r.PrepMinutesMax, r.CookMinutes, r.CookMinutesMax);
        return new RecipeView
        {
            Id = r.Id,
            Slug = r.Slug,
            Title = r.Title,
            Description = r.Description,
            Servings = r.Servings,
            ServingsMax = r.ServingsMax,
            ServingsEstimated = r.ServingsEstimated,
            PrepMinutes = r.PrepMinutes,
            PrepMinutesMax = r.PrepMinutesMax,
            CookMinutes = r.CookMinutes,
            CookMinutesMax = r.CookMinutesMax,
            TotalMinutes = total.Item1,
            TotalMinutesMax = total.Item2,
            IsPrecision = r.IsPrecision,
            Status = r.Status,
            SourceType = r.SourceType,
            SourceName = r.SourceName,
            SourceUrl = r.SourceUrl,
            SourceAuthor = r.SourceAuthor,
            Notes = r.Notes,
            Rating = r.Rating,
            Ingredients = groups,
            Steps = sections,
            Tags = r.Tags.Select(t => new TagLink(t.Tag!.Name, t.Tag.Slug, t.Tag.TagType))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Collections = collections,
            UpdatedAt = r.UpdatedAt,
        };
    }

    private static IngredientLineView ToLine(RecipeIngredient i)
    {
        var parsed = new ParsedIngredient
        {
            RawText = i.RawText,
            Quantity = i.Quantity,
            QuantityMax = i.QuantityMax,
            QuantityText = i.QuantityText,
            UnitCode = i.Unit?.Code,
            Name = i.Name,
            Preparation = i.Preparation,
            State = i.State,
            IsOptional = i.IsOptional,
            Note = i.Note,
            Confidence = i.ParseConfidence,
        };
        return new IngredientLineView(IngredientFormatter.Format(parsed), i.RawText, parsed.NeedsReview, i.IsOptional);
    }

    private static List<List<T>> ChunkBy<T, TKey>(IEnumerable<T> items, Func<T, TKey> key)
    {
        var chunks = new List<List<T>>();
        foreach (var item in items)
        {
            if (chunks.Count == 0 || !EqualityComparer<TKey>.Default.Equals(key(chunks[^1][0]), key(item)))
            {
                chunks.Add([]);
            }

            chunks[^1].Add(item);
        }

        return chunks;
    }
}
