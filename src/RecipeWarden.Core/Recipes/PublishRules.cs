namespace RecipeWarden.Core.Recipes;

/// <summary>A reason a recipe cannot be published (or, in <see cref="PublishCheck.Errors"/>, cannot be saved).</summary>
public sealed record PublishProblem(RecipeField Field, string Message);

/// <summary>Outcome of <see cref="PublishRules.Check"/>.</summary>
public sealed record PublishCheck(IReadOnlyList<PublishProblem> Problems, IReadOnlyList<PublishProblem> Errors)
{
    /// <summary>False when a field is invalid outright; nothing may be saved.</summary>
    public bool CanSave => Errors.Count == 0;

    /// <summary>True when the recipe may be saved as Published (BR-01).</summary>
    public bool CanPublish => CanSave && Problems.Count == 0;
}

/// <summary>
/// BR-01: a published recipe needs a title (1-200 characters), at least one ingredient and at least one step.
/// Anything less saves as Draft (AC-02). A title over 200 characters is invalid even for a draft.
/// </summary>
public static class PublishRules
{
    public const int MaxTitleLength = 200;

    public const string TitleMissing = "Add a title to publish";
    public const string TitleTooLong = "Title must be 200 characters or fewer";
    public const string IngredientsMissing = "Add at least one ingredient to publish";
    public const string StepsMissing = "Add at least one step to publish";

    public static PublishCheck Check(string? title, int ingredientCount, int stepCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ingredientCount);
        ArgumentOutOfRangeException.ThrowIfNegative(stepCount);

        var problems = new List<PublishProblem>();
        var errors = new List<PublishProblem>();
        var trimmed = title?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            problems.Add(new PublishProblem(RecipeField.Title, TitleMissing));
        }
        else if (trimmed.Length > MaxTitleLength)
        {
            errors.Add(new PublishProblem(RecipeField.Title, TitleTooLong));
        }

        if (ingredientCount == 0)
        {
            problems.Add(new PublishProblem(RecipeField.Ingredients, IngredientsMissing));
        }

        if (stepCount == 0)
        {
            problems.Add(new PublishProblem(RecipeField.Steps, StepsMissing));
        }

        return new PublishCheck(problems, errors);
    }

    /// <summary>The status to save with: Published only when asked for and allowed.</summary>
    public static RecipeStatus Decide(bool wantsPublish, PublishCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!check.CanSave)
        {
            throw new InvalidOperationException("The recipe has invalid fields and cannot be saved.");
        }

        return wantsPublish && check.CanPublish ? RecipeStatus.Published : RecipeStatus.Draft;
    }
}
