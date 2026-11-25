using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Measurement;

/// <summary>
/// BR-27, FR-MEAS-06, AC-36: eggs round to the nearest half and never show as "4.5 eggs". A half egg is written
/// two ways (ADR 0012, owner 2026-11-22): baking/precision recipes get the more exact
/// "4½ eggs (4 eggs + 2 Tbsp beaten egg)" (half a large egg is about 25 g, 2 Tbsp beaten); other recipes get the
/// simpler "4 eggs + 1 yolk".
/// </summary>
public static class EggRules
{
    private static readonly HashSet<string> EggNames = new(StringComparer.Ordinal)
    {
        "egg", "large egg", "medium egg", "small egg", "extra-large egg", "extra large egg", "jumbo egg", "whole egg",
    };

    /// <summary>True for whole eggs ("eggs", "large eggs"); not for yolks, whites or egg noodles.</summary>
    public static bool IsEgg(string? name) => EggNames.Contains(FoodMatcher.Singular(FoodMatcher.Normalize(name)));

    /// <summary>The nearest half egg; at least half an egg.</summary>
    public static decimal Round(decimal eggs)
    {
        var halves = Math.Round(eggs * 2m, MidpointRounding.AwayFromZero) / 2m;
        return halves <= 0m ? 0.5m : halves;
    }

    public static string Text(decimal eggs, ParsedIngredient ingredient, bool isPrecision)
    {
        ArgumentNullException.ThrowIfNull(ingredient);
        var rounded = Round(eggs);
        var whole = decimal.Floor(rounded);
        var name = ingredient.Name;
        var tail = Tail(ingredient);
        if (rounded == whole)
        {
            return Fractions.Format(whole) + " " + IngredientScaler.CountName(name, ingredient.Quantity ?? 1m, whole) + tail;
        }

        name = FoodMatcher.Plural(name);

        if (isPrecision)
        {
            return whole == 0m
                ? "½ " + FoodMatcher.Singular(name) + " (2 Tbsp beaten egg)" + tail
                : Fractions.Format(rounded) + " " + name + " (" + Fractions.Format(whole) + " "
                  + (whole == 1m ? FoodMatcher.Singular(name) : name) + " + 2 Tbsp beaten egg)" + tail;
        }

        return whole == 0m
            ? "1 egg yolk" + tail
            : Fractions.Format(whole) + " " + (whole == 1m ? FoodMatcher.Singular(name) : name) + " + 1 yolk" + tail;
    }

    private static string Tail(ParsedIngredient p)
    {
        var tail = string.Empty;
        if (!string.IsNullOrEmpty(p.Preparation))
        {
            tail += ", " + p.Preparation;
        }

        if (!string.IsNullOrEmpty(p.Note))
        {
            tail += " (" + p.Note + ")";
        }

        return p.IsOptional ? tail + " (optional)" : tail;
    }
}
