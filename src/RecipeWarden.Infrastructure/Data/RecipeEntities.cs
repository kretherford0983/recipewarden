using System.Diagnostics.CodeAnalysis;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Infrastructure.Data;

// Section 8.2 recipe aggregate, as amended by ADR 0009 (text only in M1; images, nutrition and allergens later).

/// <summary>Section 8.2 Recipe: the root entity. Soft-deleted with <see cref="DeletedAt"/> (BR-05).</summary>
public sealed class Recipe
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Servings { get; set; } = ServingsRules.Default;
    public decimal? ServingsMax { get; set; }
    public bool ServingsEstimated { get; set; }
    public string? YieldText { get; set; }
    public decimal? YieldAmount { get; set; }
    public int? YieldUnitId { get; set; }
    public string? YieldSource { get; set; }
    public decimal? FinishedDensityGPerMl { get; set; }
    public int? PrepMinutes { get; set; }
    public int? PrepMinutesMax { get; set; }
    public int? CookMinutes { get; set; }
    public int? CookMinutesMax { get; set; }
    public int? TotalMinutes { get; set; }
    public int? TotalMinutesMax { get; set; }
    public bool IsPrecision { get; set; }
    public RecipeStatus Status { get; set; }
    public SourceType SourceType { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceName { get; set; }
    public string? SourceAuthor { get; set; }
    public DateTimeOffset? SourceImportedAt { get; set; }

    /// <summary>ADR 0022: the finish image (8.2 FinishImageId), a 32-hex <see cref="Image"/> id.</summary>
    public string? FinishImageId { get; set; }
    public int? ParentRecipeId { get; set; }
    public string? Notes { get; set; }
    public int? Rating { get; set; }
    public DateTimeOffset? LastCookedAt { get; set; }
    public int InventoryVersionSeen { get; set; }

    /// <summary>ADR 0020: the recipe's pan; width is the diameter of a round pan. Inches.</summary>
    public PanShape? PanShape { get; set; }
    public decimal? PanWidth { get; set; }
    public decimal? PanLength { get; set; }

    /// <summary>BR-06: optimistic concurrency token, incremented on every save.</summary>
    public int Version { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CreatedById { get; set; }
    public string? UpdatedById { get; set; }

    public List<IngredientGroup> IngredientGroups { get; } = [];
    public List<RecipeIngredient> Ingredients { get; } = [];
    public List<RecipeStep> Steps { get; } = [];
    public List<RecipeTag> Tags { get; } = [];
}

/// <summary>Section 8.2 IngredientGroup: "For the sauce".</summary>
public sealed class IngredientGroup
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>Section 8.2 RecipeIngredient; <see cref="RawText"/> is always kept (BR-04). Name per ADR 0009.</summary>
public sealed class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int? GroupId { get; set; }
    public IngredientGroup? Group { get; set; }
    public int SortOrder { get; set; }
    public string RawText { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal? QuantityMax { get; set; }
    public string? QuantityText { get; set; }
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Preparation { get; set; }
    public string? State { get; set; }
    public bool IsOptional { get; set; }
    public string? Note { get; set; }
    public decimal ParseConfidence { get; set; }

    /// <summary>The catalogue food this line is linked to (8.2, ADR 0013); null when nothing matched.</summary>
    public int? FoodId { get; set; }

    /// <summary>BR-122: Auto, Weight or AsWritten (stored as text, 8.1).</summary>
    public RecipeWarden.Core.Measurement.LineMeasure Measure { get; set; }
}

/// <summary>Section 8.2 Step.</summary>
public sealed class RecipeStep
{
    /// <summary>ADR 0023: the step photo; follows the step across edits by text, then position.</summary>
    public string? ImageId { get; set; }

    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int SortOrder { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? SectionName { get; set; }
    public int? TimerSeconds { get; set; }

    /// <summary>Optional step heading, e.g. "Preheat oven" (ADR 0015).</summary>
    public string? Title { get; set; }
}

/// <summary>
/// FR-IMP-13, BR-131, ADR 0058 D7: a photo kept from a photo import (a recipe card), in order. The image follows the
/// recipe's access rule (ADR 0022) and goes when the recipe is purged.
/// </summary>
public sealed class RecipeCardImage
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int Position { get; set; }
    public string ImageId { get; set; } = string.Empty;
}

/// <summary>Earlier published slugs that redirect to the current one (BR-02, ADR 0008).</summary>
public sealed class RecipeSlugAlias
{
    public string Slug { get; set; } = string.Empty;
    public int RecipeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Section 8.3 Unit, seeded from <see cref="UnitCatalog"/>.</summary>
public sealed class Unit
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PluralName { get; set; } = string.Empty;
    public string Aliases { get; set; } = string.Empty;
    public UnitKind Kind { get; set; }
    public UnitSystem System { get; set; }
    public decimal ToBase { get; set; }
    public bool IsVague { get; set; }
}

/// <summary>Section 8.2 Tag (FR-ORG-01); unique on <see cref="NormalizedName"/>.</summary>
public sealed class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public TagType? TagType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RecipeTag
{
    public int RecipeId { get; set; }
    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}

/// <summary>Section 8.2 Collection (FR-ORG-02).</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "\"Collection\" is the domain term from the spec (FR-ORG-02), not a .NET collection type.")]
public sealed class RecipeCollection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CreatedById { get; set; }
    public string? UpdatedById { get; set; }
    public List<CollectionRecipe> Recipes { get; } = [];
}

