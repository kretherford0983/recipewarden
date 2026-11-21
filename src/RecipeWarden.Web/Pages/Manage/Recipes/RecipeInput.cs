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

    public decimal? Servings { get; set; }

    [Display(Name = "Prep minutes")]
    public int? PrepMinutes { get; set; }

    [Display(Name = "Cook minutes")]
    public int? CookMinutes { get; set; }

    [Display(Name = "Total minutes")]
    public int? TotalMinutes { get; set; }

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

    /// <summary>Comma-separated tag names.</summary>
    public string? Tags { get; set; }

    /// <summary>BR-06: the version the editor was opened at.</summary>
    public int Version { get; set; }

    public RecipeEdit ToEdit(bool publish) => new()
    {
        Title = Title,
        Description = Description,
        Servings = Servings,
        PrepMinutes = PrepMinutes,
        CookMinutes = CookMinutes,
        TotalMinutes = TotalMinutes,
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
        Servings = e.Servings,
        PrepMinutes = e.PrepMinutes,
        CookMinutes = e.CookMinutes,
        TotalMinutes = e.TotalMinutes,
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
        _ => "Input." + field,
    };
}
