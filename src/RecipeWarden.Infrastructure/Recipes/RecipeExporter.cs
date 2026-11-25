using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Media;

namespace RecipeWarden.Infrastructure.Recipes;

/// <summary>
/// FR-EXP-02/03, AC-25, AC-12, ADR 0031: recipe-md v1 export, alone or in a .zip with its images, and bulk export.
/// A scaled view exports its scaled lines and servings with <c>scale:</c>. In a bulk zip each recipe has its own
/// folder (<c>{slug}/{slug}.md</c>, <c>{slug}/images/…</c>) so the relative image links are the same as in a single export.
/// </summary>
public sealed class RecipeExporter(AppDbContext db, RecipeService recipes, ImageStore images, Nutrition.NutritionService nutrition)
{
    public const string FinishImage = "images/finish.webp";

    public static string StepImage(int number) => "images/step-" + number.ToString("00", CultureInfo.InvariantCulture) + ".webp";

    /// <summary>The recipe as recipe-md; null when it does not exist.</summary>
    public async Task<string?> MarkdownAsync(int id, ScaledRecipe? scaled, CancellationToken cancellationToken = default)
    {
        var export = await LoadAsync(id, scaled, withImages: false, cancellationToken).ConfigureAwait(false);
        return export?.Markdown;
    }

    /// <summary>One recipe's .md and its images (AC-25). False when the recipe does not exist.</summary>
    public async Task<bool> WriteZipAsync(Stream output, int id, ScaledRecipe? scaled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var export = await LoadAsync(id, scaled, withImages: true, cancellationToken).ConfigureAwait(false);
        if (export is null)
        {
            return false;
        }

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await AddAsync(zip, string.Empty, export, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// FR-EXP-03: the recipes a bulk export holds. Readers get published recipes; editors drafts too (ADR 0031).
    /// With <paramref name="collectionSlug"/>, only that collection (null when it does not exist).
    /// </summary>
    public async Task<IReadOnlyList<int>?> BulkIdsAsync(bool canSeeDrafts, string? collectionSlug, CancellationToken cancellationToken = default)
    {
        var visible = db.Recipes.AsNoTracking().Where(r => r.DeletedAt == null);
        if (!canSeeDrafts)
        {
            visible = visible.Where(r => r.Status == RecipeStatus.Published);
        }

        if (collectionSlug is null)
        {
            return await visible.OrderBy(r => r.Slug).Select(r => r.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var collection = await db.Collections.AsNoTracking().Where(c => c.Slug == collectionSlug).Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (collection is null)
        {
            return null;
        }

        return await (from cr in db.CollectionRecipes
                      join r in visible on cr.RecipeId equals r.Id
                      where cr.CollectionId == collection
                      orderby cr.SortOrder, r.Slug
                      select r.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>FR-EXP-03: every recipe in <paramref name="ids"/> at 1x, with images, one folder each.</summary>
    public async Task WriteBulkZipAsync(Stream output, IReadOnlyList<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(ids);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var id in ids)
        {
            var export = await LoadAsync(id, null, withImages: true, cancellationToken).ConfigureAwait(false);
            if (export is not null)
            {
                await AddAsync(zip, export.Slug + "/", export, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed record Export(string Slug, string Markdown, IReadOnlyList<(string Path, string File)> Images);

    private async Task<Export?> LoadAsync(int id, ScaledRecipe? scaled, bool withImages, CancellationToken cancellationToken)
    {
        var recipe = await recipes.GetForEditAsync(id, cancellationToken).ConfigureAwait(false);
        if (recipe is null)
        {
            return null;
        }

        var imported = await db.Recipes.AsNoTracking().Where(r => r.Id == id).Select(r => r.SourceImportedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var edit = recipe.Edit;
        decimal? factor = null;
        if (scaled is not null && scaled.Factor != 1m)
        {
            factor = scaled.Factor;
            edit = edit with
            {
                IngredientsText = string.Join('\n', scaled.Ingredients.SelectMany(g =>
                    (g.Name is null ? [] : new[] { g.Name + ":" }).Concat(g.Lines.Select(l => l.Text)))),
                Servings = scaled.Servings,
                ServingsMax = scaled.ServingsMax,
            };
        }

        var files = new List<(string Path, string File)>();
        string? finish = null;
        var steps = new List<string?>();
        if (withImages)
        {
            if (await images.FindAsync(recipe.FinishImageId, thumb: false, canSeeAll: true, cancellationToken).ConfigureAwait(false) is { } f)
            {
                finish = FinishImage;
                files.Add((FinishImage, f.Path));
            }

            foreach (var photo in recipe.StepPhotos)
            {
                var file = await images.FindAsync(photo.ImageId, thumb: false, canSeeAll: true, cancellationToken).ConfigureAwait(false);
                var path = file is null ? null : StepImage(photo.Index + 1);
                steps.Add(path);
                if (file is not null)
                {
                    files.Add((path!, file.Path));
                }
            }
        }

        if (withImages)
        {
            // ADR 0058 D7: card photos travel with the recipe as card-1.webp, card-2.webp, … (not part of recipe-md v1).
            var cards = await db.RecipeCardImages.AsNoTracking().Where(c => c.RecipeId == id).OrderBy(c => c.Position).Select(c => c.ImageId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < cards.Count; i++)
            {
                if (await images.FindAsync(cards[i], thumb: false, canSeeAll: true, cancellationToken).ConfigureAwait(false) is { } card)
                {
                    files.Add((string.Create(System.Globalization.CultureInfo.InvariantCulture, $"card-{i + 1}.webp"), card.Path));
                }
            }
        }

        var date = imported is { } at ? DateOnly.FromDateTime(at.UtcDateTime) : (DateOnly?)null;

        // ADR 0035/0036: allergens and the nutrition panel, as the recipe page shows them (per serving at any scale).
        RecipeMdExtras? extras = null;
        if (await nutrition.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } n)
        {
            extras = new RecipeMdExtras(
                n.Allergens.Keys.Order().Select(Core.Search.SearchCriteria.AllergenQuery).ToList(),
                n.Panel?.PerServing, n.Panel?.Source.ToString().ToLowerInvariant(), n.Panel?.CompletenessPct);
        }

        var markdown = RecipeMarkdown.Write(edit, recipe.Slug, date, factor, finish, steps, extras);
        return new Export(recipe.Slug, markdown, files);
    }

    private static async Task AddAsync(ZipArchive zip, string folder, Export export, CancellationToken cancellationToken)
    {
        var md = zip.CreateEntry(folder + export.Slug + ".md", CompressionLevel.Optimal);
        await using (var writer = new StreamWriter(md.Open(), new System.Text.UTF8Encoding(false)))
        {
            await writer.WriteAsync(export.Markdown.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        foreach (var (path, file) in export.Images)
        {
            // WebP is already compressed.
            var entry = zip.CreateEntry(folder + path, CompressionLevel.NoCompression);
            await using var target = entry.Open();
            await using var source = File.OpenRead(file);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }
}
