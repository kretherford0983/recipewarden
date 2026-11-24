using RecipeWarden.Core.Python;

namespace RecipeWarden.Core.Nutrition;

/// <summary>
/// BR-116, FR-NUT-07: which of the matcher's candidates may be shown. Only a candidate whose FDC id is a local,
/// non-removed food, with confidence at least 0.75; the most confident first (the matcher's order breaks ties). It is
/// never applied without an Editor, and the mapping precedence of BR-54 is unchanged.
/// </summary>
public static class FoodSuggestionRules
{
    public const decimal MinConfidence = 0.75m;

    /// <summary>Names sent to the worker per "Suggest matches" (plan 4.4).</summary>
    public const int MaxNamesPerRequest = 50;

    public static FdcCandidate? Pick(IEnumerable<FdcCandidate> candidates, Func<int, bool> isLocalFood)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(isLocalFood);
        return candidates
            .Where(c => c.Confidence is >= MinConfidence and <= 1m && isLocalFood(c.FdcId))
            .OrderByDescending(c => c.Confidence)
            .FirstOrDefault();
    }

    /// <summary>A dismissal holds for the same name and engine version (BR-116); a new engine may suggest again.</summary>
    public static bool IsDismissed(string? dismissedForEngine, string engine) =>
        dismissedForEngine is not null && string.Equals(dismissedForEngine, engine, StringComparison.Ordinal);
}
