using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.AiExchange;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Kitchen;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.AiExchange;

/// <summary>The recipes an AI export holds, in recipe-md v1 at 1x (BR-91: recipes only).</summary>
public sealed record AiExportRecipes(IReadOnlyList<string> Titles, IReadOnlyList<string> Markdown, decimal? Servings);

/// <summary>One block of an AI reply, matched to its recipe, with the differences to show (FR-EXP-05).</summary>
public sealed record AiReviewItem(AiReplyBlock Block, RecipeForEdit? Original, IReadOnlyList<string> Problems,
    IReadOnlyList<FieldDifference> Fields, IReadOnlyList<DiffLine> Ingredients, IReadOnlyList<DiffLine> Steps)
{
    public bool CanSave => Original is not null && Problems.Count == 0;
}

/// <summary>How a reviewed block is saved (BR-93: a variant unless an editor chooses to replace the original).</summary>
public enum AiSaveMode
{
    Variant,
    Replace,
}

/// <summary>
/// FR-EXP-04, FR-EXP-05, BR-90 to BR-93, ADR 0033: the data side of the AI exchange. It never calls an AI service; it
/// gathers recipes for an export and turns a pasted reply into a reviewed variant (or, if chosen, an update).
/// </summary>
public sealed class AiExchangeService(AppDbContext db, RecipeService recipes, RecipeExporter exporter, KitchenService kitchen,
    TimeProvider clock)
{
    public const string NoRecipe = "No recipe on this site matches this block (its slug is missing or unknown). Open Import AI response from the recipe's page.";
    public const string Changed = "The original recipe changed since this review. Review the reply again.";

    /// <summary>The recipes with these ids, in order, as recipe-md at 1x; missing ids are skipped.</summary>
    public async Task<AiExportRecipes> RecipesAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var titles = new List<string>();
        var markdown = new List<string>();
        decimal? servings = null;
        foreach (var id in ids)
        {
            var md = await exporter.MarkdownAsync(id, null, cancellationToken).ConfigureAwait(false);
            var row = await db.Recipes.AsNoTracking().Where(r => r.Id == id).Select(r => new { r.Title, r.Servings })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (md is null || row is null)
            {
                continue;
            }

            titles.Add(row.Title);
            markdown.Add(md);
            servings ??= row.Servings;
        }

        return new AiExportRecipes(titles, markdown, titles.Count == 1 ? servings : null);
    }

    /// <summary>BR-91 "include My Kitchen": the active tools by name, nothing else.</summary>
    public async Task<IReadOnlyList<string>> KitchenAsync(CancellationToken cancellationToken = default) =>
        (await kitchen.CurrentAsync(cancellationToken).ConfigureAwait(false)).All.Select(t => t.Name).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// FR-EXP-05, AC-27: every block in the reply, matched to its recipe (by <c>slug:</c>, or the page's recipe when
    /// the reply holds one block) and compared with it.
    /// </summary>
    public async Task<IReadOnlyList<AiReviewItem>> ReviewAsync(string? reply, string? parentSlug, CancellationToken cancellationToken = default)
    {
        var blocks = AiReplyReader.Read(reply);
        var items = new List<AiReviewItem>();
        foreach (var block in blocks)
        {
            var slug = blocks.Count == 1 && SlugRules.IsValid(parentSlug) ? parentSlug : block.Recipe?.Slug;
            var original = await FindAsync(slug, cancellationToken).ConfigureAwait(false);
            var problems = block.Problems.ToList();
            if (original is null && block.Recipe is not null)
            {
                problems.Add(NoRecipe);
            }

            IReadOnlyList<FieldDifference> fields = [];
            IReadOnlyList<DiffLine> ingredients = [];
            IReadOnlyList<DiffLine> steps = [];
            if (original is not null && block.Recipe is { } r)
            {
                var proposed = AiVariantRules.ToEdit(r, original.Edit, Today(), asVariant: false, publish: false);
                fields = RecipeDiff.Compare(Snapshot(original.Edit), Snapshot(proposed))
                    .Where(f => f.Field is not (RecipeField.Ingredients or RecipeField.Steps)).ToList();
                ingredients = AiVariantRules.Lines(AiVariantRules.SplitLines(original.Edit.IngredientsText), AiVariantRules.SplitLines(r.Ingredients));
                steps = AiVariantRules.Lines(AiVariantRules.SplitLines(original.Edit.StepsText), AiVariantRules.SplitLines(r.Steps));
            }

            items.Add(new AiReviewItem(block, original, problems, fields, ingredients, steps));
        }

        return items;
    }

    /// <summary>
    /// BR-92, BR-93, ADR 0033: saves one reviewed block. The reply is read again from the posted text, so what is
    /// saved is what was reviewed. A variant is a new recipe linked to the original; Replace checks the version the
    /// editor reviewed (BR-06).
    /// </summary>
    public async Task<SaveOutcome> SaveAsync(string? reply, int blockIndex, string? parentSlug, AiSaveMode mode, bool publish,
        int expectedVersion, string userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var items = await ReviewAsync(reply, parentSlug, cancellationToken).ConfigureAwait(false);
        if (blockIndex < 0 || blockIndex >= items.Count || !items[blockIndex].CanSave)
        {
            return SaveOutcome.NotFound;
        }

        var item = items[blockIndex];
        var original = item.Original!;
        if (original.Version != expectedVersion)
        {
            return SaveOutcome.NotFound with { Status = SaveStatus.Conflict };
        }

        var edit = AiVariantRules.ToEdit(item.Block.Recipe!, original.Edit, Today(), mode == AiSaveMode.Variant, publish);
        if (mode == AiSaveMode.Replace)
        {
            return await recipes.UpdateAsync(original.Id, edit, expectedVersion, userId, isAdmin, cancellationToken).ConfigureAwait(false);
        }

        var created = await recipes.CreateAsync(edit, userId, cancellationToken).ConfigureAwait(false);
        if (created.Status == SaveStatus.Saved)
        {
            await recipes.SetParentAsync(created.Id, original.Id, cancellationToken).ConfigureAwait(false);
        }

        return created;
    }

    private async Task<RecipeForEdit?> FindAsync(string? slug, CancellationToken cancellationToken)
    {
        if (!SlugRules.IsValid(slug))
        {
            return null;
        }

        var lookup = await recipes.GetForReadingAsync(slug!, canSeeDrafts: true, cancellationToken).ConfigureAwait(false);
        if (lookup.RedirectToSlug is { } current)
        {
            lookup = await recipes.GetForReadingAsync(current, canSeeDrafts: true, cancellationToken).ConfigureAwait(false);
        }

        return lookup.Recipe is { } view ? await recipes.GetForEditAsync(view.Id, cancellationToken).ConfigureAwait(false) : null;
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static RecipeSnapshot Snapshot(RecipeEdit e) => new()
    {
        Title = e.Title,
        Description = e.Description,
        Servings = e.Servings,
        ServingsMax = e.ServingsMax,
        PrepMinutes = e.PrepMinutes,
        PrepMinutesMax = e.PrepMinutesMax,
        CookMinutes = e.CookMinutes,
        CookMinutesMax = e.CookMinutesMax,
        TotalMinutes = e.TotalMinutes,
        TotalMinutesMax = e.TotalMinutesMax,
        IsPrecision = e.IsPrecision,
        Notes = e.Notes,
        SourceType = e.SourceType,
        SourceName = e.SourceName,
        SourceUrl = e.SourceUrl,
        SourceAuthor = e.SourceAuthor,
        Tags = e.Tags,
    };
}
