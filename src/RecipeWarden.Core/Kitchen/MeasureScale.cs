using System.Globalization;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Kitchen;

/// <summary>
/// BR-50 (ADR 0018 amendment 1): one set of markings on a cup, jug or adjustable measure: from <see cref="Min"/> to
/// <see cref="Max"/> in steps of <see cref="Step"/>, in <see cref="Unit"/> (a volume unit code). A 4-cup Pyrex has
/// "4 cups by 1/2", "1 quart by 1/3" and "1000 ml by 100"; an Adjust-A-Cup "1/8 to 1 cup by 1/8".
/// </summary>
public sealed record MeasureScale(string Unit, decimal Min, decimal Max, decimal Step)
{
    /// <summary>Every marking, smallest first (Min, Min + Step, … up to Max).</summary>
    public IEnumerable<decimal> Markings()
    {
        for (var v = Min; v <= Max + 0.0001m; v += Step)
        {
            yield return v;
        }
    }

    /// <summary>"⅛ to 1 cup by ⅛", "4 cups by ½" (Min = Step), "1000 ml by 100".</summary>
    public string Describe()
    {
        var unit = UnitCatalog.FindByCode(Unit)!;
        string N(decimal v) => Unit is "ml" or "l" ? v.ToString("0.###", CultureInfo.InvariantCulture) : Fractions.Format(Fractions.Nearest(v, Fractions.Standard).Value);
        var range = Min == Step ? N(Max) : N(Min) + " to " + N(Max);
        return range + " " + MeasureDisplay.UnitName(unit, Max) + " by " + N(Step);
    }
}

/// <summary>BR-50: reading, checking and using a tool's markings.</summary>
public static partial class MeasureScales
{
    public const int MaxScales = 4;
    public const int MaxMarkingsPerScale = 48;

    /// <summary>The units the display planner turns into fills (it shows US cups and spoons, ADR 0018).</summary>
    public static IReadOnlySet<string> PlannerUnits { get; } = new HashSet<string>(StringComparer.Ordinal) { "cup", "tbsp", "tsp" };

    public const string Help = "One line per set of markings, e.g. \"4 cups by 1/2\", \"1 quart by 1/3\", \"1000 ml by 100\", or \"1/8 to 1 cup by 1/8\" for an adjustable cup.";

