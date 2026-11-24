using System.Globalization;
using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Kitchen;

/// <summary>Section 8.4 KitchenTool.ToolType.</summary>
public enum ToolType
{
    MeasuringSpoon,
    MeasuringCup,
    LiquidMeasure,
    Scale,
    Thermometer,
    Appliance,
    Pan,
    Container,
}

/// <summary>Section 8.4 KitchenTool.PanShape and Recipe pan shape (ADR 0020).</summary>
public enum PanShape
{
    Rectangle,
    Square,
    Round,
    Loaf,
    Other,
}

/// <summary>
/// One tool in My Kitchen as the engine sees it (FR-TOOL-01). Capacity is in <see cref="CapacityUnit"/> (a unit
/// code: "tsp", "cup", "floz"); a scale has a resolution and the units it shows; a pan has a shape and inches.
/// </summary>
public sealed record KitchenToolInfo(
    int Id,
    string Name,
    ToolType Type,
    decimal? CapacityAmount = null,
    string? CapacityUnit = null,
    decimal? Resolution = null,
    string? ResolutionUnit = null,
    IReadOnlyList<string>? SupportedUnits = null,
    int QuantityOwned = 1,
    decimal DefaultFillPct = 100m,
    PanShape? PanShape = null,
    decimal? PanWidth = null,
    decimal? PanLength = null,
    bool IsActive = true)
{
    /// <summary>Capacity in ml, when the tool has one in a volume unit.</summary>
    public decimal? CapacityMl =>
        CapacityAmount is { } a && UnitCatalog.FindByCode(CapacityUnit) is { Kind: UnitKind.Volume } u ? a * u.ToBase : null;

    public bool Supports(string unitCode) =>
        SupportedUnits?.Contains(unitCode, StringComparer.Ordinal) == true;
}

/// <summary>
/// BR-120: the form fields that belong to a tool type. Only these are shown, and only these are read on save.
/// </summary>
public sealed record KitchenFields(bool Size, bool ScaleUnits, bool Fill, string SizeLabel, string SizeHint, string SizePlaceholder);

/// <summary>A tool to add from /manage/kitchen in one click (ADR 0021).</summary>
public sealed record KitchenPreset(string Key, string Label, IReadOnlyList<KitchenToolInfo> Tools);

/// <summary>FR-TOOL-01/02, BR-30, BR-33, ADR 0021: validation, the standard set and the presets.</summary>
public static class KitchenRules
{
    public const int MaxNameLength = 100;
    public const int MaxQuantity = 99;
    public const decimal MaxPanInches = 30m;

    /// <summary>BR-121: a kitchen scale reads in steps of at most 50 g (about 1.76 oz).</summary>
    public const decimal MaxResolutionGrams = 50m;

    private const decimal GramsPerOunce = 28.349523125m;

    /// <summary>BR-120: which fields a type uses, and how the size field is labelled for it.</summary>
    public static KitchenFields FieldsFor(ToolType type) => type switch
    {
        ToolType.MeasuringSpoon => new(true, false, false, "Size", "The spoon's size, e.g. 1/8 tsp or 1/2 Tbsp.", "1/8 tsp"),
        ToolType.MeasuringCup => new(true, false, false, "Size", "The cup's size, e.g. 2/3 cup.", "2/3 cup"),
        ToolType.LiquidMeasure => new(true, false, false, "Capacity", "How much the jug holds, e.g. 2 cups or 1 quart.", "2 cups"),
        ToolType.Container => new(true, false, true, "Capacity", "How much it holds, e.g. 16 fl oz or 1 quart.", "16 fl oz"),
        ToolType.Scale => new(true, true, false, "Resolution (smallest step)",
            "Printed on the scale as Max … x [step], e.g. \"Max 14000g x 1g\" means 1 g. Enter it in grams (1 g) or ounces (0.05 oz).", "1 g"),
        ToolType.Pan => new(true, false, false, "Pan size", "Width x length in inches, e.g. 9x13, 8x8 square, 9-inch round, 9x5 loaf.", "9x13"),
        _ => new(false, false, false, "Size", string.Empty, string.Empty),
    };

