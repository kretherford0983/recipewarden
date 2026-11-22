using System.Text;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Measurement;

/// <summary>
/// One ingredient to scale: the parsed line, the food it is linked to (ADR 0013) and, for lines such as
/// "1 cup plus 2 tablespoons flour", the extra amount from the note so it scales with the rest.
/// </summary>
public sealed record ScaleInput(ParsedIngredient Ingredient, int? FoodId = null, ParsedIngredient? Plus = null);

/// <summary>How a recipe is being shown: factor (BR-20), units (ADR 0014) and whether it is baking/precision (FR-REC-06).</summary>
public sealed record ScaleContext(decimal Factor, DisplayUnits Units, bool IsPrecision, MeasurementCatalog Catalog)
{
    public static ScaleContext AsWritten { get; } = new(1m, DisplayUnits.AsWritten, false, MeasurementCatalog.Default);

    public decimal Tolerance => MeasureDisplay.Tolerance(IsPrecision);
}

/// <summary>
/// A scaled ingredient line. <see cref="Text"/> is the line; <see cref="Secondary"/> the original volume when
/// weight is shown (BR-22); <see cref="Hint"/> an approximate amount or why a line was not converted (BR-23, BR-25);
/// <see cref="YieldNote"/> the purchase-state estimate (BR-26), whose grams are kept for shopping lists (M5).
/// </summary>
public sealed record ScaledIngredient(string Text, string? Secondary = null, string? Hint = null, string? YieldNote = null,
    decimal? PurchaseGrams = null, decimal? PurchaseGramsMax = null);

/// <summary>
/// Section 9.4 measurement engine (M2 part, ADR 0012): normalize to ml, g, count or vague; scale (BR-20); convert
/// kind when a density allows (BR-22 in Weight view, BR-23); estimate the purchase amount (BR-26); and show the
/// result with makeable fractions (BR-24, BR-25, BR-27, BR-28). Pure and deterministic.
/// </summary>
public static class IngredientScaler
{
    public const string NeedsCheckingHint = "Not scaled: this line needs checking in the editor.";
    public const string NoDensityHint = "No weight is known for this ingredient, so it stays in volume.";

    private static readonly HashSet<string> PackageUnits = new(StringComparer.Ordinal)
    {
        "can", "jar", "package", "box", "bag", "bottle", "container", "envelope",
    };

    public static ScaledIngredient Scale(ScaleInput input, ScaleContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        var p = input.Ingredient;
        if (p.NeedsReview)
        {
            return new ScaledIngredient(p.RawText, Hint: context.Factor != 1m ? NeedsCheckingHint : null);
        }

        var unit = context.Catalog.Unit(p.UnitCode);
        if (p.Quantity is null)
        {
            // "Salt and pepper to taste" never scales (BR-25); nor does anything without an amount.
            return new ScaledIngredient(IngredientFormatter.Format(p));
        }

        if (unit is null || unit.Kind == UnitKind.Count)
        {
            return Count(p, unit, context);
        }

        if (unit.IsVague)
        {
            return Vague(p, unit, context);
        }

        return Measured(input, unit, context);
    }

    private static ScaledIngredient Measured(ScaleInput input, UnitDefinition unit, ScaleContext context)
    {
        var p = input.Ingredient;
        var food = context.Catalog.Food(input.FoodId);
        var density = food?.DensityGPerMl;
        var low = p.Quantity!.Value * unit.ToBase * context.Factor;
        var high = p.QuantityMax is { } max ? max * unit.ToBase * context.Factor : (decimal?)null;
        var note = p.Note;

        if (input.Plus is { Quantity: { } plusQty } plus && context.Catalog.Unit(plus.UnitCode) is { Kind: var plusKind } plusUnit
            && plusKind == unit.Kind && !plusUnit.IsVague)
        {
            var extra = plusQty * plusUnit.ToBase * context.Factor;
            low += extra;
            high += extra;
            note = null;
        }

        var yieldNote = YieldEstimate(input, unit, low, context);
        if (context.Factor == 1m && context.Units == DisplayUnits.AsWritten)
        {
            return new ScaledIngredient(IngredientFormatter.Format(p), YieldNote: yieldNote.Note,
                PurchaseGrams: yieldNote.Low, PurchaseGramsMax: yieldNote.High);
        }

        var kind = unit.Kind;
        var system = unit.System == UnitSystem.Metric ? UnitSystem.Metric : UnitSystem.US;
        string? secondary = null;
        string? hint = null;
        switch (context.Units)
        {
            case DisplayUnits.US:
                system = UnitSystem.US;
                break;
            case DisplayUnits.Metric:
                system = UnitSystem.Metric;
                break;
            case DisplayUnits.Weight when kind == UnitKind.Volume && density is { } d && d > 0m:
                secondary = AmountText(low, high, kind, system, unit, context);
                low *= d;
                high *= d;
                kind = UnitKind.Mass;
                system = UnitSystem.Metric;
                break;
            case DisplayUnits.Weight when kind == UnitKind.Volume:
                hint = NoDensityHint; // BR-23: no density, no guess.
                break;
            case DisplayUnits.Weight:
                system = UnitSystem.Metric;
                break;
        }

        var source = unit.Kind == kind && (unit.System == system || (unit.System == UnitSystem.Either && system == UnitSystem.US)) ? unit : null;
        var amount = AmountText(low, high, kind, system, source, context);
        var text = Compose(amount, p, note);
        return new ScaledIngredient(text, secondary, hint, yieldNote.Note, yieldNote.Low, yieldNote.High);
    }

