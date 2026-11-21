using System.Text;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Recipes;

/// <summary>One step as entered: optional section heading ("For the sauce") and the instruction text.</summary>
public sealed record StepText(string? SectionName, string Text);

/// <summary>
/// FR-REC-02, W2 step 4: steps are typed or pasted one per line. Leading numbering ("1.", "2)", "Step 3:", "-") is
/// removed; a short line ending in ":" names the section for the steps after it.
/// </summary>
public static partial class StepTextRules
{
    public const int MaxStepLength = 4000;
    public const int MaxSteps = 200;
    private const int MaxHeadingWords = 8;

    [GeneratedRegex(@"^(?:step\s*\d+\s*[:.)-]?|\d+\s*[.)]|[-*•])\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Numbering();

    public static IReadOnlyList<StepText> Split(string? text)
    {
        var steps = new List<StepText>();
        if (string.IsNullOrEmpty(text))
        {
            return steps;
        }

        string? section = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.EndsWith(':') && line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxHeadingWords)
            {
                var name = line.TrimEnd(':').Trim();
                if (name.Length > 0 && !Numbering().IsMatch(line))
                {
                    section = name;
                    continue;
                }
            }

            var stripped = Numbering().Replace(line, string.Empty, 1).Trim();
            if (stripped.Length > 0)
            {
                steps.Add(new StepText(section, stripped));
            }
        }

        return steps;
    }

    /// <summary>Back to editable text, writing a heading line whenever the section changes.</summary>
    public static string Join(IEnumerable<StepText> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var sb = new StringBuilder();
        string? section = null;
        foreach (var step in steps)
        {
            if (step.SectionName is not null && !string.Equals(step.SectionName, section, StringComparison.Ordinal))
            {
                sb.Append(step.SectionName).Append(":\n");
            }

            section = step.SectionName;
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

        return steps.Any(s => s.Text.Length > MaxStepLength) ? "Each step is limited to 4000 characters" : null;
    }
}
