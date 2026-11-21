namespace RecipeWarden.Core.Recipes;

/// <summary>Outcome of <see cref="ServingsRules.Resolve"/>.</summary>
public sealed record ServingsResult(bool IsValid, decimal Servings, bool Estimated, string? Error);

/// <summary>BR-03: servings must be positive; unknown servings default to 4 and are flagged "servings estimated".</summary>
public static class ServingsRules
{
    public const decimal Default = 4m;
    public const decimal Maximum = 1000m;
    public const string NotPositive = "Servings must be a positive number";
    public const string TooMany = "Servings must be 1000 or fewer";

    public static ServingsResult Resolve(decimal? servings) => servings switch
    {
        null => new ServingsResult(true, Default, true, null),
        <= 0m => new ServingsResult(false, 0m, false, NotPositive),
        > Maximum => new ServingsResult(false, 0m, false, TooMany),
        _ => new ServingsResult(true, servings.Value, false, null),
    };
}
