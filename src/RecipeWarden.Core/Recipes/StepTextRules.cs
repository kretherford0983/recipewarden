using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Recipes;

/// <summary>
/// One step as entered: optional section heading ("For the sauce"), the instruction text, and an optional step
/// heading ("Preheat oven", ADR 0015).
/// </summary>
public sealed record StepText(string? SectionName, string Text, string? Title = null);

/// <summary>
/// FR-REC-02, W2 step 4, ADR 0015: steps are typed or pasted one per line. Leading numbering ("1.", "2)",
/// "Step 3:", "-") is removed; a short line ending in ":" names the section for the steps after it.
/// <para>When the text is a numbered list, lines that follow a numbered item (indented, or not separated by a
/// blank line) continue that step. A wholly bold first line with more lines after it ("1. **Preheat oven**"),
/// or a bold lead-in ending in "." or ":" ("**Sear.** Brown the meat"), becomes the step heading. Bold markers
/// (** and __) are removed: step text is plain text (SEC-22).</para>
/// </summary>
public static partial class StepTextRules
{
    public const int MaxStepLength = 4000;
    public const int MaxSteps = 200;
    public const int MaxTitleLength = 120;
    private const int MaxHeadingWords = 8;

    [GeneratedRegex(@"^(?:step\s*\d+\s*[:.)-]?|\d+\s*[.)]|[-*•])\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Numbering();

    [GeneratedRegex(@"^(?:step\s*\d+\s*[:.)-]?|\d+\s*[.)])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex NumberedItem();

    [GeneratedRegex(@"^(\*\*|__)(?<t>[^*_]{1,120}?)\1$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex WholeEmphasis();

    [GeneratedRegex(@"^(\*\*|__)(?<t>[^*_]{1,120}?)\1(?<p>[:.]?)\s+(?<rest>\S.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex LeadIn();

    private sealed class Draft(string? section, string first)
    {
        public string? Section { get; } = section;
        public string First { get; } = first;
        public List<string> More { get; } = [];
    }

    public static IReadOnlyList<StepText> Split(string? text)
    {
        var steps = new List<StepText>();
        if (string.IsNullOrEmpty(text))
        {
            return steps;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var numbered = lines.Any(l => NumberedItem().IsMatch(l.Trim()));
        string? section = null;
        Draft? current = null;
        var afterBlank = false;
        var drafts = new List<Draft>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                afterBlank = true;
                continue;
            }

            var indented = rawLine.Length > 0 && char.IsWhiteSpace(rawLine[0]);
            if (!(numbered && indented && current is not null) && IsSectionHeading(line, out var name))
            {
                section = name;
                current = null;
                afterBlank = false;
                continue;
            }

            if (numbered && current is not null && !Numbering().IsMatch(line) && (indented || !afterBlank))
            {
                current.More.Add(line);
            }
            else
            {
                current = new Draft(section, Numbering().Replace(line, string.Empty, 1).Trim());
                drafts.Add(current);
                if (!numbered)
                {
                    current = null;
                }
            }

            afterBlank = false;
        }

        foreach (var d in drafts)
        {
            if (Finish(d) is { } step)
            {
                steps.Add(step);
            }
        }

        return steps;
    }

    private static bool IsSectionHeading(string line, out string name)
    {
        name = string.Empty;
        if (!line.EndsWith(':') || line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxHeadingWords
            || Numbering().IsMatch(line))
        {
            return false;
        }

        name = Plain(line.TrimEnd(':').Trim());
        return name.Length > 0;
    }

    private static StepText? Finish(Draft d)
    {
        string? title = null;
        var body = new List<string>();
        if (WholeEmphasis().Match(d.First) is { Success: true } whole && d.More.Count > 0)
        {
            title = whole.Groups["t"].Value.Trim().TrimEnd(':').Trim();
        }
        else if (LeadIn().Match(d.First) is { Success: true } lead
                 && (lead.Groups["p"].Length > 0 || lead.Groups["t"].Value.TrimEnd().EndsWith('.') || lead.Groups["t"].Value.TrimEnd().EndsWith(':')))
        {
            title = lead.Groups["t"].Value.Trim().TrimEnd(':', '.').Trim();
            body.Add(lead.Groups["rest"].Value);
        }
        else if (d.First.Length > 0)
        {
            body.Add(d.First);
        }

        body.AddRange(d.More);
        var text = Plain(string.Join(' ', body));
        title = title is null ? null : Plain(title);
        if (text.Length == 0)
        {
            return string.IsNullOrEmpty(title) ? null : new StepText(d.Section, title);
        }

        return new StepText(d.Section, text, string.IsNullOrEmpty(title) ? null : title);
    }

    /// <summary>Removes bold markers; step text is plain (SEC-22, ADR 0015).</summary>
    private static string Plain(string text) =>
        text.Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>
    /// Back to editable text, writing a heading line whenever the section changes. When any step has a heading the
    /// steps are written as a numbered list ("1. **Heading**" then the text indented), which <see cref="Split"/> reads back.
    /// </summary>
    public static string Join(IEnumerable<StepText> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var list = steps.ToList();
        var numbered = list.Any(s => s.Title is not null);
        var sb = new StringBuilder();
        string? section = null;
        var n = 0;
        foreach (var step in list)
        {
            if (step.SectionName is not null && !string.Equals(step.SectionName, section, StringComparison.Ordinal))
            {
                sb.Append(step.SectionName).Append(":\n");
            }

            section = step.SectionName;
            if (!numbered)
            {
                sb.Append(step.Text).Append('\n');
                continue;
            }

            n++;
            sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(". ");
            if (step.Title is not null)
            {
                sb.Append("**").Append(step.Title).Append("**\n   ");
            }

            sb.Append(step.Text).Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    public static string? Validate(IReadOnlyCollection<StepText> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count > MaxSteps)
        {
            return "A recipe can have at most 200 steps";
        }

        if (steps.Any(s => s.Title is { Length: > MaxTitleLength }))
        {
            return "A step heading is limited to 120 characters";
        }

        return steps.Any(s => s.Text.Length > MaxStepLength) ? "Each step is limited to 4000 characters" : null;
    }
}
