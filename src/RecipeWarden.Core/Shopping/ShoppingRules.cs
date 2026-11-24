using System.Globalization;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Shopping;

/// <summary>
/// One stored shopping line (ADR 0029): a scaled ingredient from a recipe, or a manual item. <see cref="Amount"/> is in
/// base units for <see cref="Kind"/> (ml, g or count); null when the line has no amount ("salt to taste").
/// <see cref="CountUnit"/> is a count unit code ("can", "clove", "stick") for counted lines.
/// </summary>
/// BR-45: <see cref="IsRestock"/> marks a line added by a staples check; <see cref="Section"/> is a custom staple's store section.
public sealed record ShoppingLineInput(
    int Id,
    string Name,
    UnitKind? Kind,
    decimal? Amount,
    decimal? AmountMax,
    string? CountUnit,
    int? FoodId,
    string RawText,
    string? RecipeTitle,
    bool IsManual = false,
    bool IsRestock = false,
    string? Section = null);

/// <summary>BR-44: the answer to "do you have it?" for a staple on one list.</summary>
public enum StapleAnswer
{
    Have,
    Need,
}

/// <summary>
/// What the list needs to know about a food (8.3, ADR 0030). The package is one unit you buy, e.g. butter: 118 ml,
/// "stick"; beans: 1 count of "can".
/// </summary>
public sealed record ShoppingFood(
    int Id,
    string Name,
    decimal? DensityGPerMl,
    string? StoreSection,
    bool IsPantryStaple,
    decimal? PackageBase = null,
    UnitKind? PackageKind = null,
    string? PackageName = null);

/// <summary>
/// One line on the list as shown: everything one food (or name) needs, merged. <see cref="IsPantry"/> items are staples
/// still to check (BR-43); <see cref="Answer"/> is the Have it / Need it answer (BR-44); <see cref="IsRestock"/> items
/// came from a staples check (BR-45).
/// </summary>
public sealed record ShoppingItem(
    string Key,
    string Name,
    string? AmountText,
    string? PackageText,
    string Section,
    bool IsPantry,
    bool IsChecked,
    IReadOnlyList<string> Recipes,
    IReadOnlyList<string> Lines)
{
    public StapleAnswer? Answer { get; init; }
    public bool IsRestock { get; init; }
}

/// <summary>A store section and its items, in walking order.</summary>
public sealed record ShoppingSection(string Name, IReadOnlyList<ShoppingItem> Items);

/// <summary>The merged list (FR-SHOP-02, FR-SHOP-03): sections to buy and the staples to check ("Check before you buy").</summary>
public sealed record ShoppingView(IReadOnlyList<ShoppingSection> Sections, IReadOnlyList<ShoppingItem> Pantry)
{
    public IEnumerable<ShoppingItem> ToBuy => Sections.SelectMany(s => s.Items);

    /// <summary>BR-46: staples nobody has answered yet ("2 staples to check").</summary>
    public int ToCheck => Pantry.Count(i => i.Answer is null);

    /// <summary>ADR 0028: finished when there is something to buy and all of it is ticked. Pantry items do not count.</summary>
    public bool IsFinished => ToBuy.Any() && ToBuy.All(i => i.IsChecked);
}

/// <summary>
/// FR-SHOP-02, FR-SHOP-03, BR-40, BR-41, BR-42, AC-50, AC-51, ADR 0029: merges stored lines into one item per food
/// (or per name when no food matched). Volume and mass merge when the food has a density, counts with counts; what
/// cannot be converted stays as a separate amount on the same item ("1 cup + 2"). A known package size adds the
/// package count: whole and half packages as they are ("2½ sticks"), otherwise rounded up ("buy 2 cans"). Items keep
/// their recipes. Staples a recipe needs wait under "Check before you buy" (BR-43, BR-44).
/// </summary>
public static class ShoppingRules
{
    public const string OtherSection = "Other";
    public const int MaxListNameLength = 80;
    public const int MaxItemLength = 200;
    public const int MaxSectionLength = 40;
    public const int MaxPackageNameLength = 24;
    public const int MaxLinesPerList = 1000;
    public const int MaxRecipesPerList = 50;
    public const string PantryHeading = "Check before you buy";

    /// <summary>BR-45: the source shown for a restock item.</summary>
    public const string RestockSource = "Restock";