    /// <summary>
    /// BR-120: a tool keeps only the fields its type uses. Anything else in the request (a fill % on a spoon, units on
    /// a pan) is cleared, so a hand-built or stale form cannot store them.
    /// </summary>
    public static KitchenToolInfo OnlyItsFields(KitchenToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var fields = FieldsFor(tool.Type);
        var volume = tool.Type is ToolType.MeasuringSpoon or ToolType.MeasuringCup or ToolType.LiquidMeasure or ToolType.Container;
        return tool with
        {
            CapacityAmount = volume ? tool.CapacityAmount : null,
            CapacityUnit = volume ? tool.CapacityUnit : null,
            Resolution = tool.Type == ToolType.Scale ? tool.Resolution : null,
            ResolutionUnit = tool.Type == ToolType.Scale ? tool.ResolutionUnit : null,
            SupportedUnits = fields.ScaleUnits ? tool.SupportedUnits : null,
            DefaultFillPct = fields.Fill ? tool.DefaultFillPct : 100m,
            PanShape = tool.Type == ToolType.Pan ? tool.PanShape : null,
            PanWidth = tool.Type == ToolType.Pan ? tool.PanWidth : null,
            PanLength = tool.Type == ToolType.Pan ? tool.PanLength : null,
        };
    }

    /// <summary>A scale's step in grams (BR-121), or null.</summary>
    public static decimal? ResolutionGrams(KitchenToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool.Resolution is not { } r ? null : tool.ResolutionUnit switch
        {
            "g" => r,
            "oz" => r * GramsPerOunce,
            _ => null,
        };
    }

    /// <summary>BR-33: ¼, ½ and 1 tsp; 1 Tbsp; ¼, ⅓, ½ and 1 cup. Seeded once (ADR 0021) and assumed when none are active.</summary>
    public static IReadOnlyList<KitchenToolInfo> StandardSet { get; } =
    [
        Spoon(0.25m, "tsp"), Spoon(0.5m, "tsp"), Spoon(1m, "tsp"), Spoon(1m, "tbsp"),
        Cup(0.25m), Cup(1m / 3m), Cup(0.5m), Cup(1m),
    ];

    public static IReadOnlyList<KitchenPreset> Presets { get; } =
    [
        new("standard", "Standard US spoon and cup set", StandardSet),
        new("eighth-tsp", "⅛ tsp spoon", [Spoon(0.125m, "tsp")]),
        new("half-tbsp", "½ Tbsp spoon", [Spoon(0.5m, "tbsp")]),
        new("two-thirds-cup", "⅔ and ¾ cup measures", [Cup(2m / 3m), Cup(0.75m)]),
        new("scale", "Digital scale (1 g / 0.05 oz)",
            [new KitchenToolInfo(0, "Digital scale", ToolType.Scale, Resolution: 1m, ResolutionUnit: "g", SupportedUnits: ["g", "oz"])]),
        new("squeeze-bottle", "16 oz squeeze bottle", [Container("16 oz squeeze bottle", 16m, "floz", 90m)]),
        new("jars", "Pint and quart jars", [Container("Pint jar", 1m, "pint", 90m), Container("Quart jar", 1m, "quart", 90m)]),
        new("freezer", "Freezer containers (1 and 2 quart)", [Container("1 qt freezer container", 1m, "quart", 85m), Container("2 qt freezer container", 2m, "quart", 85m)]),
        new("pans", "Common pans (9x13, 8x8, 9-inch round, 9x5 loaf, half sheet)",
        [
            Pan("9x13 pan", Kitchen.PanShape.Rectangle, 9m, 13m), Pan("8x8 pan", Kitchen.PanShape.Square, 8m, 8m),
            Pan("9-inch round pan", Kitchen.PanShape.Round, 9m, 9m), Pan("9x5 loaf pan", Kitchen.PanShape.Loaf, 5m, 9m),
            Pan("Half sheet pan", Kitchen.PanShape.Rectangle, 13m, 18m),
        ]),
    ];

