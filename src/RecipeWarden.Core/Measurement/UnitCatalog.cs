using System.Collections.Frozen;

namespace RecipeWarden.Core.Measurement;

/// <summary>Section 8.3 Unit.Kind.</summary>
public enum UnitKind
{
    Volume,
    Mass,
    Count,
    Vague,
}

/// <summary>Section 8.3 Unit.System.</summary>
public enum UnitSystem
{
    US,
    Metric,
    Either,
}

/// <summary>One unit of measure. <see cref="ToBase"/> is ml for volume and vague units, g for mass, 1 for count.</summary>
public sealed record UnitDefinition(
    string Code,
    string Name,
    string PluralName,
    UnitKind Kind,
    UnitSystem System,
    decimal ToBase,
    IReadOnlyList<string> Aliases)
{
    public bool IsVague => Kind == UnitKind.Vague;
}

/// <summary>
/// The standard units, used by the ingredient-line parser (FR-REC-03) and seeded into the Unit table by the M1
/// migration (ADR 0009), so the parser and the database cannot drift apart. Densities, tools and the display
/// planner arrive in M2.
/// Aliases are matched case-insensitively, except the single letters in <see cref="CaseSensitiveAliases"/>,
/// where case carries meaning (t = teaspoon, T = tablespoon).
/// </summary>
public static class UnitCatalog
{
    // Exact US customary factors (NIST): 1 US cup = 236.5882365 ml; 1 oz = 28.349523125 g.
    public static IReadOnlyList<UnitDefinition> All { get; } =
    [
        new("tsp", "tsp", "tsp", UnitKind.Volume, UnitSystem.US, 4.92892159375m,
            ["teaspoon", "teaspoons", "tsp", "tsps", "tsp.", "teasp", "teasp."]),
        new("tbsp", "Tbsp", "Tbsp", UnitKind.Volume, UnitSystem.US, 14.78676478125m,
            ["tablespoon", "tablespoons", "tbsp", "tbsps", "tbsp.", "tbs", "tbs.", "tbl", "tbl.", "tblsp", "tblsp."]),
        new("cup", "cup", "cups", UnitKind.Volume, UnitSystem.US, 236.5882365m,
            ["cup", "cups", "c."]),
        new("floz", "fl oz", "fl oz", UnitKind.Volume, UnitSystem.US, 29.5735295625m,
            ["fl oz", "fl. oz.", "fl.oz.", "fl oz.", "fl. oz", "fluid ounce", "fluid ounces", "floz"]),
        new("pint", "pint", "pints", UnitKind.Volume, UnitSystem.US, 473.176473m,
            ["pint", "pints", "pt", "pt.", "pts"]),
        new("quart", "quart", "quarts", UnitKind.Volume, UnitSystem.US, 946.352946m,
            ["quart", "quarts", "qt", "qt.", "qts"]),
        new("gallon", "gallon", "gallons", UnitKind.Volume, UnitSystem.US, 3785.411784m,
            ["gallon", "gallons", "gal", "gal."]),
        new("ml", "ml", "ml", UnitKind.Volume, UnitSystem.Metric, 1m,
            ["ml", "ml.", "milliliter", "milliliters", "millilitre", "millilitres"]),
        new("l", "l", "l", UnitKind.Volume, UnitSystem.Metric, 1000m,
            ["liter", "liters", "litre", "litres", "ltr"]),
        new("g", "g", "g", UnitKind.Mass, UnitSystem.Metric, 1m,
            ["g", "g.", "gram", "grams", "gm", "gms", "gr"]),
        new("kg", "kg", "kg", UnitKind.Mass, UnitSystem.Metric, 1000m,
            ["kg", "kg.", "kilogram", "kilograms", "kilo", "kilos"]),
        new("oz", "oz", "oz", UnitKind.Mass, UnitSystem.US, 28.349523125m,
            ["oz", "oz.", "ounce", "ounces"]),
        new("lb", "lb", "lb", UnitKind.Mass, UnitSystem.US, 453.59237m,
            ["lb", "lb.", "lbs", "lbs.", "pound", "pounds"]),
        new("pinch", "pinch", "pinches", UnitKind.Vague, UnitSystem.Either, 0.30805759960938m,
            ["pinch", "pinches"]),
        new("dash", "dash", "dashes", UnitKind.Vague, UnitSystem.Either, 0.61611519921875m,
            ["dash", "dashes"]),
        new("smidgen", "smidgen", "smidgens", UnitKind.Vague, UnitSystem.Either, 0.15402879980469m,
            ["smidgen", "smidgens", "smidge"]),
        new("drop", "drop", "drops", UnitKind.Vague, UnitSystem.Either, 0.05m,
            ["drop", "drops"]),
        new("handful", "handful", "handfuls", UnitKind.Vague, UnitSystem.Either, 0m,
            ["handful", "handfuls"]),
        new("can", "can", "cans", UnitKind.Count, UnitSystem.Either, 1m, ["can", "cans", "tin", "tins"]),
        new("jar", "jar", "jars", UnitKind.Count, UnitSystem.Either, 1m, ["jar", "jars"]),
        new("package", "package", "packages", UnitKind.Count, UnitSystem.Either, 1m,
            ["package", "packages", "pkg", "pkg.", "pkgs", "packet", "packets", "pack", "packs"]),
        new("envelope", "envelope", "envelopes", UnitKind.Count, UnitSystem.Either, 1m, ["envelope", "envelopes"]),
        new("bag", "bag", "bags", UnitKind.Count, UnitSystem.Either, 1m, ["bag", "bags"]),
        new("box", "box", "boxes", UnitKind.Count, UnitSystem.Either, 1m, ["box", "boxes"]),
        new("bottle", "bottle", "bottles", UnitKind.Count, UnitSystem.Either, 1m, ["bottle", "bottles"]),
        new("container", "container", "containers", UnitKind.Count, UnitSystem.Either, 1m, ["container", "containers"]),
        new("stick", "stick", "sticks", UnitKind.Count, UnitSystem.Either, 1m, ["stick", "sticks"]),
        new("clove", "clove", "cloves", UnitKind.Count, UnitSystem.Either, 1m, ["clove", "cloves"]),
        new("slice", "slice", "slices", UnitKind.Count, UnitSystem.Either, 1m, ["slice", "slices"]),
        new("sprig", "sprig", "sprigs", UnitKind.Count, UnitSystem.Either, 1m, ["sprig", "sprigs"]),
        new("bunch", "bunch", "bunches", UnitKind.Count, UnitSystem.Either, 1m, ["bunch", "bunches"]),
        new("head", "head", "heads", UnitKind.Count, UnitSystem.Either, 1m, ["head", "heads"]),
        new("stalk", "stalk", "stalks", UnitKind.Count, UnitSystem.Either, 1m, ["stalk", "stalks"]),
        new("piece", "piece", "pieces", UnitKind.Count, UnitSystem.Either, 1m, ["piece", "pieces"]),
    ];