    private static string AmountText(decimal low, decimal? high, UnitKind kind, UnitSystem system, UnitDefinition? source, ScaleContext context)
    {
        if (high is not { } h || h <= low)
        {
            var single = MeasureDisplay.Show(low, kind, system, source, context.Tolerance, context.Catalog);
            return single.Text + " " + MeasureDisplay.UnitName(single.Unit, single.Value);
        }

        // A range takes its unit from the high end, so "4–6 Tbsp" does not become "¼–⅜ cup".
        var hi = MeasureDisplay.Show(h, kind, system, source, context.Tolerance, context.Catalog);
        var lo = MeasureDisplay.InUnit(low, hi.Unit);
        return lo.Text + "–" + hi.Text + " " + MeasureDisplay.UnitName(hi.Unit, hi.Value);
    }

    /// <summary>BR-27, FR-MEAS-06: whole items; eggs to the nearest half; packages as "1 can + about ⅓ more".</summary>
    private static ScaledIngredient Count(ParsedIngredient p, UnitDefinition? unit, ScaleContext context)
    {
        if (context.Factor == 1m)
        {
            return new ScaledIngredient(IngredientFormatter.Format(p));
        }

        var n = p.Quantity!.Value * context.Factor;
        var nMax = p.QuantityMax is { } m ? m * context.Factor : (decimal?)null;

        if (nMax is { } top && top > n)
        {
            var a = WholeOrFraction(n);
            var b = WholeOrFraction(top);
            return new ScaledIngredient(Compose(Fractions.Format(a) + "–" + Fractions.Format(b) + UnitPart(unit, b), p, p.Note));
        }

        if (unit is null && EggRules.IsEgg(p.Name))
        {
            return new ScaledIngredient(EggRules.Text(n, p, context.IsPrecision));
        }

        if (unit is not null && PackageUnits.Contains(unit.Code))
        {
            return new ScaledIngredient(PackageText(n, unit, p));
        }

        var value = unit?.Code == "stick" ? Fractions.Nearest(n, [2, 4]).Value : WholeOrFraction(n);
        var named = unit is null ? p with { Name = CountName(p.Name, p.Quantity!.Value, value) } : p;
        return new ScaledIngredient(Compose(Fractions.Format(value) + UnitPart(unit, value), named, p.Note));
    }

    /// <summary>"1 onion" → "2 onions" and "2 eggs" → "1 egg" when a count crosses one; otherwise the name as written.</summary>
    public static string CountName(string name, decimal original, decimal scaled)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (original <= 1m && scaled > 1m)
        {
            return FoodMatcher.Plural(name);
        }

