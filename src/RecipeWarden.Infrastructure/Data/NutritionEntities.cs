using RecipeWarden.Core.Nutrition;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>
/// Section 8.3 NutrientRef (ADR 0034): one food's nutrients per 100 g, from a USDA FoodData Central file (FdcId set)
/// or a package label (Source Custom). Portions are count weights ("large" → 50 g) as JSON.
/// </summary>
public sealed class NutrientRef
{
    public int Id { get; set; }
    public int? FdcId { get; set; }
    public string Source { get; set; } = NutrientSources.Usda;
    public string? DataType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string NormalizedDescription { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? DatasetVersion { get; set; }

    /// <summary>FR-NUT-05: the dataset version that no longer has this food (it is kept so mappings survive).</summary>
    public string? RemovedInVersion { get; set; }
    public decimal? DensityGPerMl { get; set; }
    public string? PortionsJson { get; set; }
    public decimal? Calories { get; set; }
    public decimal? FatG { get; set; }
    public decimal? SatFatG { get; set; }
    public decimal? CholesterolMg { get; set; }
    public decimal? SodiumMg { get; set; }
    public decimal? CarbsG { get; set; }
    public decimal? FiberG { get; set; }
    public decimal? SugarG { get; set; }
    public decimal? ProteinG { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CreatedById { get; set; }

    public NutrientValues Values() => new(Calories, FatG, SatFatG, CholesterolMg, SodiumMg, CarbsG, FiberG, SugarG, ProteinG);

    public void SetValues(NutrientValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        (Calories, FatG, SatFatG, CholesterolMg, SodiumMg, CarbsG, FiberG, SugarG, ProteinG) =
            (v.Calories, v.FatG, v.SatFatG, v.CholesterolMg, v.SodiumMg, v.CarbsG, v.FiberG, v.SugarG, v.ProteinG);
    }
}

public static class NutrientSources
{
    public const string Usda = "Usda";
    public const string Custom = "Custom";
}

/// <summary>FR-NUT-05, AC-58: each nutrient dataset applied, newest last.</summary>
public sealed class NutrientDataset
{
    public int Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? FileSha256 { get; set; }
    public int Foods { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
}

/// <summary>Section 8.2 NutritionFacts (ADR 0035): a recipe's manual and/or imported panel, per serving (one row per source).</summary>
public sealed class NutritionFacts
{
    public int RecipeId { get; set; }
    public NutritionSource Source { get; set; }
    public decimal? Calories { get; set; }
    public decimal? FatG { get; set; }
    public decimal? SatFatG { get; set; }
    public decimal? CholesterolMg { get; set; }
    public decimal? SodiumMg { get; set; }
    public decimal? CarbsG { get; set; }
    public decimal? FiberG { get; set; }
    public decimal? SugarG { get; set; }
    public decimal? ProteinG { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedById { get; set; }

    public NutrientValues Values() => new(Calories, FatG, SatFatG, CholesterolMg, SodiumMg, CarbsG, FiberG, SugarG, ProteinG);

    public void SetValues(NutrientValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        (Calories, FatG, SatFatG, CholesterolMg, SodiumMg, CarbsG, FiberG, SugarG, ProteinG) =
            (v.Calories, v.FatG, v.SatFatG, v.CholesterolMg, v.SodiumMg, v.CarbsG, v.FiberG, v.SugarG, v.ProteinG);
    }
}

/// <summary>Section 8.2 RecipeAllergen (ADR 0036): Auto rows are rewritten on save; Manual rows only by an editor.</summary>
public sealed class RecipeAllergen
{
    public int RecipeId { get; set; }
    public AllergenCode Allergen { get; set; }
    public AllergenOrigin Origin { get; set; }
    public AllergenStatus Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