    /// <summary>ADR 0030: the walking order of a typical store; unknown sections come before "Other", by name.</summary>
    public static IReadOnlyList<string> SectionOrder { get; } =
    [
        "Produce", "Bakery", "Meat and Seafood", "Dairy and Eggs", "Deli", "Baking", "Pantry", "Canned Goods",
        "Pasta and Grains", "Spices", "Condiments", "Snacks", "Beverages", "Frozen", OtherSection,
    ];

    public const string BadSection = "Choose a store section from the list.";
    public const string BadPackage = "Give a package size above 0 with a name, e.g. 15 oz can, or leave both empty.";
    public const string BadPackageUnit = "Package sizes are a volume, a weight, or a plain count.";

    /// <summary>
    /// ADR 0030, FR-MEAS-05: checks an editor's shopping fields for a food. The package is a size (with a volume or
    /// mass unit code, or none for a count) and a short name; both empty clears it.
    /// </summary>
    public static string? ValidateFood(string? section, decimal? packageSize, string? packageUnit, string? packageName)
    {
        if (section is not null && !SectionOrder.Contains(section, StringComparer.Ordinal))
        {
            return BadSection;
        }

        var hasName = !string.IsNullOrWhiteSpace(packageName);
        if (packageSize is null && !hasName)
        {
            return null;
        }

        if (packageSize is not { } size || size <= 0 || size > 100_000 || !hasName || packageName!.Trim().Length > MaxPackageNameLength
            || packageName.Any(c => !char.IsLetter(c) && c != ' ' && c != '-'))
        {
            return BadPackage;
        }

        return packageUnit is null || UnitCatalog.FindByCode(packageUnit) is { Kind: UnitKind.Volume or UnitKind.Mass } ? null : BadPackageUnit;
    }

    /// <summary>The key a tick is stored under (ADR 0029): the food id, else the normalized name.</summary>
    public static string KeyFor(int? foodId, string name) =>
        foodId is { } id ? "f:" + id.ToString(CultureInfo.InvariantCulture) : "n:" + NameRules.Normalize(name);

    /// <summary>
    /// ADR 0029: the stored amount for one recipe line at <paramref name="factor"/>, in base units. A yield
    /// conversion's purchase grams win (AC-35). "1 cup plus 2 Tbsp" adds the plus amount of the same kind. Vague
    /// amounts ("pinch") and lines without a number store no amount.
    /// </summary>
    public static (UnitKind? Kind, decimal? Amount, decimal? AmountMax, string? CountUnit) Amount(ParsedIngredient line, decimal factor,
        ParsedIngredient? plus = null, decimal? purchaseGrams = null, decimal? purchaseGramsMax = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (purchaseGrams is { } grams && grams > 0)
        {
            return (UnitKind.Mass, grams, purchaseGramsMax is { } gm && gm > grams ? gm : null, null);
        }

        if (line.Quantity is not { } quantity || quantity <= 0 || factor <= 0)
        {
            return (null, null, null, null);
        }

        var unit = UnitCatalog.FindByCode(line.UnitCode);
        if (unit is null && line.UnitCode is not null)
        {
            return (null, null, null, null);
        }

        if (unit?.IsVague == true)
        {
            return (null, null, null, null);
        }

        var kind = unit?.Kind ?? UnitKind.Count;
        var toBase = kind == UnitKind.Count ? 1m : unit!.ToBase;
        var extra = 0m;
        if (plus?.Quantity is { } pq && UnitCatalog.FindByCode(plus.UnitCode) is { } plusUnit && plusUnit.Kind == kind && kind != UnitKind.Count)
        {
            extra = pq * plusUnit.ToBase;
        }

        var low = (quantity * toBase + extra) * factor;
        decimal? high = line.QuantityMax is { } max && max > quantity ? (max * toBase + extra) * factor : null;
        return (kind, low, high, kind == UnitKind.Count ? unit?.Code : null);
    }