public sealed class CollectionRecipe
{
    public int CollectionId { get; set; }
    public int RecipeId { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Section 8.3 Food: the ingredient catalogue with densities (FR-MEAS-01, ADR 0013).</summary>
public sealed class Food
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string PluralName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal? DensityGPerMl { get; set; }
    public int? FdcId { get; set; }

    /// <summary>BR-54, ADR 0034: an editor's nutrient mapping (USDA or custom); wins over <see cref="FdcId"/>.</summary>
    public int? NutrientRefId { get; set; }

    /// <summary>Usda (seeded from USDA public-domain data) or Custom (added by an editor).</summary>
    public string Source { get; set; } = FoodSources.Custom;

    /// <summary>Where the numbers came from, e.g. "USDA FoodData Central SR Legacy 168894; 1 cup = 125 g".</summary>
    public string? SourceNote { get; set; }

    /// <summary>ADR 0018: Dry, Liquid, Fat or Other, for BR-22's weight preference.</summary>
    public FoodForm Form { get; set; }

    public int? DefaultPurchaseUnitId { get; set; }
    public decimal? PackageSize { get; set; }
    public int? PackageUnitId { get; set; }

    /// <summary>ADR 0030: what one package is called ("stick", "can", "carton"); BR-41.</summary>
    public string? PackageName { get; set; }
    public string? StoreSection { get; set; }
    public bool IsPantryStaple { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedById { get; set; }
    public List<FoodAlias> Aliases { get; } = [];
}

public static class FoodSources
{
    public const string Usda = "Usda";
    public const string Custom = "Custom";
}

/// <summary>Section 8.3 FoodAlias: "AP flour" → all-purpose flour. Unique on <see cref="NormalizedAlias"/>.</summary>
public sealed class FoodAlias
{
    public int Id { get; set; }
    public int FoodId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
}

/// <summary>Section 8.3 YieldConversion (BR-26): grams bought per gram prepared, as a range.</summary>
public sealed class YieldConversion
{
    public int Id { get; set; }
    public int FoodId { get; set; }
    public Food? Food { get; set; }
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public UnitKind FromUnitKind { get; set; } = UnitKind.Mass;
    public decimal Ratio { get; set; }
    public decimal RatioMax { get; set; }
    public decimal? PreparedDensityGPerMl { get; set; }
    public string Basis { get; set; } = string.Empty;
    public string Source { get; set; } = FoodSources.Custom;
    public string? SourceNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedById { get; set; }
}

/// <summary>One row per seed applied at startup (ADR 0013), so a seed runs once and deleted rows stay deleted.</summary>
public sealed class SeedRun
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset AppliedAt { get; set; }
}

/// <summary>Section 8.4 KitchenTool (FR-TOOL-01). Retired tools are kept for history (FR-TOOL-02).</summary>
public sealed class KitchenTool
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ToolType ToolType { get; set; }
    public int QuantityOwned { get; set; } = 1;
    public decimal DefaultFillPct { get; set; } = 100m;
    public decimal? CapacityAmount { get; set; }
    public int? CapacityUnitId { get; set; }
    public Unit? CapacityUnit { get; set; }
    public decimal? ResolutionAmount { get; set; }
    public int? ResolutionUnitId { get; set; }
    public Unit? ResolutionUnit { get; set; }

    /// <summary>Unit codes a scale shows, comma-separated ("g,oz").</summary>
    public string? SupportedUnits { get; set; }

    /// <summary>BR-50: markings, "unit min max step" per set, separated by "|" (invariant numbers).</summary>
    public string? Markings { get; set; }
    public PanShape? PanShape { get; set; }
    public decimal? PanWidth { get; set; }
    public decimal? PanLength { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedById { get; set; }
}

/// <summary>Section 8.4 InventoryVersion (BR-30): one row, incremented by every add, edit, retire or reactivate.</summary>
public sealed class InventoryVersion
{
    public int Id { get; set; }
    public int Version { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>ADR 0017: the active tools at a version, so suggestions can be worked out on view without writing.</summary>
public sealed class InventorySnapshot
{
    public int Version { get; set; }
    public string ToolsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Section 8.2 RecipeSuggestionState (FR-TOOL-05): an editor's dismissal or application of a keyed suggestion.</summary>
public sealed class RecipeSuggestionState
{
    public int RecipeId { get; set; }
    public string SuggestionKey { get; set; } = string.Empty;
    public SuggestionStatus Status { get; set; }
    public int InventoryVersion { get; set; }
    public DateTimeOffset At { get; set; }
    public string? ById { get; set; }
}

public enum SuggestionStatus
{
    Dismissed,
    Applied,
}

/// <summary>
/// Section 8.2 Image, ADR 0022: a re-encoded WebP (at most 2400 px) and a 640 px thumbnail under
/// <c>&lt;data&gt;/media</c>. The id is 32 random hex characters, so links cannot be guessed.
/// </summary>
public sealed class Image
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Relative to the media directory, e.g. "ab/ab12….webp".</summary>
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/webp";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Bytes { get; set; }
    public long ThumbBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string? AltText { get; set; }

    /// <summary>BR-12: where a copied image came from.</summary>
    public string? SourceUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedById { get; set; }
}
