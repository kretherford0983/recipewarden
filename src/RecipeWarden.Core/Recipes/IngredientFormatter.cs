using System.Globalization;
using System.Text;
using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Recipes;

/// <summary>
/// UX-05: shows a parsed ingredient for reading, with fraction glyphs (½, 1¼) and consistent unit abbreviations.
/// Lines that need review show their original text, so nothing the parser misread is shown as fact. The display
/// planner (M2) will choose units and tools; this formatter only shows what was entered.
/// </summary>
public static class IngredientFormatter
{
    private static readonly (decimal Value, string Glyph)[] Glyphs =
    [
        (0.125m, "⅛"), (0.25m, "¼"), (0.333333m, "⅓"), (0.375m, "⅜"), (0.5m, "½"), (0.625m, "⅝"),
        (0.666667m, "⅔"), (0.75m, "¾"), (0.875m, "⅞"),
    ];

    private const decimal GlyphTolerance = 0.002m;

    public static string Format(ParsedIngredient ingredient)
    {
        ArgumentNullException.ThrowIfNull(ingredient);
        if (ingredient.NeedsReview)
        {
            return ingredient.RawText;
        }

        var sb = new StringBuilder();
        if (ingredient.Quantity is { } q)
        {
            sb.Append(Amount(q));
            if (ingredient.QuantityMax is { } max)
            {
                sb.Append('–').Append(Amount(max));
            }

            sb.Append(' ');
        }

        if (ingredient.UnitCode is { } code)
        {
            var unit = UnitCatalog.FindByCode(code);
            var plural = (ingredient.QuantityMax ?? ingredient.Quantity ?? 1m) > 1m;
            sb.Append(unit is null ? code : plural ? unit.PluralName : unit.Name).Append(' ');
        }

        sb.Append(ingredient.Name);
        if (!string.IsNullOrEmpty(ingredient.Preparation))
        {
            sb.Append(", ").Append(ingredient.Preparation);
        }

        if (!string.IsNullOrEmpty(ingredient.Note))
        {
            // Notes that read as a phrase ("to taste", "divided") follow a comma; sizes go in brackets.
            var phrase = ingredient.Note.Length > 0 && char.IsAsciiLetterLower(ingredient.Note[0])
                && !ingredient.Note.Contains(';', StringComparison.Ordinal)
                && ingredient.Quantity is null;
            sb.Append(phrase ? ", " + ingredient.Note : " (" + ingredient.Note + ")");
        }

        if (ingredient.IsOptional)
        {
            sb.Append(" (optional)");
        }

        return sb.ToString();
    }

    public static string Amount(decimal value)
    {
        var whole = decimal.Truncate(value);
        var fraction = value - whole;
        if (fraction == 0m)
        {
            return whole.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var (v, glyph) in Glyphs)
        {
            if (Math.Abs(fraction - v) <= GlyphTolerance)
            {
                return whole == 0m ? glyph : whole.ToString(CultureInfo.InvariantCulture) + glyph;
            }
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