    public static KitchenToolInfo Spoon(decimal amount, string unit) =>
        new(0, Fractions.Format(Fractions.Nearest(amount, Fractions.Teaspoon).Value) + " " + UnitCatalog.FindByCode(unit)!.Name,
            ToolType.MeasuringSpoon, amount, unit);

    public static KitchenToolInfo Cup(decimal amount) =>
        new(0, Fractions.Format(Fractions.Nearest(amount, Fractions.Standard).Value) + " cup", ToolType.MeasuringCup, amount, "cup");

    public static KitchenToolInfo Container(string name, decimal amount, string unit, decimal fill) =>
        new(0, name, ToolType.Container, amount, unit, DefaultFillPct: fill);

    public static KitchenToolInfo Pan(string name, PanShape shape, decimal width, decimal length) =>
        new(0, name, ToolType.Pan, PanShape: shape, PanWidth: width, PanLength: length);

    /// <summary>Errors for an add or edit; empty when the tool can be saved.</summary>
    public static IReadOnlyList<string> Validate(KitchenToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(tool.Name) || tool.Name.Trim().Length > MaxNameLength)
        {
            errors.Add($"Enter a name of up to {MaxNameLength} characters");
        }

        if (tool.QuantityOwned is < 1 or > MaxQuantity)
        {
            errors.Add($"How many you own must be between 1 and {MaxQuantity}");
        }

        if (tool.DefaultFillPct is < 1m or > 100m)
        {
            errors.Add("Fill % must be between 1 and 100");
        }

        switch (tool.Type)
        {
            case ToolType.MeasuringSpoon or ToolType.MeasuringCup or ToolType.LiquidMeasure or ToolType.Container:
                if (tool.CapacityAmount is not > 0m || tool.CapacityMl is null)
                {
                    errors.Add("Enter the capacity with a volume unit, e.g. 1/8 tsp or 16 fl oz");
                }
                else if (tool.Type == ToolType.MeasuringSpoon && tool.CapacityMl > 30m)
                {
                    errors.Add("A measuring spoon holds at most 2 Tbsp; use a cup for larger sizes");
                }

                break;
            case ToolType.Scale:
                if (tool.Resolution is not > 0m || tool.ResolutionUnit is not ("g" or "oz"))
                {
                    errors.Add("Enter the scale's resolution in g or oz, e.g. 1 g");
                }
                else if (ResolutionGrams(tool) > MaxResolutionGrams)
                {
                    errors.Add("A kitchen scale's step is at most 50 g (about 1.75 oz); check the Max … x [step] label");
                }

                if (tool.SupportedUnits is not { Count: > 0 } units || units.Any(u => u is not ("g" or "oz")))
                {
                    errors.Add("Choose the units the scale shows (g, oz or both)");
                }

                break;
            case ToolType.Pan:
                if (tool.PanShape is null || tool.PanWidth is not > 0m || tool.PanWidth > MaxPanInches
                    || (tool.PanShape is not (Kitchen.PanShape.Round or Kitchen.PanShape.Square) && (tool.PanLength is not > 0m || tool.PanLength > MaxPanInches)))
                {
                    errors.Add("Enter the pan's shape and size in inches, e.g. 9x13 or 9-inch round");
                }

                break;
        }

