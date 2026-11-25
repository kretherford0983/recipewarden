using System.Globalization;
using System.Text;
using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Importers.Tests;

/// <summary>
/// AC-03: the parser passes at least 95% of a 500+ line corpus (Corpus/ingredient-lines.txt). A line passes only
/// when every field matches. As a guard against one whole pattern failing behind a good average, every category
/// must also pass at least 85%. Failures are written to the test output with expected and actual values.
/// </summary>
[Trait("Req", "FR-REC-03")]
[Trait("Req", "AC-03")]
public sealed class FR_REC_03_IngredientCorpusTests(ITestOutputHelper output)
{
    private const decimal OverallGate = 0.95m;
    private const decimal CategoryGate = 0.85m;

    private sealed record Expected(
        int LineNumber, string Category, string Raw, decimal? Quantity, decimal? Max, string? Unit, string Name,
        string? Prep, bool Optional, string? State, string? Note);

    private static readonly Lazy<IReadOnlyList<Expected>> Corpus = new(Load);

    [Fact]
    public void Corpus_has_at_least_500_lines_in_every_category()
    {
        Corpus.Value.Count.ShouldBeGreaterThanOrEqualTo(500);
        Corpus.Value.Select(e => e.Category).Distinct().Order().ShouldBe(
            ["compound", "count", "fraction", "metric", "noqty", "optional", "package", "plain", "prep", "range", "vague"]);
    }

    [Fact]
    public void Parser_passes_95_percent_overall_and_85_percent_per_category()
    {
        var failures = new List<string>();
        var byCategory = new Dictionary<string, (int Pass, int Total)>(StringComparer.Ordinal);

        foreach (var e in Corpus.Value)
        {
            var p = IngredientLineParser.Parse(e.Raw);
            var problems = Compare(e, p);
            var (pass, total) = byCategory.GetValueOrDefault(e.Category);
            byCategory[e.Category] = (pass + (problems.Count == 0 ? 1 : 0), total + 1);
            if (problems.Count > 0)
            {
                failures.Add($"line {e.LineNumber} [{e.Category}] \"{e.Raw}\": {string.Join("; ", problems)}");
            }
        }

        var overall = 1m - ((decimal)failures.Count / Corpus.Value.Count);
        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Overall {overall:P1} ({Corpus.Value.Count - failures.Count}/{Corpus.Value.Count})");
        foreach (var (category, (pass, total)) in byCategory.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {category,-9} {(decimal)pass / total:P1} ({pass}/{total})");
        }

        foreach (var f in failures)
        {
            report.AppendLine(f);
        }

        output.WriteLine(report.ToString());

        overall.ShouldBeGreaterThanOrEqualTo(OverallGate, report.ToString());
        foreach (var (category, (pass, total)) in byCategory)
        {
            ((decimal)pass / total).ShouldBeGreaterThanOrEqualTo(CategoryGate, $"category {category}\n{report}");
        }
    }

    [Fact]
    public void Every_corpus_line_keeps_its_raw_text()
    {
        foreach (var e in Corpus.Value)
        {
            IngredientLineParser.Parse(e.Raw).RawText.ShouldBe(e.Raw.Trim());
        }
    }

    private static List<string> Compare(Expected e, Core.Recipes.ParsedIngredient p)
    {
        var problems = new List<string>();
        if (!SameNumber(e.Quantity, p.Quantity)) problems.Add($"qty {e.Quantity} != {p.Quantity}");
        if (!SameNumber(e.Max, p.QuantityMax)) problems.Add($"max {e.Max} != {p.QuantityMax}");
        if (!SameText(e.Unit, p.UnitCode)) problems.Add($"unit {e.Unit} != {p.UnitCode}");
        if (!SameText(e.Name, p.Name)) problems.Add($"name '{e.Name}' != '{p.Name}'");
        if (!SameText(e.Prep, p.Preparation)) problems.Add($"prep '{e.Prep}' != '{p.Preparation}'");
        if (e.Optional != p.IsOptional) problems.Add($"optional {e.Optional} != {p.IsOptional}");
        if (!SameText(e.State, p.State)) problems.Add($"state {e.State} != {p.State}");
        if (!SameText(e.Note, p.Note)) problems.Add($"note '{e.Note}' != '{p.Note}'");
        return problems;
    }

    private static bool SameNumber(decimal? a, decimal? b) =>
        (a is null && b is null) || (a is not null && b is not null && Math.Abs(a.Value - b.Value) < 0.001m);

    private static bool SameText(string? a, string? b) =>
        string.Equals(string.IsNullOrEmpty(a) ? null : a, string.IsNullOrEmpty(b) ? null : b, StringComparison.OrdinalIgnoreCase);

    private static List<Expected> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "ingredient-lines.txt");
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var result = new List<Expected>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var f = line.Split('|');
            if (f.Length != 8)
            {
                throw new InvalidDataException($"Corpus line {i + 1} has {f.Length} fields: {line}");
            }

            var extras = f[7].Trim().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var notes = extras.Where(x => x.StartsWith("note=", StringComparison.Ordinal)).Select(x => x[5..]).ToArray();
            result.Add(new Expected(
                i + 1,
                f[0].Trim(),
                // Only the one separator space on each side is removed; deliberate inner and trailing spaces stay.
                f[1][1..^1],
                Number(f[2]),
                Number(f[3]),
                Text(f[4]),
                f[5].Trim(),
                Text(f[6]),
                extras.Contains("opt"),
                extras.FirstOrDefault(x => x.StartsWith("state=", StringComparison.Ordinal))?[6..],
                notes.Length > 0 ? string.Join("; ", notes) : null));
        }

        return result;
    }

    private static decimal? Number(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : decimal.Parse(s.Trim(), CultureInfo.InvariantCulture);

    private static string? Text(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
