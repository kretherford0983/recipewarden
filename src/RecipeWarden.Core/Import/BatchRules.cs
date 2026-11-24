using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Import;

/// <summary>A batch size of a recipe that was folded into the base recipe's notes on import (BR-125).</summary>
public sealed record FoldedBatch(ImportedRecipe Recipe, string Note, string Remark);

/// <summary>
/// BR-124 to BR-126 (FR-IMP-04, owner decisions D1 and D2, ADR 0056): one file can hold the same recipe at several
/// batch sizes ("Kansas Corn Bake (Single Batch)" and "(Double Batch)"). Only the base recipe is imported; what scaling
/// cannot reproduce goes into its notes. Variations with different titles are always separate recipes.
/// </summary>
public static partial class BatchRules
{
    /// <summary>BR-124: share of ingredient names the two recipes must have in common (FR-IMP-07's rule).</summary>
    public const double MinOverlap = ImportRules.DuplicateOverlap;

    /// <summary>BR-124: share of matched amounts that must sit near the common ratio.</summary>
    public const double ConsistentShare = 0.7;

    /// <summary>BR-124: how far from the common ratio a line may be and still count as scaled (±15%).</summary>
    public const decimal RatioTolerance = 0.15m;

    [GeneratedRegex(@"\b(single|double|doubled|triple|tripled|quadruple|half|halved|large|small|big|batch)\b|(?<![\w.])[x×]\s?\d+(?:\.\d+)?\b|\b\d+(?:\.\d+)?\s?[x×](?!\w)|\bfor\s+\d+\b|\bserves\s+\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex BatchWord();

    [GeneratedRegex(@"^(?<base>.+?)\s*\((?<q>[^()]{1,40})\)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"^(?<base>.+?)\s*[-–—:,]\s*(?<q>[^-–—:,]{1,40})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Suffixed();

    [GeneratedRegex(@"^(?<base>.+?)\s+(?<q>(?:(?:single|double|triple|quadruple|half|large|small|big)\s+batch)|(?:[x×]\s?\d+(?:\.\d+)?)|(?:\d+(?:\.\d+)?\s?[x×]))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Trailing();

    /// <summary>BR-124: "Kansas Corn Bake (Double Batch)" → ("Kansas Corn Bake", "Double Batch"); no batch word → (title, null).</summary>
    public static (string Base, string? Qualifier) SplitTitle(string? title)
    {
        var t = (title ?? string.Empty).Trim();
        try
        {
            foreach (var m in new[] { Bracketed().Match(t), Suffixed().Match(t), Trailing().Match(t) })
            {
                if (m.Success && BatchWord().IsMatch(m.Groups["q"].Value))
                {
                    return (m.Groups["base"].Value.Trim(), m.Groups["q"].Value.Trim());
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Not a batch title: keep the recipe as it is.
        }

        return (t, null);
    }

    /// <summary>
    /// BR-124: the common ratio of <paramref name="other"/> to <paramref name="baseLines"/>, or null when they are not
    /// the same recipe at another size. <paramref name="offRatio"/> lists the other recipe's lines that do not follow it.
    /// </summary>
    public static decimal? Ratio(IReadOnlyList<ParsedIngredient> baseLines, IReadOnlyList<ParsedIngredient> other, out IReadOnlyList<string> offRatio)
    {
        ArgumentNullException.ThrowIfNull(baseLines);
        ArgumentNullException.ThrowIfNull(other);
        offRatio = [];
        if (ImportRules.IngredientOverlap(baseLines.Select(l => l.Name), other.Select(l => l.Name)) < MinOverlap)
        {
            return null;
        }

        var byName = new Dictionary<string, ParsedIngredient>(StringComparer.Ordinal);
        foreach (var l in baseLines)
        {
            byName.TryAdd(NameRules.Normalize(l.Name), l);
        }

        var pairs = new List<(ParsedIngredient Line, decimal? Ratio)>();
        foreach (var l in other)
        {
            var match = byName.GetValueOrDefault(NameRules.Normalize(l.Name));
            pairs.Add((l, match is null ? null : AmountRatio(match, l)));
        }

        var ratios = pairs.Where(p => p.Ratio is not null).Select(p => p.Ratio!.Value).Order().ToList();
        if (ratios.Count == 0)
        {
            return null;
        }

        var median = ratios[ratios.Count / 2];
        bool Near(decimal r) => Math.Abs(r - median) <= median * RatioTolerance;
        if ((double)ratios.Count(Near) / ratios.Count < ConsistentShare)
        {
            return null;
        }

        var rounded = RoundRatio(median);
        var otherNames = other.Select(l => NameRules.Normalize(l.Name)).ToHashSet(StringComparer.Ordinal);
        offRatio =
        [
            .. pairs.Where(p => p.Ratio is { } r && !Near(r)).Select(p => p.Line.RawText),
            .. pairs.Where(p => !byName.ContainsKey(NameRules.Normalize(p.Line.Name))).Select(p => p.Line.RawText),
            .. baseLines.Where(l => !otherNames.Contains(NameRules.Normalize(l.Name))).Select(l => "Only in this recipe, not that batch: " + l.RawText),
        ];
        return rounded;
    }

    /// <summary>
    /// BR-124, BR-125, BR-126: folds batch sizes of one recipe into the base recipe. <paramref name="parse"/> reads an
    /// ingredient list (the Importers' line parser). Recipes that are not batch siblings are returned unchanged and in order.
    /// </summary>
    public static IReadOnlyList<ImportedRecipe> Fold(IReadOnlyList<ImportedRecipe> recipes, Func<string?, IReadOnlyList<ParsedIngredient>> parse)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(parse);
        if (recipes.Count < 2)
        {
            return recipes;
        }

        var titles = recipes.Select(r => SplitTitle(r.Title)).ToList();
        var result = recipes.ToList();
        var removed = new HashSet<int>();
        foreach (var group in Enumerable.Range(0, recipes.Count).GroupBy(i => NameRules.Normalize(titles[i].Base), StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count < 2 || group.Key.Length == 0 || members.Count(i => titles[i].Qualifier is null) > 1)
            {
                continue; // BR-126: only batch qualifiers make a group; two plain titles stay as they are
            }

            var baseIndex = members.FirstOrDefault(i => titles[i].Qualifier?.Contains("single", StringComparison.OrdinalIgnoreCase) == true, -1);
            if (baseIndex < 0)
            {
                baseIndex = members.FirstOrDefault(i => titles[i].Qualifier is null, members[0]);
            }

            var baseLines = parse(recipes[baseIndex].Ingredients);
            var folded = result[baseIndex];
            foreach (var i in members.Where(i => i != baseIndex))
            {
                if (Ratio(baseLines, parse(recipes[i].Ingredients), out var off) is not { } ratio)
                {
                    continue;
                }

                var label = titles[i].Qualifier ?? Times(ratio) + " batch";
                var note = Note(recipes[baseIndex], recipes[i], ratio, label, off);
                var remark = $"{recipes[i].Title} looked like a {Times(ratio)} batch of this recipe, so it was added to the notes instead of imported.";
                folded = folded with
                {
                    Notes = string.IsNullOrWhiteSpace(folded.Notes) ? note : folded.Notes + "\n\n" + note,
                    Remarks = [.. folded.Remarks, remark],
                    FoldedBatches = [.. folded.FoldedBatches, new FoldedBatch(recipes[i], note, remark)],
                };
                removed.Add(i);
            }

            if (titles[baseIndex].Qualifier is not null && folded.FoldedBatches.Count > 0)
            {
                folded = folded with { Title = titles[baseIndex].Base };
            }

            result[baseIndex] = folded;
        }

        return result.Where((_, i) => !removed.Contains(i)).ToList();
    }

    /// <summary>BR-125: undoes one fold: the base recipe without that batch's note and remark.</summary>
    public static ImportedRecipe Unfold(ImportedRecipe recipe, int index)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var batch = recipe.FoldedBatches[index];
        var notes = recipe.Notes?.Replace(batch.Note, string.Empty, StringComparison.Ordinal).Trim();
        return recipe with
        {
            Notes = string.IsNullOrEmpty(notes) ? null : notes,
            Remarks = recipe.Remarks.Where(r => r != batch.Remark).ToList(),
            FoldedBatches = recipe.FoldedBatches.Where((_, i) => i != index).ToList(),
        };
    }

    /// <summary>
    /// Owner decision D3 (ADR 0056): ingredient groups that are two versions of one part ("Sauce (Side Dish)" and
    /// "Sauce (Taco Topping)"). Returns the shared names; the review warns, it does not decide.
    /// </summary>
    public static IReadOnlyList<string> AlternativeGroups(IEnumerable<string> ingredientLines)
    {
        ArgumentNullException.ThrowIfNull(ingredientLines);
        var groups = ingredientLines.Where(l => l.EndsWith(':') && !l.Any(char.IsDigit))
            .Select(l => Bracketed().Match(l.TrimEnd(':').Trim()))
            .Where(m => m.Success)
            .Select(m => (Base: m.Groups["base"].Value.Trim(), Version: m.Groups["q"].Value.Trim()))
            .ToList();
        return groups.GroupBy(g => g.Base, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => g.First().Base)
            .ToList();
    }

    public static string AlternativeGroupsRemark(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return "Two versions of " + string.Join(" and ", names.Select(n => "“" + n + "”"))
               + " are listed: keep the one you make most and move the other to the notes, or shopping lists and nutrition count both.";
    }

    /// <summary>"2×", "1.5×".</summary>
    public static string Times(decimal ratio) => ratio.ToString("0.##", CultureInfo.InvariantCulture) + "×";

    /// <summary>BR-125: what scaling the base by <paramref name="ratio"/> does not reproduce, as plain text for the notes.</summary>
    public static string Note(ImportedRecipe baseRecipe, ImportedRecipe other, decimal ratio, string label, IReadOnlyList<string> offRatio)
    {
        ArgumentNullException.ThrowIfNull(baseRecipe);
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(offRatio);
        var sb = new StringBuilder();
        sb.Append(label).Append(" (from import, ").Append(Times(ratio)).Append("):");
        if (offRatio.Count > 0)
        {
            sb.Append("\nIngredients that are not ").Append(Times(ratio)).Append(':');
            foreach (var line in offRatio)
            {
                sb.Append("\n- ").Append(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(other.Servings) && !SameServings(baseRecipe.Servings, other.Servings, ratio))
        {
            sb.Append("\nServes ").Append(other.Servings);
        }

        if (!string.IsNullOrWhiteSpace(other.Pan) && !string.Equals(other.Pan, baseRecipe.Pan, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("\nPan: ").Append(other.Pan);
        }

        var times = new List<string>();
        void Time(string name, string? mine, string? theirs)
        {
            if (!string.IsNullOrWhiteSpace(theirs) && !string.Equals(mine, theirs, StringComparison.Ordinal))
            {
                times.Add(name + " " + theirs);
            }
        }

        Time("prep", baseRecipe.PrepTime, other.PrepTime);
        Time("cook", baseRecipe.CookTime, other.CookTime);
        Time("total", baseRecipe.TotalTime, other.TotalTime);
        if (times.Count > 0)
        {
            sb.Append("\nTimes: ").Append(string.Join(", ", times));
        }

        var theirSteps = StepTextRules.Split(other.Steps);
        if (!theirSteps.Select(Plain).SequenceEqual(StepTextRules.Split(baseRecipe.Steps).Select(Plain), StringComparer.Ordinal))
        {
            sb.Append("\nSteps:");
            for (var i = 0; i < theirSteps.Count; i++)
            {
                sb.Append('\n').Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(Plain(theirSteps[i]));
            }
        }

        var mine = Lines(baseRecipe.Notes).Select(LineMeasureRules.Key).ToHashSet(StringComparer.Ordinal);
        var extra = Lines(other.Notes).Where(l => !mine.Contains(LineMeasureRules.Key(l))).ToList();
        if (extra.Count > 0)
        {
            sb.Append("\nNotes:");
            foreach (var line in extra)
            {
                sb.Append('\n').Append(line);
            }
        }

        return sb.ToString();
    }

    private static string Plain(StepText step) => step.Title is null ? step.Text : step.Title + ": " + step.Text;

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static bool SameServings(string? mine, string? theirs, decimal ratio)
    {
        var a = ServingsRules.ParseText(mine);
        var b = ServingsRules.ParseText(theirs);
        return a.Min is { } min && b.Min == min * ratio && (a.Max ?? min) * ratio == (b.Max ?? b.Min);
    }

    private static decimal? AmountRatio(ParsedIngredient a, ParsedIngredient b)
    {
        if (a.Quantity is not > 0m || b.Quantity is not > 0m)
        {
            return null;
        }

        var ua = UnitCatalog.FindByCode(a.UnitCode);
        var ub = UnitCatalog.FindByCode(b.UnitCode);
        if (ua is null && ub is null)
        {
            return b.Quantity.Value / a.Quantity.Value; // counts: 2 eggs → 4 eggs
        }

        if (ua is null || ub is null || ua.Kind != ub.Kind || ua.IsVague || ub.IsVague)
        {
            return null;
        }

        return b.Quantity.Value * ub.ToBase / (a.Quantity.Value * ua.ToBase);
    }

    // 1.98 → 2, 1.5 stays 1.5, 0.33 → 0.33.
    private static decimal RoundRatio(decimal r)
    {
        var whole = Math.Round(r, MidpointRounding.AwayFromZero);
        if (whole > 0 && Math.Abs(r - whole) <= whole * 0.05m)
        {
            return whole;
        }

        return Math.Round(r * 4, MidpointRounding.AwayFromZero) / 4 is var quarter && quarter > 0 && Math.Abs(r - quarter) <= r * 0.05m
            ? quarter
            : Math.Round(r, 2);
    }
}
