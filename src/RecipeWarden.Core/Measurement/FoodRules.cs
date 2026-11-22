namespace RecipeWarden.Core.Measurement;

/// <summary>
/// FR-MEAS-05: what an editor may enter on /manage/conversions. Values outside these ranges are typos (a density of
/// 53 instead of 0.53), and a wrong value would silently give wrong amounts, so they are refused with a message.
/// </summary>
public static class FoodRules
{
    public const int MaxNameLength = 100;
    public const int MaxBasisLength = 200;
    public const int MaxAliases = 30;

    /// <summary>g/ml. Herbs are about 0.07 (cilantro); syrups about 1.44 (corn syrup); salt 1.22.</summary>
    public const decimal MinDensity = 0.01m;
    public const decimal MaxDensity = 3m;

    /// <summary>Vague amounts, in teaspoons: from a drop to a tablespoon.</summary>
    public const decimal MinVagueTsp = 0.005m;
    public const decimal MaxVagueTsp = 3m;

    /// <summary>Grams bought per gram prepared: 1 (no loss) to 10 (e.g. dried mushrooms rehydrated, the other way round).</summary>
    public const decimal MinRatio = 0.1m;
    public const decimal MaxRatio = 10m;

    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a name";
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return $"Names are limited to {MaxNameLength} characters";
        }

        return FoodMatcher.Normalize(name).Length == 0 ? "Enter a name with letters or numbers" : null;
    }

    public static string? ValidateDensity(decimal? density) =>
        density is null || (density >= MinDensity && density <= MaxDensity)
            ? null
            : $"Density must be between {MinDensity} and {MaxDensity} g/ml (water is 1, flour about 0.53)";

    public static string? ValidateVagueTsp(decimal tsp) =>
        tsp >= MinVagueTsp && tsp <= MaxVagueTsp ? null : $"Enter between {MinVagueTsp} and {MaxVagueTsp} tsp";

    public static string? ValidateYield(decimal ratio, decimal ratioMax, decimal? preparedDensity, string? basis)
    {
        if (ratio < MinRatio || ratioMax > MaxRatio || ratio > ratioMax)
        {
            return $"The range must run low to high, between {MinRatio} and {MaxRatio}";
        }

        if (ValidateDensity(preparedDensity) is { } densityError)
        {
            return densityError;
        }

        if (string.IsNullOrWhiteSpace(basis))
        {
            return "Say what the estimate is based on (BR-26), e.g. \"boneless breast, simmered\"";
        }

        return basis.Length > MaxBasisLength ? $"The basis is limited to {MaxBasisLength} characters" : null;
    }

    /// <summary>Comma-separated aliases, trimmed, without duplicates or the food's own name.</summary>
    public static IReadOnlyList<string> ParseAliases(string? text, string foodName)
    {
        var own = FoodMatcher.Normalize(foodName);
        return (text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => a.Length <= MaxNameLength && FoodMatcher.Normalize(a).Length > 0 && FoodMatcher.Normalize(a) != own)
            .DistinctBy(FoodMatcher.Normalize, StringComparer.Ordinal)
            .Take(MaxAliases)
            .ToList();
    }
}
