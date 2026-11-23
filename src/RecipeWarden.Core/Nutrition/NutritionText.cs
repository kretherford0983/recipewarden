using System.Globalization;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Nutrition;

/// <summary>
/// ADR 0035: imported nutrition. Reads schema.org NutritionInformation strings ("410 kcal", "890 mg", "1.2 g") and
/// recipe-md's <c>nutrition.per_serving</c> keys; writes the recipe-md keys. Values out of range are dropped.
/// </summary>
public static partial class NutritionText
{
    /// <summary>recipe-md <c>per_serving</c> keys.</summary>
    public static IReadOnlyList<string> Keys { get; } =
        ["calories", "fat_g", "saturated_fat_g", "cholesterol_mg", "sodium_mg", "carbs_g", "fiber_g", "sugar_g", "protein_g"];

    [GeneratedRegex(@"^\s*(?<n>\d{1,6}(?:[.,]\d+)?)\s*(?<u>kcal|cal|calories|mg|milligrams?|g|grams?|kj)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex Amount();

    /// <summary>A schema.org value in the unit the panel uses: grams, or milligrams when <paramref name="milligrams"/>.</summary>
    public static decimal? Parse(string? text, bool milligrams = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var m = Amount().Match(text);
            if (!m.Success || !decimal.TryParse(m.Groups["n"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n))
            {
                return null;
            }

            var unit = m.Groups["u"].Value.ToLowerInvariant();
            if (unit == "kj")
            {
                n /= 4.184m;
            }
            else if (milligrams && unit is "g" or "gram" or "grams")
            {
                n *= 1000;
            }
            else if (!milligrams && unit.StartsWith('m'))
            {
                n /= 1000;
            }

            return n is >= 0 and <= 100_000 ? Math.Round(n, 2) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>schema.org property → value (calories, fatContent, …); null when nothing usable.</summary>
    public static NutrientValues? FromSchemaOrg(Func<string, string?> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var v = new NutrientValues(
            Parse(property("calories")), Parse(property("fatContent")), Parse(property("saturatedFatContent")),
            Parse(property("cholesterolContent"), milligrams: true), Parse(property("sodiumContent"), milligrams: true),
            Parse(property("carbohydrateContent")), Parse(property("fiberContent")), Parse(property("sugarContent")),
            Parse(property("proteinContent")));
        return v.Calories is null ? null : v;
    }

    /// <summary>recipe-md <c>per_serving</c> map → values (numbers only); null without calories.</summary>
    public static NutrientValues? FromRecipeMd(Func<string, string?> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        decimal? N(string k) => decimal.TryParse(key(k), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 100_000 ? n : null;
        var v = new NutrientValues(N("calories"), N("fat_g"), N("saturated_fat_g"), N("cholesterol_mg"), N("sodium_mg"), N("carbs_g"),
            N("fiber_g"), N("sugar_g"), N("protein_g"));
        return v.Calories is null ? null : v;
    }

    /// <summary>"{ calories: 410, fat_g: 14 }" (missing values left out).</summary>
    public static string ToRecipeMd(NutrientValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        decimal?[] values = [v.Calories, v.FatG, v.SatFatG, v.CholesterolMg, v.SodiumMg, v.CarbsG, v.FiberG, v.SugarG, v.ProteinG];
        var parts = Keys.Zip(values).Where(p => p.Second is not null)
            .Select(p => p.First + ": " + Math.Round(p.Second!.Value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture));
        return "{ " + string.Join(", ", parts) + " }";
    }
}
