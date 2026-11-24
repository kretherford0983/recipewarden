using System.Globalization;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Python;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Import;

/// <summary>
/// Something the reviewer should look at before saving (FR-IMP-06). <see cref="Kind"/> is
/// <see cref="SecondOpinionKind"/> for a line the NLP parser read differently (FR-IMP-11); null otherwise.
/// </summary>
public sealed record ImportWarning(string Message, string? Line = null, string? Kind = null)
{
    public const string SecondOpinionKind = "second-opinion";

    public bool IsSecondOpinion => Kind == SecondOpinionKind;
}

/// <summary>An existing recipe that looks like the one being imported (FR-IMP-07, AC-16).</summary>
public sealed record DuplicateCandidate(int RecipeId, string Title, string? SourceUrl, IReadOnlyList<string> IngredientNames);

/// <summary>
/// FR-IMP-01, FR-IMP-06, FR-IMP-07, BR-04: rules shared by every importer — times and yields from schema.org values,
/// the review warnings, and duplicate detection (same source address, or same title with more than 80% of the
/// ingredients in common).
/// </summary>
public static partial class ImportRules
{
    public const double ReviewConfidence = 0.8;
    public const double DuplicateOverlap = 0.8;

    public const string NoTitle = "No title was found; add one before publishing";
    public const string NoIngredients = "No ingredients were found";
    public const string NoSteps = "No steps were found";
    public const string CheckLine = "Check this ingredient line";

