using System.Globalization;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Python;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Import;

/// <summary>
/// FR-IMP-12, BR-117, ADR 0059 (M10): when the site reader is asked during a URL or paste import, and how its reading
/// becomes an import. Complete JSON-LD or microdata always wins (AC-111); the reading is plain, bounded text (SEC-22)
/// and the page is credited as usual (BR-10).
/// </summary>
public static partial class SiteReaderRules
{
    public const int MaxLines = 200;
    public const int MaxLineLength = 500;
    public const int MaxTitleLength = 200;

    /// <summary>BR-117: ask the site reader unless JSON-LD or microdata gave a complete reading.</summary>
    public static bool ShouldTry(ImportedRecipe? dotNet) =>
        dotNet is null
        || dotNet.Method is not (ExtractionMethod.JsonLd or ExtractionMethod.Microdata)
        || !ImportCompareRules.IsComplete(dotNet.Title, ImportCompareRules.Lines(dotNet.Ingredients), ImportCompareRules.Lines(dotNet.Steps));

    /// <summary>
    /// BR-117: the site reader's reading as an import, or null when the host is not supported, nothing was found or the
    /// reading is incomplete (then the .NET reading stands).
    /// </summary>
    public static ImportedRecipe? ToImported(SiteRecipeReading site, Uri pageUrl, ImportedRecipe? dotNet)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(pageUrl);
        if (!site.SupportedHost || !site.Found)
        {
            return null;
        }

        var title = Clean(site.Title, MaxTitleLength);
        var ingredients = Lines(site.Ingredients);
        var steps = Lines(site.Instructions);
        if (!ImportCompareRules.IsComplete(title, ingredients.Count, steps.Count))
        {
            return null;
        }

        var host = pageUrl.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? pageUrl.IdnHost[4..] : pageUrl.IdnHost;
        return new ImportedRecipe
        {
            Title = title,
            Description = Clean(site.Description, RecipeEdit.MaxDescriptionLength),
            Servings = Servings(site.Yields),
            PrepTime = Minutes(site.PrepMinutes),
            CookTime = Minutes(site.CookMinutes),
            TotalTime = Minutes(site.TotalMinutes),
            Ingredients = string.Join('\n', ingredients),
            Steps = StepTextRules.Join(steps.Select(s => new StepText(null, s))),
            Tags = dotNet?.Tags ?? [],
            Nutrition = dotNet?.Nutrition,
            SourceType = SourceType.Web,
            SourceUrl = pageUrl.AbsoluteUri,
            SourceName = dotNet?.SourceName ?? Clean(site.SiteName, MaxTitleLength) ?? host,
            SourceAuthor = Clean(site.Author, MaxTitleLength) ?? dotNet?.SourceAuthor,
            ImageUrl = WebAddress(site.ImageUrl) ?? dotNet?.ImageUrl,
            Method = ExtractionMethod.SiteReader,
        };
    }

    /// <summary>FR-IMP-12: how the review page names the way a recipe was read.</summary>
    public static string MethodLabel(ExtractionMethod method) => method switch
    {
        ExtractionMethod.JsonLd => "Recipe data (JSON-LD)",
        ExtractionMethod.Microdata => "Recipe data (microdata)",
        ExtractionMethod.Heuristic => "Page headings (no recipe data)",
        ExtractionMethod.RecipeMarkdown => "recipe-md",
        ExtractionMethod.Headings => "Page headings",
        ExtractionMethod.SiteReader => "Site reader (recipe-scrapers)",
        _ => method.ToString(),
    };

    private static List<string> Lines(IEnumerable<string> items) =>
        items.Select(i => Clean(i, MaxLineLength)).OfType<string>().Take(MaxLines).ToList();

    /// <summary>One line of plain text: control characters and runs of spaces removed, trimmed, cut to length.</summary>
    private static string? Clean(string? text, int max)
    {
        if (text is null)
        {
            return null;
        }

        var flat = Spaces().Replace(new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()), " ").Trim();
        return flat.Length == 0 ? null : flat.Length <= max ? flat : flat[..max].TrimEnd();
    }

    private static string? Servings(string? yields) =>
        yields is not null && LeadingAmount().Match(yields.Trim()) is { Success: true } m ? m.Value.Replace('–', '-').Replace(" ", string.Empty, StringComparison.Ordinal) : null;

    private static string? Minutes(int? minutes) => minutes is > 0 ? minutes.Value.ToString(CultureInfo.InvariantCulture) : null;

    private static string? WebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.AbsoluteUri : null;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^\d{1,3}(\s*[-–]\s*\d{1,3})?(?!\d)")]
    private static partial Regex LeadingAmount();
}
