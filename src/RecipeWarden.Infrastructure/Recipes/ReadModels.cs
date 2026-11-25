using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Infrastructure.Recipes;

/// <summary>Result of a recipe, tag or collection write.</summary>
public enum SaveStatus
{
    Saved,
    Invalid,
    Conflict,
    NotFound,
}

/// <summary>
/// The outcome of a save. <see cref="PublishProblems"/> lists what keeps a saved Draft from publishing (AC-02);
/// <see cref="Errors"/> stop the save; <see cref="Differences"/> explain a BR-06 conflict.
/// </summary>
public sealed record SaveOutcome
{
    public SaveStatus Status { get; init; }
    public int Id { get; init; }
    public string? Slug { get; init; }
    public RecipeStatus RecipeStatus { get; init; }
    public int Version { get; init; }
    public IReadOnlyList<PublishProblem> PublishProblems { get; init; } = [];
    public IReadOnlyList<PublishProblem> Errors { get; init; } = [];
    public IReadOnlyList<FieldDifference> Differences { get; init; } = [];

    public static SaveOutcome NotFound { get; } = new() { Status = SaveStatus.NotFound };

    public static SaveOutcome Invalid(IReadOnlyList<PublishProblem> errors) => new() { Status = SaveStatus.Invalid, Errors = errors };
}

/// <summary>A recipe as the editor shows it.</summary>
public sealed record RecipeForEdit(int Id, string Slug, RecipeStatus Status, int Version, RecipeEdit Edit)
{
    /// <summary>ADR 0022: the finish image id, if any.</summary>
    public string? FinishImageId { get; init; }

    /// <summary>ADR 0023: the saved steps, in order, with their photos (for the photo panel).</summary>
    public IReadOnlyList<StepPhoto> StepPhotos { get; init; } = [];
}

/// <summary>ADR 0023: a saved step and its photo, for the editor's photo panel. <see cref="Index"/> is its position.</summary>
public sealed record StepPhoto(int Index, string? Title, string Text, string? ImageId);

public sealed record TagLink(string Name, string Slug, TagType? TagType);

public sealed record IngredientGroupView(string? Name, IReadOnlyList<IngredientLineView> Lines);

/// <summary>
/// One ingredient for reading: the formatted text (UX-05), whether it still needs review (BR-04), and the parsed
/// parts and linked food the measurement engine scales from (ADR 0013).
/// </summary>
public sealed record IngredientLineView(string Text, string RawText, bool NeedsReview, bool IsOptional)
{
    public ParsedIngredient? Parsed { get; init; }
    public int? FoodId { get; init; }

    /// <summary>The stored line's id (0 for a preview).</summary>
    public int Id { get; init; }

    /// <summary>BR-122: the line's measure choice.</summary>
    public LineMeasure Measure { get; init; }
}

/// <summary>A step with its optional heading (ADR 0015) and photo (ADR 0023).</summary>
public sealed record StepView(string? Title, string Text, string? ImageId = null);

public sealed record StepSectionView(string? Name, IReadOnlyList<StepView> Steps);

/// <summary>FR-VIEW-01 reading view (images, allergens and nutrition arrive in M4 and M6).</summary>
public sealed record RecipeView
{
    public int Id { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public decimal Servings { get; init; }
    public decimal? ServingsMax { get; init; }
    public bool ServingsEstimated { get; init; }
    public int? PrepMinutes { get; init; }
    public int? PrepMinutesMax { get; init; }
    public int? CookMinutes { get; init; }
    public int? CookMinutesMax { get; init; }

    /// <summary>The total, or prep + cook added end by end when no total was given (ADR 0010).</summary>
    public int? TotalMinutes { get; init; }
    public int? TotalMinutesMax { get; init; }
    public bool IsPrecision { get; init; }
    public RecipeStatus Status { get; init; }
    public SourceType SourceType { get; init; }
    public string? SourceName { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourceAuthor { get; init; }
    public string? Notes { get; init; }
    public int? Rating { get; init; }
    public IReadOnlyList<IngredientGroupView> Ingredients { get; init; } = [];
    public IReadOnlyList<StepSectionView> Steps { get; init; } = [];
    public IReadOnlyList<TagLink> Tags { get; init; } = [];
    public IReadOnlyList<CollectionLink> Collections { get; init; } = [];

    /// <summary>FR-REC-05, ADR 0033: the recipe this one is a variant of, and its own variants (visible ones only).</summary>
    public RecipeLink? Parent { get; init; }
    public IReadOnlyList<RecipeLink> Variants { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>ADR 0022: the finish image id, if any.</summary>
    public string? FinishImageId { get; init; }

    /// <summary>FR-IMP-13, ADR 0058 D7: photos of the recipe card it was imported from, in order.</summary>
    public IReadOnlyList<string> CardImageIds { get; init; } = [];

    /// <summary>ADR 0019: the stored finished yield (amount, unit code, source) and finished density (BR-112).</summary>
    public decimal? YieldAmount { get; init; }
    public string? YieldUnitCode { get; init; }
    public Core.Kitchen.YieldSource? YieldSource { get; init; }
    public decimal? FinishedDensityGPerMl { get; init; }

    /// <summary>ADR 0020: the recipe's pan, if entered.</summary>
    public Core.Kitchen.Pan? Pan { get; init; }

    public DateTimeOffset? LastCookedAt { get; init; }

    /// <summary>ADR 0017: the inventory version this recipe last accepted.</summary>
    public int InventoryVersionSeen { get; init; }
}

public sealed record CollectionLink(int Id, string Name, string Slug);

/// <summary>A link to another recipe.</summary>
public sealed record RecipeLink(int Id, string Slug, string Title);

/// <summary>Result of looking up a reading-view slug: the recipe, or the current slug to redirect to (ADR 0008).</summary>
public sealed record RecipeLookup(RecipeView? Recipe, string? RedirectToSlug)
{
    public static RecipeLookup None { get; } = new(null, null);
}

/// <summary>A recipe in a grid or list.</summary>
public sealed record RecipeCard(int Id, string Slug, string Title, string? Summary, int? TotalMinutes, int? TotalMinutesMax, int? Rating,
    RecipeStatus Status)
{
    /// <summary>ADR 0022: the finish image, shown as a thumbnail.</summary>
    public string? ImageId { get; init; }

    /// <summary>"25 min", "20–25 min", "1 hr 15 min"; empty when unknown.</summary>
    public string TimeText => Core.Recipes.TimeText.Format(TotalMinutes, TotalMinutesMax);
}

public sealed record SearchPage(IReadOnlyList<RecipeCard> Items, int Total, int Page, int PageCount);

public sealed record TrashItem(int Id, string Title, DateTimeOffset DeletedAt, DateTimeOffset PurgesOn);

public sealed record TagSummary(int Id, string Name, string Slug, TagType? TagType, int RecipeCount);

public sealed record CollectionSummary(int Id, string Name, string Slug, string? Description, int RecipeCount, int Version);

public sealed record CollectionView(int Id, string Name, string Slug, string? Description, int Version, IReadOnlyList<RecipeCard> Recipes);
