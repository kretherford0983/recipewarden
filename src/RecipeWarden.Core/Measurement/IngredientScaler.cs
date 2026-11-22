using System.Globalization;
using System.Text;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Measurement;

/// <summary>
/// One ingredient to scale: the parsed line, the food it is linked to (ADR 0013) and, for lines such as
/// "1 cup plus 2 tablespoons flour", the extra amount from the note so it scales with the rest.
/// </summary>
public sealed record ScaleInput(ParsedIngredient Ingredient, int? FoodId = null, ParsedIngredient? Plus = null);

/// <summary>
/// How a recipe is being shown: factor (BR-20), units (ADR 0014) and whether it is baking/precision (FR-REC-06).
/// <see cref="Tools"/> are the active tools in My Kitchen (null: no planner, the M2 display); <see cref="PreferWeight"/>
/// turns on BR-22's automatic weight when a scale is among them (ADR 0017, ADR 0018).
/// </summary>
public sealed record ScaleContext(decimal Factor, DisplayUnits Units, bool IsPrecision, MeasurementCatalog Catalog,
    KitchenTools? Tools = null, bool PreferWeight = false)
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
    decimal? PurchaseGrams = null, decimal? PurchaseGramsMax = null)
{
    /// <summary>The scaled amount as a volume, when it can be one (BR-113 rounding check).</summary>
    public decimal? ExactMl { get; init; }

    /// <summary>The volume the shown amount measures after rounding (BR-113).</summary>
    public decimal? ShownMl { get; init; }

    /// <summary>The plan's explanation for the tooltip (9.4, UX-06), when a tool plan was used.</summary>
    public string? Explanation { get; init; }

    /// <summary>True when BR-22 put the weight first.</summary>
    public bool ByWeight { get; init; }
}

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
        var exactMl = unit.Kind == UnitKind.Volume ? low : density is > 0m ? low / density.Value : (decimal?)null;
        if (WeightFirst(unit, food, high, context) is { } weighed)
        {
            var volume = AmountText(low, high, UnitKind.Volume, UnitSystem.US, unit, context, out _, out _);
            var grams = low * density!.Value;
            return new ScaledIngredient(Compose(weighed.Text(grams), p, note), volume, null, yieldNote.Note, yieldNote.Low, yieldNote.High)
            {
                ExactMl = exactMl,
                ShownMl = weighed.RoundedGrams(grams) / density.Value,
                ByWeight = true,
            };
        }

        if (context.Factor == 1m && context.Units == DisplayUnits.AsWritten)
        {
            return new ScaledIngredient(IngredientFormatter.Format(p), YieldNote: yieldNote.Note,
                PurchaseGrams: yieldNote.Low, PurchaseGramsMax: yieldNote.High) { ExactMl = exactMl, ShownMl = exactMl };
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
                secondary = AmountText(low, high, kind, system, unit, context, out _, out _);
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
        var amount = AmountText(low, high, kind, system, source, context, out var shownBase, out var plan);
        var text = Compose(amount, p, note);
        var shownMl = kind == UnitKind.Volume ? shownBase : density is > 0m ? shownBase / density.Value : null;
        return new ScaledIngredient(text, secondary, hint, yieldNote.Note, yieldNote.Low, yieldNote.High)
        {
            ExactMl = exactMl,
            ShownMl = shownMl,
            Explanation = plan?.Explain(),
        };
    }

    /// <summary>
    /// BR-22, ADR 0018: weight first when a scale is in My Kitchen (and accepted for this recipe, ADR 0017), a
    /// density is known, and the recipe is baking/precision or the food is dry. Grams when the scale shows g.
    /// </summary>
    private static WeightDisplay? WeightFirst(UnitDefinition unit, FoodFacts? food, decimal? high, ScaleContext context)
    {
        if (!context.PreferWeight || context.Tools?.Scale is not { } scale || unit.Kind != UnitKind.Volume || high is not null
            || food?.DensityGPerMl is not > 0m || context.Units is not (DisplayUnits.AsWritten or DisplayUnits.US)
            || !(context.IsPrecision || food.Form == FoodForm.Dry))
        {
            return null;
        }

        return new WeightDisplay(scale);
    }

    private sealed class WeightDisplay(KitchenToolInfo scale)
    {
        private readonly bool grams = scale.Supports("g") || !scale.Supports("oz");
        private readonly decimal stepGrams = KitchenTools.GramsPerStep(scale);

        public decimal RoundedGrams(decimal g)
        {
            if (grams)
            {
                var step = scale.ResolutionUnit == "g" ? scale.Resolution!.Value : Math.Max(1m, decimal.Round(stepGrams));
                return Math.Max(step, Math.Round(g / step, MidpointRounding.AwayFromZero) * step);
            }

            var oz = UnitCatalog.FindByCode("oz")!.ToBase;
            var ozStep = scale.ResolutionUnit == "oz" ? scale.Resolution!.Value : 0.05m;
            return Math.Max(ozStep, Math.Round(g / oz / ozStep, MidpointRounding.AwayFromZero) * ozStep) * oz;
        }

        public string Text(decimal g)
        {
            var rounded = RoundedGrams(g);
            if (grams)
            {
                return rounded.ToString("0.###", CultureInfo.InvariantCulture) + " g";
            }

            var oz = rounded / UnitCatalog.FindByCode("oz")!.ToBase;
            return Math.Round(oz, 2).ToString("0.##", CultureInfo.InvariantCulture) + " oz";
        }
    }

    private static string AmountText(decimal low, decimal? high, UnitKind kind, UnitSystem system, UnitDefinition? source, ScaleContext context,
        out decimal? shownBase, out ToolPlan? plan)
    {
        plan = null;
        if (high is not { } h || h <= low)
        {
            // FR-MEAS-02, ADR 0018: US volume is made with the tools in My Kitchen when a plan fits.
            if (context.Tools is { } tools && kind == UnitKind.Volume && system == UnitSystem.US
                && (source is null || source.Code is "tsp" or "tbsp" or "cup")
                && DisplayPlanner.Plan(low, tools, context.Tolerance) is { } p)
            {
                plan = p;
                shownBase = p.PlannedMl;
                return p.Text;
            }

            var single = MeasureDisplay.Show(low, kind, system, source, context.Tolerance, context.Catalog);
            shownBase = single.Value * single.Unit.ToBase;
            return single.Text + " " + MeasureDisplay.UnitName(single.Unit, single.Value);
        }

        shownBase = null;

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
