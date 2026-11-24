using System.Text;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Importers.Import;

/// <summary>Recipes read from one source, or why none could be read.</summary>
public sealed record ReadResult(IReadOnlyList<ImportedRecipe> Recipes, string? Error)
{
    public static ReadResult Fail(string error) => new([], error);
}

/// <summary>
/// The one entry point the import service uses (9.3 Extract and Normalize): a fetched page, pasted text or HTML
/// (ADR 0024), or a validated upload (FR-IMP-04, FR-IMP-05). Also builds the review warnings (FR-IMP-06, BR-04)
/// and the ingredient names used for duplicate detection (FR-IMP-07).
/// </summary>
public static class ImportReader
{
    public const string NoRecipe = "No recipe was found. Check the page has ingredients and steps, or paste the recipe text instead.";
    public const string NoRecipeInFile = "No recipe was found in the file. It needs an Ingredients and a Steps (or Directions) heading.";

    public static ReadResult ReadPage(string html, Uri pageUrl)
    {
        var recipe = HtmlRecipeExtractor.Extract(html, pageUrl);
        return recipe is null ? ReadResult.Fail(NoRecipe) : new ReadResult([recipe], null);
    }

    /// <summary>Pasted page text or saved HTML; the address, when given, is kept as the source (BR-10).</summary>
    public static ReadResult ReadPaste(string text, Uri? pageUrl)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (LooksLikeHtml(text))
        {
            var fromHtml = HtmlRecipeExtractor.Extract(text, pageUrl);
            return fromHtml is null ? ReadResult.Fail(NoRecipe) : new ReadResult([fromHtml], null);
        }

        // Pasted recipe-md (an export, or an AI reply's block) keeps every field, nutrition included (ADR 0035).
        var recipes = RecipeMarkdown.IsRecipeMarkdown(text)
            ? RecipeMarkdown.Read(text).Where(r => r.HasContent).ToList()
            : TextRecipeExtractor.Extract(text);
        if (recipes.Count == 0)
        {
            return ReadResult.Fail(NoRecipe);
        }

        return new ReadResult(recipes.Select(r => pageUrl is null ? r : r with
        {
            SourceType = SourceType.Web,
            SourceUrl = pageUrl.AbsoluteUri,
            SourceName = pageUrl.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? pageUrl.IdnHost[4..] : pageUrl.IdnHost,
        }).ToList(), null);
    }

    /// <summary>A file that passed the upload validator (SEC-50, SEC-51).</summary>
    public static ReadResult ReadFile(UploadKind kind, byte[] bytes, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var fallback = FallbackTitle(fileName);
        IReadOnlyList<ImportedRecipe> recipes;
        switch (kind)
        {
            case UploadKind.Markdown or UploadKind.Text:
                if (!UploadRules.IsText(bytes))
                {
                    return ReadResult.Fail(UploadRules.NotText);
                }

                var text = new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');
                recipes = RecipeMarkdown.IsRecipeMarkdown(text)
                    ? RecipeMarkdown.Read(text).Where(r => r.HasContent).ToList()
                    : TextRecipeExtractor.Extract(text, fallback);
                break;
            case UploadKind.Docx:
                using (var stream = new MemoryStream(bytes, writable: false))
                {
                    var (docx, error) = DocxRecipeExtractor.Extract(stream, fallback);
                    if (error is not null)
                    {
                        return ReadResult.Fail(error);
                    }

                    recipes = docx;
                }

                break;
            case UploadKind.Pdf:
                var (pdf, pdfError) = PdfRecipeExtractor.Extract(bytes, fallback);
                if (pdfError is not null)
                {
                    return ReadResult.Fail(pdfError);
                }

                recipes = pdf;
                break;
            default:
                return ReadResult.Fail(UploadRules.NotAllowed);
        }

        if (recipes.Count == 0)
        {
            return ReadResult.Fail(NoRecipeInFile);
        }

        // A file's recipes are File-sourced unless recipe-md says otherwise.
        return new ReadResult(recipes.Select(r => r with { SourceType = r.SourceType ?? SourceType.File }).ToList(), null);
    }

    public static IReadOnlyList<ImportWarning> Warnings(ImportedRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var parses = IngredientLineParser.ParseList(recipe.Ingredients)
            .Where(l => l.Ingredient is not null)
            .Select(l => (l.Ingredient!.RawText, (double)l.Ingredient.Confidence));
        return ImportRules.Warnings(recipe, parses);
    }

    /// <summary>
    /// FR-IMP-11: the .NET parse of each ingredient line (headings left out), in order, at most 500, for the second
    /// opinion. A line longer than the worker accepts is sent blank and so gets no second reading.
    /// </summary>
    public static IReadOnlyList<ParsedIngredient> IngredientParses(string? ingredients) =>
        IngredientLineParser.ParseList(ingredients).Where(l => l.Ingredient is not null).Select(l => l.Ingredient!).Take(500).ToList();

    public const int MaxSecondOpinionLineLength = 300;

    /// <summary>FR-IMP-09: whether the rules' result is weak enough to try the AI parser.</summary>
    public static bool NeedsAi(ImportedRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var lines = IngredientLineParser.ParseList(recipe.Ingredients).Where(l => l.Ingredient is not null).ToList();
        var low = lines.Count(l => (double)l.Ingredient!.Confidence < ImportRules.ReviewConfidence);
        return Core.AiParser.AiParserRules.ShouldUseAi(recipe, lines.Count, low);
    }

    public static IReadOnlyList<string> IngredientNames(string? ingredients) =>
        IngredientLineParser.ParseList(ingredients)
            .Where(l => l.Ingredient is not null && !string.IsNullOrWhiteSpace(l.Ingredient.Name))
            .Select(l => l.Ingredient!.Name!)
            .ToList();

    public static string? FallbackTitle(string? fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty).Replace('_', ' ').Replace('-', ' ').Trim();
        return name.Length == 0 ? null : ImportText.Line(name, 200);
    }

    private static bool LooksLikeHtml(string text)
    {
        var head = text.Length > 4000 ? text[..4000] : text;
        return head.Contains("<html", StringComparison.OrdinalIgnoreCase) || head.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<!doctype", StringComparison.OrdinalIgnoreCase) || head.Contains("<div", StringComparison.OrdinalIgnoreCase)
            || head.Contains("application/ld+json", StringComparison.OrdinalIgnoreCase) || head.Contains("<li", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>FR-IMP-09: the plain text the AI parser reads from a stored raw payload (never HTML, BR-13).</summary>
public static class ImportPlainText
{
    public static string? From(byte[] raw, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext == ".pdf" || (raw.Length > 4 && raw[0] == (byte)'%' && raw[1] == (byte)'P' && raw[2] == (byte)'D' && raw[3] == (byte)'F'))
        {
            return PdfRecipeExtractor.ExtractText(raw).Text;
        }

        if (ext == ".docx" || (raw.Length > 4 && raw[0] == (byte)'P' && raw[1] == (byte)'K'))
        {
            using var stream = new MemoryStream(raw, writable: false);
            return DocxRecipeExtractor.ExtractText(stream).Text;
        }

        var text = new System.Text.UTF8Encoding(false).GetString(raw).TrimStart('﻿');
        return LooksLikeHtml(text) ? HtmlText.PageText(text) : text;
    }

    private static bool LooksLikeHtml(string text)
    {
        var head = text.Length > 2000 ? text[..2000] : text;
        return head.Contains("<html", StringComparison.OrdinalIgnoreCase) || head.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<div", StringComparison.OrdinalIgnoreCase) || head.Contains("<!doctype", StringComparison.OrdinalIgnoreCase);
    }
}
