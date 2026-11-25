namespace RecipeWarden.Core.Recipes;

/// <summary>
/// FR-REC-03, BR-04: one ingredient line split into parts. <see cref="RawText"/> is always kept; a confidence
/// below <see cref="ReviewThreshold"/> flags the line for review.
/// </summary>
public sealed record ParsedIngredient
{
    public const decimal ReviewThreshold = 0.8m;

    public required string RawText { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? QuantityMax { get; init; }

    /// <summary>The amount as written when it holds a fraction ("1 1/2", "3/4"), so exact fractions survive (8.1).</summary>
    public string? QuantityText { get; init; }

    /// <summary>A <see cref="Measurement.UnitCatalog"/> code, or null for a plain count ("2 eggs") or no amount.</summary>
    public string? UnitCode { get; init; }

    /// <summary>The ingredient itself, e.g. "all-purpose flour" (ADR 0009).</summary>
    public string Name { get; init; } = string.Empty;

    public string? Preparation { get; init; }

    /// <summary>raw, cooked, uncooked or frozen when the line says so (used by yield conversions in M2).</summary>
    public string? State { get; init; }

    public bool IsOptional { get; init; }

    /// <summary>Everything else worth keeping: "to taste", "divided", "15 oz", "plus 2 Tbsp".</summary>
    public string? Note { get; init; }

    public decimal Confidence { get; init; }

    public bool NeedsReview => Confidence < ReviewThreshold;
}

/// <summary>A pasted ingredient line: either a group heading ("For the sauce:") or an ingredient.</summary>
public sealed record IngredientLine(string? GroupHeading, ParsedIngredient? Ingredient)
{
    public bool IsHeading => GroupHeading is not null;
}
