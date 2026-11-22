using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Measurement;

/// <summary>The factor from "I have …", or why it could not be worked out.</summary>
public sealed record IngredientFactorResult(decimal? Factor, string? Error);

/// <summary>
/// FR-SCL-02, AC-31: "I have 3 lb chicken" on a recipe calling for 1.5 lb gives 2x. Amounts are compared in base
/// units; volume and weight are compared only when the food's density is known (BR-23). A range in the recipe
/// uses its low end.
/// </summary>
public static class IngredientFactor
{
    public const string NoAmount = "That ingredient has no amount to scale from.";
    public const string HaveNoAmount = "Enter how much you have, for example 3 lb.";
    public const string NoDensity = "Can't compare those units for this ingredient: no weight per cup is known.";
    public const string DifferentKinds = "Can't compare those units; use the recipe's unit.";
    public const string OutOfRange = "That amount would scale the recipe beyond 0.1x–100x.";

    public static IngredientFactorResult Compute(ParsedIngredient have, ParsedIngredient recipeLine, decimal? densityGPerMl,
        MeasurementCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(have);
        ArgumentNullException.ThrowIfNull(recipeLine);
        catalog ??= MeasurementCatalog.Default;
        if (recipeLine.Quantity is not { } need || need <= 0m)
        {
            return new IngredientFactorResult(null, NoAmount);
        }

        if (have.Quantity is not { } got || got <= 0m)
        {
            return new IngredientFactorResult(null, HaveNoAmount);
        }

        var haveUnit = catalog.Unit(have.UnitCode);
        var needUnit = catalog.Unit(recipeLine.UnitCode);
        decimal factor;
        if (haveUnit is null || needUnit is null)
        {
            // Counts: "6 eggs" for "3 eggs"; both must be counts of the same thing.
            if (haveUnit != needUnit)
            {
                return new IngredientFactorResult(null, DifferentKinds);
            }

            factor = got / need;
        }
        else if (haveUnit.Kind == needUnit.Kind && haveUnit.Kind is UnitKind.Volume or UnitKind.Mass)
        {
            factor = got * haveUnit.ToBase / (need * needUnit.ToBase);
        }
        else if (haveUnit.Kind is UnitKind.Volume or UnitKind.Mass && needUnit.Kind is UnitKind.Volume or UnitKind.Mass)
        {
            if (densityGPerMl is not { } d || d <= 0m)
            {
                return new IngredientFactorResult(null, NoDensity);
            }

            var haveGrams = got * haveUnit.ToBase * (haveUnit.Kind == UnitKind.Volume ? d : 1m);
            var needGrams = need * needUnit.ToBase * (needUnit.Kind == UnitKind.Volume ? d : 1m);
            factor = haveGrams / needGrams;
        }
        else if (haveUnit.Code == needUnit.Code)
        {
            factor = got / need;
        }
        else
        {
            return new IngredientFactorResult(null, DifferentKinds);
        }

        factor = Math.Round(factor, 4);
        return ScaleRules.InRange(factor) ? new IngredientFactorResult(factor, null) : new IngredientFactorResult(null, OutOfRange);
    }
}