    /// <summary>
    /// BR-43, BR-44, BR-45: <paramref name="answers"/> holds the Have it / Need it answers by item key. A staple a recipe
    /// needs waits under "Check before you buy" until answered; Need it (or a restock line) puts it on the list in its
    /// section; Have it keeps it checked off below.
    /// </summary>
    public static ShoppingView Merge(IEnumerable<ShoppingLineInput> lines, IReadOnlyDictionary<int, ShoppingFood> foods,
        IReadOnlySet<string> checkedKeys, UnitSystem system = UnitSystem.US, MeasurementCatalog? catalog = null,
        IReadOnlyDictionary<string, StapleAnswer>? answers = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(foods);
        ArgumentNullException.ThrowIfNull(checkedKeys);
        catalog ??= MeasurementCatalog.Default;
        answers ??= new Dictionary<string, StapleAnswer>(StringComparer.Ordinal);

        var items = new List<ShoppingItem>();
        foreach (var group in lines.Where(l => !string.IsNullOrWhiteSpace(l.Name)).GroupBy(l => KeyFor(l.FoodId, l.Name)))
        {
            var list = group.ToList();
            var food = list[0].FoodId is { } fid && foods.TryGetValue(fid, out var f) ? f : null;
            var (amount, package) = Amounts(list, food, system, catalog);
            var restock = list.Any(l => l.IsRestock);
            StapleAnswer? answer = answers.TryGetValue(group.Key, out var a) ? a : null;
            var staple = food?.IsPantryStaple == true && !restock && answer != StapleAnswer.Need;
            var section = list.Select(l => l.Section).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                          ?? (string.IsNullOrWhiteSpace(food?.StoreSection) ? OtherSection : food!.StoreSection!.Trim());
            var sources = list.Select(l => l.RecipeTitle).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!).Distinct(StringComparer.Ordinal).ToList();
            if (restock)
            {
                sources.Add(RestockSource);
            }

            items.Add(new ShoppingItem(
                group.Key,
                food?.Name ?? list[0].Name.Trim(),
                amount,
                package,
                section,
                staple,
                checkedKeys.Contains(group.Key),
                sources,
                list.Select(l => l.RawText).ToList())
            {
                Answer = answer,
                IsRestock = restock,
            });
        }

