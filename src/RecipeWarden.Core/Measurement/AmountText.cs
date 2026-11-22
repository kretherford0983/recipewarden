namespace RecipeWarden.Core.Measurement;

/// <summary>An amount and its unit, read from short text.</summary>
public sealed record Amount(decimal Value, UnitDefinition Unit)
{
    public decimal Base => Value * Unit.ToBase;
}

/// <summary>
/// Reads "12 fl oz", "1 quart", "about 2 1/2 cups", "500 g" (ADR 0019: recipe yields and typed container
/// capacities). Volume and mass units only; anything else is refused rather than guessed.
/// </summary>
public static class AmountText
{
    public const int MaxLength = 40;

    public static bool TryParse(string? text, out Amount? amount)
    {
        amount = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return false;
        }

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && (words[0].Equals("about", StringComparison.OrdinalIgnoreCase) || words[0] == "~"))
        {
            words.RemoveAt(0);
        }

        for (var unitWords = Math.Min(UnitCatalog.MaxAliasWords, words.Count - 1); unitWords >= 1; unitWords--)
        {
            var number = string.Join(' ', words.Take(words.Count - unitWords));
            var unitText = string.Join(' ', words.Skip(words.Count - unitWords));
            if (UnitCatalog.FindByAlias(unitText) is { Kind: UnitKind.Volume or UnitKind.Mass } unit
                && ScaleRules.TryParseNumber(number, out var value) && value > 0m)
            {
                amount = new Amount(value, unit);
                return true;
            }
        }

        return false;
    }

    public static string Format(decimal value, UnitDefinition unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var shown = unit.System == UnitSystem.Metric ? Fractions.FormatMetric(value, unit.Code) : Fractions.Format(Fractions.Nearest(value, Fractions.Standard).Value);
        return shown + " " + MeasureDisplay.UnitName(unit, value);
    }
}