    /// <summary>Single-letter aliases whose case matters.</summary>
    public static FrozenDictionary<string, string> CaseSensitiveAliases { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["t"] = "tsp",
        ["t."] = "tsp",
        ["T"] = "tbsp",
        ["T."] = "tbsp",
        ["c"] = "cup",
        ["C"] = "cup",
        ["l"] = "l",
        ["L"] = "l",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, UnitDefinition> ByCode =
        All.ToFrozenDictionary(u => u.Code, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, UnitDefinition> ByAlias =
        All.SelectMany(u => u.Aliases.Select(a => (a, u)))
            .ToFrozenDictionary(p => p.a, p => p.u, StringComparer.OrdinalIgnoreCase);

    /// <summary>The longest alias, in words, so the parser knows how far ahead to look ("fluid ounces" = 2).</summary>
    public static int MaxAliasWords { get; } = All.SelectMany(u => u.Aliases).Max(a => a.Split(' ').Length);

    public static UnitDefinition? FindByCode(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var unit) ? unit : null;

    /// <summary>Finds a unit by any alias; single letters are matched case-sensitively.</summary>
    public static UnitDefinition? FindByAlias(string? alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return null;
        }

        if (CaseSensitiveAliases.TryGetValue(alias, out var code))
        {
            return ByCode[code];
        }

        return ByAlias.TryGetValue(alias, out var unit) ? unit : null;
    }
}