        var sections = items.Where(i => !i.IsPantry)
            .GroupBy(i => i.Section, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => SectionRank(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ShoppingSection(g.First().Section, g.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
        var pantry = items.Where(i => i.IsPantry).OrderBy(i => i.Answer is null ? 0 : 1).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new ShoppingView(sections, pantry);
    }

    private static int SectionRank(string section)
    {
        for (var i = 0; i < SectionOrder.Count; i++)
        {
            if (string.Equals(SectionOrder[i], section, StringComparison.OrdinalIgnoreCase))
            {
                return i == SectionOrder.Count - 1 ? 1000 : i;
            }
        }

        return 500;
    }

    /// <summary>The merged amount text and the package text for one item's lines.</summary>
    private static (string? Amount, string? Package) Amounts(List<ShoppingLineInput> lines, ShoppingFood? food, UnitSystem system,
        MeasurementCatalog catalog)
    {
        decimal volume = 0, volumeMax = 0, mass = 0, massMax = 0;
        int volumeLines = 0, massLines = 0;
        var counts = new Dictionary<string, (decimal Low, decimal High)>(StringComparer.Ordinal);
        var density = food?.DensityGPerMl;

        foreach (var line in lines)
        {
            if (line.Amount is not { } low || line.Kind is null)
            {
                continue;
            }

            var high = line.AmountMax ?? low;
            switch (line.Kind)
            {
                case UnitKind.Volume:
                    volume += low;
                    volumeMax += high;
                    volumeLines++;
                    break;
                case UnitKind.Mass:
                    mass += low;
                    massMax += high;
                    massLines++;
                    break;
                case UnitKind.Count:
                    // "1 stick butter" when the package is a stick: count it as the package's amount.
                    if (food?.PackageName is { } pkg && food.PackageBase is { } size && food.PackageKind is UnitKind.Volume or UnitKind.Mass
                        && string.Equals(line.CountUnit, pkg, StringComparison.OrdinalIgnoreCase))
                    {
                        if (food.PackageKind == UnitKind.Volume)
                        {
                            volume += low * size;
                            volumeMax += high * size;
                            volumeLines++;
                        }
                        else
                        {
                            mass += low * size;
                            massMax += high * size;
                            massLines++;
                        }

                        break;
                    }

                    var unit = line.CountUnit ?? string.Empty;
                    counts[unit] = counts.TryGetValue(unit, out var c) ? (c.Low + low, c.High + high) : (low, high);
                    break;
            }
        }

        // BR-40: volume and mass merge only with a density; the kind shown follows the package, else the most lines.
        UnitKind? merged = null;
        if (density is { } d && d > 0 && volumeLines > 0 && massLines > 0)
        {
            merged = food?.PackageKind is UnitKind.Volume or UnitKind.Mass ? food.PackageKind
                : volumeLines > massLines ? UnitKind.Volume : UnitKind.Mass;
            if (merged == UnitKind.Volume)
            {
                volume += mass / d;
                volumeMax += massMax / d;
                mass = massMax = 0;
                massLines = 0;
            }
            else
            {
                mass += volume * d;
                massMax += volumeMax * d;
                volume = volumeMax = 0;
                volumeLines = 0;
            }
        }

        var parts = new List<string>();
        string? package = null;
        if (volumeLines > 0)
        {
            parts.Add(Show(volume, volumeMax, UnitKind.Volume, system, catalog));
            package ??= Packages(volumeMax, UnitKind.Volume, food);
        }

        if (massLines > 0)
        {
            parts.Add(Show(mass, massMax, UnitKind.Mass, system, catalog));
            package ??= Packages(massMax, UnitKind.Mass, food);
        }

        foreach (var (unitCode, (low, high)) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var unit = UnitCatalog.FindByCode(unitCode);
            var number = Range(low, high);
            parts.Add(unit is null ? number : number + " " + (high > 1 ? unit.PluralName : unit.Name));
            // "6 eggs" with a 12-egg carton: plain counts are packaged by the count per package.
            if (unit is null && food?.PackageKind == UnitKind.Count && food.PackageBase is { } perPack && perPack > 0)
            {
                package ??= Packages(high, UnitKind.Count, food);
            }
        }

        return (parts.Count == 0 ? null : string.Join(" + ", parts), package);
    }

    private static string Show(decimal low, decimal high, UnitKind kind, UnitSystem system, MeasurementCatalog catalog)
    {
        var shown = MeasureDisplay.Show(low, kind, system, null, MeasureDisplay.StandardTolerance, catalog);
        if (high <= low)
        {
            return shown.Text + " " + MeasureDisplay.UnitName(shown.Unit, shown.Value);
        }

        var top = MeasureDisplay.InUnit(high, shown.Unit);
        return shown.Text + "–" + top.Text + " " + MeasureDisplay.UnitName(shown.Unit, top.Value);
    }

    private static string Range(decimal low, decimal high) =>
        high > low ? Fractions.Format(low) + "–" + Fractions.Format(high) : Fractions.Format(low);

    /// <summary>
    /// BR-41: how many packages to buy for <paramref name="amount"/> (the high end of a range). Whole and half packages
    /// are shown as they are; anything else is rounded up to whole packages.
    /// </summary>
    public static string? Packages(decimal amount, UnitKind kind, ShoppingFood? food)
    {
        if (food?.PackageBase is not { } size || size <= 0 || food.PackageName is not { Length: > 0 } name || amount <= 0)
        {
            return null;
        }

        decimal base_;
        if (food.PackageKind == kind)
        {
            base_ = amount;
        }
        else if (food.DensityGPerMl is { } d && d > 0 && kind == UnitKind.Volume && food.PackageKind == UnitKind.Mass)
        {
            base_ = amount * d;
        }
        else if (food.DensityGPerMl is { } d2 && d2 > 0 && kind == UnitKind.Mass && food.PackageKind == UnitKind.Volume)
        {
            base_ = amount / d2;
        }
        else
        {
            return null;
        }

        var packages = base_ / size;
        var half = Fractions.Nearest(packages, [2]);
        var plural = Plural(name);
        if (half.Error <= 0.02m && half.Value > 0)
        {
            return Fractions.Format(half.Value) + " " + (half.Value > 1 ? plural : name);
        }

        var whole = Math.Ceiling(packages - 0.02m);
        whole = Math.Max(1, whole);
        return "buy " + whole.ToString(CultureInfo.InvariantCulture) + " " + (whole > 1 ? plural : name);
    }

    private static string Plural(string name) =>
        UnitCatalog.FindByCode(name.ToLowerInvariant()) is { } unit ? unit.PluralName
        : name.EndsWith('x') || name.EndsWith('s') ? name + "es" : name + "s";
}