    [GeneratedRegex(@"^P(?:(?<d>\d+(?:\.\d+)?)D)?(?:T(?:(?<h>\d+(?:\.\d+)?)H)?(?:(?<m>\d+(?:\.\d+)?)M)?(?:(?<s>\d+(?:\.\d+)?)S)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex IsoDuration();

    [GeneratedRegex(@"^(?:serves|servings?|makes|yield|yields)?\s*:?\s*(?<a>\d+(?:\.\d+)?)(?:\s*(?:-|–|to)\s*(?<b>\d+(?:\.\d+)?))?\s*(?:servings?|serves|people|persons|portions)?\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ServingsYield();

    /// <summary>"PT1H15M" → "75"; null for zero, missing or unreadable values. Plain text ("20 min") is passed on.</summary>
    public static string? Minutes(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var m = IsoDuration().Match(text);
        if (m.Success && text.Length > 1)
        {
            double Part(string g) => m.Groups[g].Success ? double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
            var minutes = (Part("d") * 24 * 60) + (Part("h") * 60) + Part("m") + (Part("s") / 60);
            var rounded = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
            return rounded is > 0 and <= TimeText.MaxMinutes ? rounded.ToString(CultureInfo.InvariantCulture) : null;
        }

        var parsed = TimeText.Parse(text);
        return parsed.Error is null && parsed.Min is not null ? TimeText.Format(parsed.Min, parsed.Max) : null;
    }

    /// <summary>
    /// schema.org recipeYield is free text: "4", "Serves 6-8", "4 servings", "12 fl oz", "24 cookies". Servings and
    /// finished amounts are used; anything else becomes a remark the reviewer sees (and goes into the notes).
    /// </summary>
    public static (string? Servings, string? Yield, string? Remark) ServingsAndYield(IEnumerable<string?> values)
    {
        string? servings = null;
        string? yield = null;
        string? remark = null;
        foreach (var raw in values)
        {
            var text = ImportText.Line(raw, 100);
            if (text is null)
            {
                continue;
            }

            var m = ServingsYield().Match(text);
            if (m.Success)
            {
                servings ??= m.Groups["b"].Success ? $"{m.Groups["a"].Value}-{m.Groups["b"].Value}" : m.Groups["a"].Value;
                continue;
            }

            var amount = text.StartsWith("makes ", StringComparison.OrdinalIgnoreCase) ? text[6..] : text;
            if (yield is null && AmountText.TryParse(amount, out _))
            {
                yield = amount;
                continue;
            }

            remark ??= "Makes " + (text.StartsWith("makes ", StringComparison.OrdinalIgnoreCase) ? text[6..] : text);
        }

        return (servings, yield, remark);
    }

    /// <summary>
    /// The review warnings (FR-IMP-06): missing parts (BR-01) and ingredient lines whose parse confidence is below
    /// 0.8 (BR-04).
    /// </summary>
    public static IReadOnlyList<ImportWarning> Warnings(ImportedRecipe recipe, IEnumerable<(string Line, double Confidence)> ingredientParses)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(ingredientParses);
        var warnings = new List<ImportWarning>();
        if (string.IsNullOrWhiteSpace(recipe.Title))
        {
            warnings.Add(new ImportWarning(NoTitle));
        }

        if (string.IsNullOrWhiteSpace(recipe.Ingredients))
        {
            warnings.Add(new ImportWarning(NoIngredients));
        }

        if (string.IsNullOrWhiteSpace(recipe.Steps))
        {
            warnings.Add(new ImportWarning(NoSteps));
        }

        warnings.AddRange(ingredientParses
            .Where(p => p.Confidence < ReviewConfidence)
            .Select(p => new ImportWarning(CheckLine, p.Line)));
        warnings.AddRange(recipe.Remarks.Select(r => new ImportWarning(r)));
        return warnings;
    }

    /// <summary>
    /// FR-IMP-11, BR-118, D4: a review warning for each line the second parser read differently. The line and the .NET
    /// parse are unchanged; the warning only shows the other reading. <paramref name="readings"/> is in line order.
    /// </summary>
    public static IReadOnlyList<ImportWarning> SecondOpinionWarnings(IReadOnlyList<ParsedIngredient> parses, IReadOnlyList<IngredientReading?> readings)
    {
        ArgumentNullException.ThrowIfNull(parses);
        ArgumentNullException.ThrowIfNull(readings);
        if (parses.Count != readings.Count)
        {
            return [];
        }

        var warnings = new List<ImportWarning>();
        for (var i = 0; i < parses.Count; i++)
        {
            if (SecondOpinionRules.Compare(parses[i], readings[i]) is { } opinion)
            {
                warnings.Add(new ImportWarning(opinion.Summary, parses[i].RawText, ImportWarning.SecondOpinionKind));
            }
        }

        return warnings;
    }

    /// <summary>
    /// The address used to spot the same page imported twice: lower-case host without "www.", no fragment, no
    /// default port, no tracking parameters (utm_*, fbclid, gclid, mc_*), no trailing slash, scheme ignored.
    /// </summary>
    public static string? DuplicateKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p =>
            {
                var name = p.Split('=')[0].ToLowerInvariant();
                return !(name.StartsWith("utm_", StringComparison.Ordinal) || name is "fbclid" or "gclid" or "msclkid"
                    || name.StartsWith("mc_", StringComparison.Ordinal));
            })
            .Order(StringComparer.Ordinal)
            .ToList();
        var path = uri.AbsolutePath.TrimEnd('/');
        return host + path + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
    }

    /// <summary>Shared ingredients as a share of the larger list (0 when either list is empty).</summary>
    public static double IngredientOverlap(IEnumerable<string> a, IEnumerable<string> b)
    {
        var left = a.Select(NameRules.Normalize).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
        var right = b.Select(NameRules.Normalize).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        return (double)left.Intersect(right).Count() / Math.Max(left.Count, right.Count);
    }

    /// <summary>
    /// FR-IMP-07: the first existing recipe with the same source address, else one with the same title (ignoring
    /// case and spacing) and more than 80% of its ingredients in common.
    /// </summary>
    public static DuplicateCandidate? FindDuplicate(string? title, string? sourceUrl, IReadOnlyCollection<string> ingredientNames, IEnumerable<DuplicateCandidate> existing)
    {
        ArgumentNullException.ThrowIfNull(ingredientNames);
        var list = existing.ToList();
        var key = DuplicateKey(sourceUrl);
        if (key is not null)
        {
            var byUrl = list.FirstOrDefault(e => DuplicateKey(e.SourceUrl) == key);
            if (byUrl is not null)
            {
                return byUrl;
            }
        }

        var name = NameRules.Normalize(title);
        if (name.Length == 0)
        {
            return null;
        }

        return list.FirstOrDefault(e => NameRules.Normalize(e.Title) == name
            && IngredientOverlap(ingredientNames, e.IngredientNames) > DuplicateOverlap);
    }
}