    [GeneratedRegex(@"^(?:(?<min>[\d./ ]+?)\s*(?:to|-|–)\s*)?(?<max>[\d./ ]+?)\s+(?<unit>[a-zA-Z][a-zA-Z. ]*?)\s+(?:by|in|every|steps? of)\s+(?<step>[\d./ ]+?)(?:\s+[a-zA-Z][a-zA-Z. ]*?)?\s*(?:steps?)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex Line();

    /// <summary>Reads the markings text (one scale per line). Errors name the line that could not be read.</summary>
    public static (IReadOnlyList<MeasureScale> Scales, IReadOnlyList<string> Errors) Parse(string? text)
    {
        var scales = new List<MeasureScale>();
        var errors = new List<string>();
        var lines = (text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count > MaxScales)
        {
            return ([], [$"Enter at most {MaxScales} sets of markings."]);
        }

        foreach (var line in lines)
        {
            if (Read(line) is { } scale)
            {
                if (scale.Max < scale.Min || scale.Step <= 0m || (scale.Max - scale.Min) / scale.Step + 1 > MaxMarkingsPerScale)
                {
                    errors.Add($"\"{line}\": the steps don't fit (at most {MaxMarkingsPerScale} markings, the largest amount last).");
                }
                else
                {
                    scales.Add(scale);
                }
            }
            else
            {
                errors.Add($"Couldn't read \"{line}\". {Help}");
            }
        }

        return (scales, errors);
    }

    /// <summary>Storage form: "cup 0.125 1 0.125|ml 100 1000 100".</summary>
    public static string? Store(IReadOnlyList<MeasureScale>? scales) => scales is not { Count: > 0 } ? null
        : string.Join('|', scales.Select(s => string.Join(' ', s.Unit, N(s.Min), N(s.Max), N(s.Step))));

    /// <summary>Reads <see cref="Store"/>'s form; anything unreadable is skipped.</summary>
    public static IReadOnlyList<MeasureScale>? Load(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var list = new List<MeasureScale>();
        foreach (var part in stored.Split('|'))
        {
            var f = part.Split(' ');
            if (f.Length == 4 && UnitCatalog.FindByCode(f[0]) is { Kind: UnitKind.Volume }
                && decimal.TryParse(f[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var min)
                && decimal.TryParse(f[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var max)
                && decimal.TryParse(f[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var step) && step > 0m)
            {
                list.Add(new MeasureScale(f[0], min, max, step));
            }
        }

        return list.Count == 0 ? null : list;
    }

    private static string N(decimal v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Text for the form, one scale per line.</summary>
    public static string Format(IReadOnlyList<MeasureScale>? scales) =>
        string.Join('\n', (scales ?? []).Select(s => s.Describe()));

    /// <summary>
    /// The markings the planner may use as single fills: virtual tools named after their tool, in cups and spoons only,
    /// from active tools.
    /// </summary>
    public static IReadOnlyList<KitchenToolInfo> Markings(IEnumerable<KitchenToolInfo> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var result = new List<KitchenToolInfo>();
        foreach (var tool in tools.Where(t => t.IsActive && t.Scales is { Count: > 0 }))
        {
            foreach (var scale in tool.Scales!.Where(s => PlannerUnits.Contains(s.Unit)))
            {
                foreach (var amount in scale.Markings())
                {
                    result.Add(new KitchenToolInfo(tool.Id, tool.Name, ToolType.MeasuringCup, amount, scale.Unit) { IsMarking = true });
                }
            }
        }

        return result.GroupBy(m => decimal.Round(m.CapacityMl!.Value, 4)).Select(g => g.First()).ToList();
    }

    // "1½" → "1 1/2", so the number reader sees plain fractions.
    private static string Vulgar(string text)
    {
        var map = new Dictionary<char, string>
        {
            ['½'] = "1/2", ['⅓'] = "1/3", ['⅔'] = "2/3", ['¼'] = "1/4", ['¾'] = "3/4", ['⅛'] = "1/8", ['⅜'] = "3/8", ['⅝'] = "5/8", ['⅞'] = "7/8",
        };
        var sb = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (map.TryGetValue(c, out var f))
            {
                if (sb.Length > 0 && char.IsDigit(sb[^1]))
                {
                    sb.Append(' ');
                }

                sb.Append(f);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// BR-51: the amounts a measuring tool gives (its own size when it is a spoon or cup, and its markings), as
    /// (ml, text) pairs in cups and spoons.
    /// </summary>
    public static IReadOnlyList<(decimal Ml, string Text)> Sizes(KitchenToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var sizes = new List<(decimal, string)>();
        if (tool.Type is ToolType.MeasuringSpoon or ToolType.MeasuringCup && tool.CapacityMl is { } ml && ml > 0m)
        {
            sizes.Add((decimal.Round(ml, 4), KitchenRules.Describe(tool with { Scales = null })));
        }

        foreach (var m in Markings([tool with { IsActive = true }]))
        {
            sizes.Add((decimal.Round(m.CapacityMl!.Value, 4), KitchenRules.Describe(m)));
        }

        return sizes.DistinctBy(s => s.Item1).ToList();
    }

    /// <summary>
    /// BR-51: what retiring <paramref name="tool"/> would mean: sizes no other active tool gives (lost), and sizes
    /// another active tool still gives, with that tool's name (covered).
    /// </summary>
    public static (IReadOnlyList<string> Lost, IReadOnlyList<(string Size, string By)> Covered) IfRetired(KitchenToolInfo tool, IEnumerable<KitchenToolInfo> active)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(active);
        var others = active.Where(t => t.Id != tool.Id && t.IsActive)
            .SelectMany(t => Sizes(t).Select(s => (s.Ml, Name: string.IsNullOrWhiteSpace(t.Name) ? "your " + KitchenRules.Describe(t with { Scales = null }) + " measure" : t.Name)))
            .ToList();
        var lost = new List<string>();
        var covered = new List<(string, string)>();
        foreach (var (ml, text) in Sizes(tool))
        {
            if (others.FirstOrDefault(o => o.Ml == ml) is { Name: { } by })
            {
                covered.Add((text, by));
            }
            else
            {
                lost.Add(text);
            }
        }

        return (lost, covered);
    }

    private static MeasureScale? Read(string line)
    {
        Match m;
        try
        {
            m = Line().Match(Vulgar(line.Replace('\u2009', ' ')));
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        if (!m.Success || UnitCatalog.FindByAlias(m.Groups["unit"].Value.Trim()) is not { Kind: UnitKind.Volume, IsVague: false } unit
            || !ScaleRules.TryParseNumber(m.Groups["max"].Value.Trim(), out var max)
            || !ScaleRules.TryParseNumber(m.Groups["step"].Value.Trim(), out var step) || step <= 0m)
        {
            return null;
        }

        var min = step;
        if (m.Groups["min"].Success && !ScaleRules.TryParseNumber(m.Groups["min"].Value.Trim(), out min))
        {
            return null;
        }

        return new MeasureScale(unit.Code, min, max, step);
    }
}