        return original > 1m && scaled <= 1m && !name.Any(char.IsUpper) ? FoodMatcher.Singular(name) : name;
    }

    /// <summary>BR-27: the nearest whole item; below one, the nearest ½, ⅓ or ¼ (never zero).</summary>
    public static decimal WholeOrFraction(decimal n) =>
        n >= 1m ? Math.Round(n, MidpointRounding.AwayFromZero) : Fractions.Nearest(n, [2, 3, 4]).Value;

    private static string PackageText(decimal n, UnitDefinition unit, ParsedIngredient p)
    {
        var whole = decimal.Floor(n);
        var rest = n - whole;
        decimal part = 0m;
        if (rest > 0.875m)
        {
            whole += 1m;
        }
        else if (rest >= 0.125m)
        {
            part = Fractions.Nearest(rest, [2, 3, 4]).Value;
        }

        var size = IsSize(p.Note) ? " (" + p.Note + ")" : string.Empty;
        var tailNote = IsSize(p.Note) ? null : p.Note;
        string amount;
        if (whole == 0m)
        {
            amount = "about " + Fractions.Format(part) + " " + unit.Name + size;
        }
        else
        {
            amount = Fractions.Format(whole) + " " + MeasureDisplay.UnitName(unit, whole) + size;
            if (part > 0m)
            {
                amount += " + about " + Fractions.Format(part) + " more";
            }
        }

        return Compose(amount, p, tailNote);
    }

    private static bool IsSize(string? note) => note is { Length: > 0 } && char.IsAsciiDigit(note[0]);

    private static string UnitPart(UnitDefinition? unit, decimal value) =>
        unit is null ? string.Empty : " " + MeasureDisplay.UnitName(unit, value);

    /// <summary>
    /// BR-25, FR-MEAS-03: vague amounts keep their word and show the approximate amount as a hint; above 4 pinches
    /// (or dashes) they become the nearest measure, "about ⅝ tsp (~10 pinches)".
    /// </summary>
    private static ScaledIngredient Vague(ParsedIngredient p, UnitDefinition unit, ScaleContext context)
    {
        var n = p.Quantity!.Value * context.Factor;
        var metric = context.Units is DisplayUnits.Metric or DisplayUnits.Weight;
        string? Approx(decimal count)
        {
            if (unit.ToBase <= 0m)
            {
                return null;
            }

            var ml = count * unit.ToBase;
            return "about " + (metric
                ? Fractions.FormatMetric(ml, "ml") + " ml"
                : Fractions.Format(Fractions.Nearest(ml / UnitCatalog.FindByCode("tsp")!.ToBase, Fractions.Teaspoon).Value) + " tsp");
        }

        if (context.Factor == 1m)
        {
            return new ScaledIngredient(IngredientFormatter.Format(p), Hint: Approx(n));
        }

        var count = Fractions.Nearest(n, [2]).Value;
        if (n <= 4m || unit.ToBase <= 0m)
        {
            return new ScaledIngredient(Compose(Fractions.Format(count) + " " + MeasureDisplay.UnitName(unit, count), p, p.Note),
                Hint: Approx(n));
        }

        var shown = MeasureDisplay.Show(n * unit.ToBase, UnitKind.Volume, metric ? UnitSystem.Metric : UnitSystem.US, null,
            context.Tolerance, context.Catalog);
        var amount = "about " + shown.Text + " " + MeasureDisplay.UnitName(shown.Unit, shown.Value);
        var times = "~" + Fractions.Format(Math.Round(n, MidpointRounding.AwayFromZero)) + " " + unit.PluralName;
        return new ScaledIngredient(Compose(amount, p, p.Note) + " (" + times + ")");
    }

    private static (string? Note, decimal? Low, decimal? High) YieldEstimate(ScaleInput input, UnitDefinition unit, decimal scaledBase,
        ScaleContext context)
    {
        var p = input.Ingredient;
        var conversion = context.Catalog.Yield(input.FoodId, p.State);
        if (conversion is null)
        {
            return (null, null, null);
        }

        decimal grams;
        if (unit.Kind == UnitKind.Mass)
        {
            grams = scaledBase;
        }
        else if (unit.Kind == UnitKind.Volume && conversion.PreparedDensityGPerMl is { } d && d > 0m)
        {
            grams = scaledBase * d;
        }
        else
        {
            return (null, null, null);
        }

        var low = grams * conversion.Ratio;
        var high = grams * conversion.RatioMax;
        var metric = context.Units is DisplayUnits.Metric or DisplayUnits.Weight
            || (context.Units == DisplayUnits.AsWritten && unit.System == UnitSystem.Metric);
        var food = context.Catalog.Food(input.FoodId)?.Name ?? p.Name;
        var note = YieldRules.Note(low, high, metric, conversion.ToState, food, conversion.Basis);
        return (note, low, high);
    }

    /// <summary>The line after the amount: name, preparation, note and "optional", as IngredientFormatter writes them.</summary>
    private static string Compose(string amount, ParsedIngredient p, string? note)
    {
        var sb = new StringBuilder(amount);
        sb.Append(' ').Append(p.Name);
        if (!string.IsNullOrEmpty(p.Preparation))
        {
            sb.Append(", ").Append(p.Preparation);
        }

        if (!string.IsNullOrEmpty(note))
        {
            sb.Append(" (").Append(note).Append(')');
        }

        if (p.IsOptional)
        {
            sb.Append(" (optional)");
        }

        return sb.ToString();
    }
}
