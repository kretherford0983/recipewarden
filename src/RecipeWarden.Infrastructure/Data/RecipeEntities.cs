using System.Diagnostics.CodeAnalysis;
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
    public int? ParentRecipeId { get; set; }
    public string? Notes { get; set; }
    public int? Rating { get; set; }
    public DateTimeOffset? LastCookedAt { get; set; }
    public int InventoryVersionSeen { get; set; }

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
}

/// <summary>Section 8.2 Step (ImageId arrives in M4).</summary>
public sealed class RecipeStep
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int SortOrder { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? SectionName { get; set; }
    public int? TimerSeconds { get; set; }

    /// <summary>Optional step heading, e.g. "Preheat oven" (ADR 0015).</summary>
    public string? Title { get; set; }
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

    /// <summary>Usda (seeded from USDA public-domain data) or Custom (added by an editor).</summary>
    public string Source { get; set; } = FoodSources.Custom;

    /// <summary>Where the numbers came from, e.g. "USDA FoodData Central SR Legacy 168894; 1 cup = 125 g".</summary>
    public string? SourceNote { get; set; }

    public int? DefaultPurchaseUnitId { get; set; }
    public decimal? PackageSize { get; set; }
    public int? PackageUnitId { get; set; }
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
