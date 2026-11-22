using System.ComponentModel.DataAnnotations;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>
/// SEC-13, AC-72: the editor form binds to this DTO only. It holds the fields an Editor may change and the version
/// they loaded (BR-06). Id, slug, status, owner and audit fields are not here, so posting them does nothing.
/// </summary>
public sealed class RecipeInput
{
    [Display(Name = "Title")]
    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>"6", "6-8", "Serves 6 to 8" (ADR 0010).</summary>
    public string? Servings { get; set; }

    /// <summary>"20", "20-25", "1 hr 15 min", "1-1.5 hours" (ADR 0010).</summary>
    [Display(Name = "Prep time")]
    public string? PrepTime { get; set; }

    [Display(Name = "Cook time")]
    public string? CookTime { get; set; }

    [Display(Name = "Total time")]
    public string? TotalTime { get; set; }

    public bool IsPrecision { get; set; }

    public string? Notes { get; set; }

    public SourceType SourceType { get; set; } = SourceType.Manual;

    public string? SourceName { get; set; }

    public string? SourceUrl { get; set; }

    public string? SourceAuthor { get; set; }

    /// <summary>One ingredient per line; a line ending in ":" starts a group.</summary>
    public string? Ingredients { get; set; }

    /// <summary>One step per line; a short line ending in ":" starts a section.</summary>
    public string? Steps { get; set; }

    /// <summary>ADR 0016: "New tags" box, comma-separated names. A name that matches an existing tag selects it.</summary>
    public string? Tags { get; set; }

    /// <summary>ADR 0016: the tags ticked in the picker. Ids that do not exist are refused (SEC-13).</summary>
    public int[] TagIds { get; set; } = [];

    /// <summary>BR-06: the version the editor was opened at.</summary>
    public int Version { get; set; }

    /// <summary>
    /// The edit for the save pipeline, or the fields whose text could not be read (UX-08: shown at the top and on
    /// the field, with an example of what is accepted).
    /// </summary>
    public (RecipeEdit Edit, IReadOnlyList<PublishProblem> Errors) ToEdit(bool publish)
    {
        var errors = new List<PublishProblem>();
        var servings = ServingsRules.ParseText(Servings);
        if (servings.Error is not null)
        {
            errors.Add(new PublishProblem(RecipeField.Servings, servings.Error));
        }

        var prep = Time(RecipeField.PrepMinutes, PrepTime, errors);
        var cook = Time(RecipeField.CookMinutes, CookTime, errors);
        var total = Time(RecipeField.TotalMinutes, TotalTime, errors);

        return (Build(publish, servings, prep, cook, total), errors);
    }

    private static TimeValue Time(RecipeField field, string? text, List<PublishProblem> errors)
    {
        var value = TimeText.Parse(text);
        if (value.Error is not null)
        {
            errors.Add(new PublishProblem(field, value.Error));
        }

        return value;
    }

    private RecipeEdit Build(bool publish, ServingsText servings, TimeValue prep, TimeValue cook, TimeValue total) => new()
    {
        Title = Title,
        Description = Description,
        Servings = servings.Min,
        ServingsMax = servings.Max,
        PrepMinutes = prep.Min,
        PrepMinutesMax = prep.Max,
        CookMinutes = cook.Min,
        CookMinutesMax = cook.Max,
        TotalMinutes = total.Min,
        TotalMinutesMax = total.Max,
        IsPrecision = IsPrecision,
        Notes = Notes,
        SourceType = SourceType,
        SourceName = SourceName,
        SourceUrl = SourceUrl,
        SourceAuthor = SourceAuthor,
        IngredientsText = Ingredients,
        StepsText = Steps,
        Tags = (Tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        WantsPublish = publish,
    };

    public static RecipeInput From(RecipeEdit e, int version) => new()
    {
        Title = e.Title,
        Description = e.Description,
        Servings = e.Servings is { } s ? ServingsRules.Format(s, e.ServingsMax) : null,
        PrepTime = NullIfEmpty(TimeText.Format(e.PrepMinutes, e.PrepMinutesMax)),
        CookTime = NullIfEmpty(TimeText.Format(e.CookMinutes, e.CookMinutesMax)),
        TotalTime = NullIfEmpty(TimeText.Format(e.TotalMinutes, e.TotalMinutesMax)),
        IsPrecision = e.IsPrecision,
        Notes = e.Notes,
        SourceType = e.SourceType,
        SourceName = e.SourceName,
        SourceUrl = e.SourceUrl,
        SourceAuthor = e.SourceAuthor,
        Ingredients = e.IngredientsText,
        Steps = e.StepsText,
        Tags = string.Join(", ", e.Tags),
        Version = version,
    };

    /// <summary>The form field a Core problem belongs to (UX-08: errors link to their fields).</summary>
    public static string FieldName(RecipeField field) => field switch
    {
        RecipeField.Ingredients => "Input.Ingredients",
        RecipeField.Steps => "Input.Steps",
        RecipeField.PrepMinutes => "Input.PrepTime",
        RecipeField.CookMinutes => "Input.CookTime",
        RecipeField.TotalMinutes => "Input.TotalTime",
        _ => "Input." + field,
    };

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}