        return errors;
    }

    /// <summary>
    /// /manage/kitchen: a tool from the form. <paramref name="size"/> is a capacity ("⅛ tsp", "16 fl oz") for
    /// spoons, cups, jugs and containers, a step ("1 g", "0.05 oz") for a scale, or a pan ("9x13", "9-inch round").
    /// </summary>
    public static (KitchenToolInfo Tool, IReadOnlyList<string> Errors) FromInput(ToolType type, string? name, string? size,
        IReadOnlyList<string>? shows, int quantity, decimal fillPct)
    {
        var tool = new KitchenToolInfo(0, (name ?? string.Empty).Trim(), type, QuantityOwned: quantity,
            DefaultFillPct: FieldsFor(type).Fill ? fillPct : 100m);
        var sizeErrors = new List<string>();
        switch (type)
        {
            case ToolType.MeasuringSpoon or ToolType.MeasuringCup or ToolType.LiquidMeasure or ToolType.Container:
                if (AmountText.TryParse(Normalize(size), out var capacity) && capacity!.Unit.Kind == UnitKind.Volume)
                {
                    tool = tool with { CapacityAmount = capacity.Value, CapacityUnit = capacity.Unit.Code };
                }

                break;
            case ToolType.Scale:
                // BR-121: a bare number is grams, the unit on the label ("1" → 1 g).
                var scaleSize = Normalize(size);
                if (scaleSize is { Length: > 0 } && decimal.TryParse(scaleSize, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
                {
                    scaleSize += " g";
                }

                if (AmountText.TryParse(scaleSize, out var step) && step!.Unit.Code is "g" or "oz")
                {
                    tool = tool with { Resolution = step.Value, ResolutionUnit = step.Unit.Code, SupportedUnits = shows ?? [] };
                }
                else
                {
                    tool = tool with { SupportedUnits = shows ?? [] };
                }

                break;
            case ToolType.Pan:
                if (PanRules.Parse(size) is { } pan)
                {
                    tool = tool with { PanShape = pan.Shape, PanWidth = pan.Width, PanLength = pan.Length };
                }

                break;
        }

        if (string.IsNullOrWhiteSpace(tool.Name) && Describe(tool) is { Length: > 0 } described)
        {
            tool = tool with { Name = type is ToolType.MeasuringSpoon or ToolType.MeasuringCup ? described : described + " " + type.ToString().ToLowerInvariant() };
        }

        tool = OnlyItsFields(tool);
        return (tool, Validate(tool).Concat(sizeErrors).ToList());
    }

    /// <summary>The size text for an edit form: "⅛ tsp", "16 fl oz", "1 g", "9x13".</summary>
    public static string SizeText(KitchenToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool.Type switch
        {
            ToolType.Scale when tool.Resolution is { } r => r.ToString("0.###", CultureInfo.InvariantCulture) + " " + tool.ResolutionUnit,
            ToolType.Pan when tool.PanWidth is { } w => PanRules.Describe(new Pan(tool.PanShape ?? Kitchen.PanShape.Other, w, tool.PanLength ?? w)),
            _ when tool.CapacityAmount is { } a && UnitCatalog.FindByCode(tool.CapacityUnit) is { } u => AmountText.Format(a, u),
            _ => string.Empty,
        };
    }

    // "⅛ tsp" → "1/8 tsp" so the amount reader takes glyphs too.
    private static string? Normalize(string? size)
    {
        if (size is null)
        {
            return null;
        }

        var text = size;
        foreach (var (glyph, fraction) in new[] { ("⅛", "1/8"), ("¼", "1/4"), ("⅓", "1/3"), ("½", "1/2"), ("⅔", "2/3"), ("¾", "3/4") })
        {
            text = text.Replace(glyph, (text.Length > 0 && char.IsDigit(text[0]) && !text.StartsWith(glyph, StringComparison.Ordinal) ? " " : string.Empty) + fraction, StringComparison.Ordinal);
        }

        return text.Trim();
    }

    /// <summary>"16 fl oz", "⅛ tsp", "1 g", "9x13 in", or empty.</summary>
    public static string Describe(KitchenToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool.Type switch
        {
            ToolType.Scale when tool.Resolution is { } r =>
                r.ToString("0.###", CultureInfo.InvariantCulture) + " " + tool.ResolutionUnit + " steps; shows " + string.Join(", ", tool.SupportedUnits ?? []),
            ToolType.Pan when tool.PanWidth is { } w => PanRules.Describe(new Pan(tool.PanShape ?? Kitchen.PanShape.Other, w, tool.PanLength ?? w)),
            _ when tool.CapacityAmount is { } a && UnitCatalog.FindByCode(tool.CapacityUnit) is { } u =>
                Fractions.Format(Fractions.Nearest(a, Fractions.Teaspoon).Value) + " " + MeasureDisplay.UnitName(u, a),
            _ => string.Empty,
        };
    }
}
