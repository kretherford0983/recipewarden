using RecipeWarden.Core.Organization;

namespace RecipeWarden.Core.Recipes;

/// <summary>
/// What an Editor can change on a recipe (SEC-13: only these fields; never Id, slug, status, owner or audit fields).
/// Ingredients and steps arrive as text, one per line, and are parsed on save so the stored form always matches the
/// parser (FR-REC-03).
/// </summary>
public sealed record RecipeEdit
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public decimal? Servings { get; init; }
    /// <summary>High end of a servings range ("6-8"), ADR 0010.</summary>
    public decimal? ServingsMax { get; init; }
    public int? PrepMinutes { get; init; }
    public int? PrepMinutesMax { get; init; }
    public int? CookMinutes { get; init; }
    public int? CookMinutesMax { get; init; }
    public int? TotalMinutes { get; init; }
    public int? TotalMinutesMax { get; init; }
    public bool IsPrecision { get; init; }
    public string? Notes { get; init; }
    public SourceType SourceType { get; init; } = SourceType.Manual;
    public string? SourceName { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourceAuthor { get; init; }
    public string? IngredientsText { get; init; }
    public string? StepsText { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>"Publish" rather than "Save draft" was pressed; BR-01 still decides.</summary>
    public bool WantsPublish { get; init; }

    public const int MaxDescriptionLength = 4000;
    public const int MaxNotesLength = 10000;
    public const int MaxIngredientLines = 200;
    public const int MaxIngredientLineLength = 500;
    public const int MaxTags = 30;
    public const int MaxMinutes = TimeText.MaxMinutes;

    /// <summary>Field errors that stop any save (BR-01 problems only stop publishing).</summary>
    public IReadOnlyList<PublishProblem> Validate()
    {
        var errors = new List<PublishProblem>();
        if ((Description?.Length ?? 0) > MaxDescriptionLength)
        {
            errors.Add(new(RecipeField.Description, "The description is limited to 4000 characters"));
        }

        if (ServingsRules.ValidateRange(Servings, ServingsMax) is { } servingsError)
        {
            errors.Add(new(RecipeField.Servings, servingsError));
        }

        AddTime(errors, RecipeField.PrepMinutes, PrepMinutes, PrepMinutesMax);
        AddTime(errors, RecipeField.CookMinutes, CookMinutes, CookMinutesMax);
        AddTime(errors, RecipeField.TotalMinutes, TotalMinutes, TotalMinutesMax);

        if ((Notes?.Length ?? 0) > MaxNotesLength)
        {
            errors.Add(new(RecipeField.Notes, "Notes are limited to 10000 characters"));
        }

        foreach (var p in SourceRules.Validate(SourceType, SourceUrl, SourceName, SourceAuthor))
        {
            errors.Add(new(p.Field, p.Message));
        }

        var lines = (IngredientsText ?? string.Empty).Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length > MaxIngredientLines)
        {
            errors.Add(new(RecipeField.Ingredients, "A recipe can have at most 200 ingredient lines"));
        }
        else if (lines.Any(l => l.Trim().Length > MaxIngredientLineLength))
        {
            errors.Add(new(RecipeField.Ingredients, "Each ingredient line is limited to 500 characters"));
        }

        if (StepTextRules.Validate(StepTextRules.Split(StepsText)) is { } stepError)
        {
            errors.Add(new(RecipeField.Steps, stepError));
        }

        if (Tags.Count > MaxTags)
        {
            errors.Add(new(RecipeField.Tags, "A recipe can have at most 30 tags"));
        }
        else if (Tags.Select(NameRules.ValidateTag).FirstOrDefault(e => e is not null) is { } tagError)
        {
            errors.Add(new(RecipeField.Tags, tagError));
        }

        return errors;
    }

    private static void AddTime(List<PublishProblem> errors, RecipeField field, int? minutes, int? max)
    {
        if (TimeText.ValidateRange(minutes, max) is { } error)
        {
            errors.Add(new(field, error));
        }
    }
}
