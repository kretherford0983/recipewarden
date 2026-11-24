using RecipeWarden.Core.Python;

namespace RecipeWarden.Core.Import;

/// <summary>How a saved URL import compares between the .NET extractor and a site reader (AC-109, D6).</summary>
public enum CompareVerdict
{
    /// <summary>Both readings are complete: the site reader would change nothing (BR-117 keeps .NET).</summary>
    BothComplete,

    /// <summary>Only the site reader is complete: M10 would have helped.</summary>
    OnlySiteReader,

    /// <summary>Only the .NET reading is complete.</summary>
    OnlyDotNet,

    /// <summary>Neither is complete.</summary>
    Neither,

    /// <summary>The host has no site reader, and its schema data gave nothing complete.</summary>
    NoSiteReader,
}

/// <summary>One saved URL import, read both ways (AC-109).</summary>
public sealed record ImportComparison(string Url, ExtractionMethod? DotNetMethod, bool SupportedHost, bool DotNetComplete, bool SiteComplete,
    int DotNetIngredients, int SiteIngredients, int DotNetSteps, int SiteSteps, CompareVerdict Verdict);

/// <summary>
/// BR-117 (M10) and AC-109 (M9): a reading is complete with a title, at least one ingredient and at least one step. The
/// comparison only measures; it changes no import (D6: M10 is switched on only if the numbers show a gain).
/// </summary>
public static class ImportCompareRules
{
    public static bool IsComplete(string? title, int ingredients, int steps) =>
        !string.IsNullOrWhiteSpace(title) && ingredients >= 1 && steps >= 1;

    /// <summary>Non-blank lines of an imported text field (ingredients or steps).</summary>
    public static int Lines(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));

    public static ImportComparison Compare(string url, ImportedRecipe? ours, SiteRecipeReading? site)
    {
        ArgumentNullException.ThrowIfNull(url);
        var oursIngredients = Lines(ours?.Ingredients);
        var oursSteps = Lines(ours?.Steps);
        var dotNet = ours is not null && IsComplete(ours.Title, oursIngredients, oursSteps);
        var siteIngredients = site is { Found: true } ? site.Ingredients.Count : 0;
        var siteSteps = site is { Found: true } ? site.Instructions.Count : 0;
        var siteComplete = site is { Found: true } && IsComplete(site.Title, siteIngredients, siteSteps);
        var supported = site?.SupportedHost ?? false;
        var verdict = (dotNet, siteComplete) switch
        {
            (true, true) => CompareVerdict.BothComplete,
            (false, true) when supported => CompareVerdict.OnlySiteReader,
            (true, false) => CompareVerdict.OnlyDotNet,
            _ when !supported => CompareVerdict.NoSiteReader,
            _ => CompareVerdict.Neither,
        };
        return new ImportComparison(url, ours?.Method, supported, dotNet, siteComplete, oursIngredients, siteIngredients, oursSteps, siteSteps, verdict);
    }
}
