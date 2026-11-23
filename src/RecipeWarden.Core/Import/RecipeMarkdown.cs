using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Import;

/// <summary>
/// recipe-md v1 (section 9.7, FR-IMP-04, FR-EXP-02, AC-14, ADR 0025): YAML front matter, then "# Title", a
/// description, "## Ingredients" (groups as "### Name", lines as "- …"), "## Steps" (sections as "### Name",
/// numbered steps, a step heading as a bold first line), "## Notes" and "## Source". An optional "## Changes" section
/// (BR-95) is added to the notes. Optional front-matter keys beyond the spec example — <c>total_minutes</c>,
/// <c>precision</c>, <c>pan</c>, <c>finished_density</c> — and ranges written as "6-8" keep every editable field.
/// </summary>
public static partial class RecipeMarkdown
{
    public const string Format = "recipe-md/1";

    [GeneratedRegex(@"^(?<n>\d{1,3})[.)]\s+(?<t>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex NumberedItem();

    [GeneratedRegex(@"^(?:[-*+]|\d{1,3}[.)])\s+(?<t>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\*\*(?<t>[^*]{1,120})\*\*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex BoldLine();

    [GeneratedRegex(@"^!\[[^\]]*\]\([^)]*\)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ImageLine();

    /// <summary>True when the text starts with front matter declaring <c>format: recipe-md/1</c>.</summary>
    public static bool IsRecipeMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var lines = Lines(text);
        var first = lines.FindIndex(l => l.Trim().Length > 0);
        return first >= 0 && lines[first].Trim() == "---" && StartsDocument(lines, first);
    }

    /// <summary>Reads every recipe in the text (one per front-matter block).</summary>
    public static IReadOnlyList<ImportedRecipe> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = Lines(text);
        var starts = Enumerable.Range(0, lines.Count).Where(i => lines[i].Trim() == "---" && StartsDocument(lines, i)).ToList();
        var recipes = new List<ImportedRecipe>();
        for (var k = 0; k < starts.Count; k++)
        {
            var start = starts[k];
            var end = k + 1 < starts.Count ? starts[k + 1] : lines.Count;
            var close = lines.FindIndex(start + 1, l => l.Trim() == "---");
            if (close < 0 || close >= end)
            {
                continue;
            }

            var meta = FrontMatter.Parse(string.Join('\n', lines.Skip(start + 1).Take(close - start - 1)));
            recipes.Add(ReadOne(meta, lines.Skip(close + 1).Take(end - close - 1).ToList()));
        }

        return recipes;
    }

    private static bool StartsDocument(List<string> lines, int i)
    {
        for (var j = i + 1; j < lines.Count && j < i + 40; j++)
        {
            var t = lines[j].Trim();
            if (t == "---")
            {
                return false;
            }

            if (t.StartsWith("format:", StringComparison.OrdinalIgnoreCase))
            {
                return FrontMatter.Scalar(t[7..]).Trim().Equals(Format, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static ImportedRecipe ReadOne(FrontMatter.Map meta, List<string> body)
    {
        string? title = meta.Text("title");
        var description = new List<string>();
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        foreach (var line in body)
        {
            // Headings start at the first column; the writer indents body lines that look like headings.
            var t = line.TrimEnd();
            if (t.StartsWith("# ", StringComparison.Ordinal))
            {
                title ??= t[2..].Trim();
                current = null;
                continue;
            }

            if (t.StartsWith("## ", StringComparison.Ordinal))
            {
                var name = t[3..].Trim();
                if (!sections.TryGetValue(name, out current))
                {
                    current = [];
                    sections[name] = current;
                }

                continue;
            }

            (current ?? description).Add(line);
        }

        var notes = Paragraphs(Section(sections, "Notes"));
        var changes = Paragraphs(Section(sections, "Changes"));
        if (changes is not null)
        {
            notes = (notes is null ? string.Empty : notes + "\n\n") + "Changes:\n" + changes;
        }

        var source = meta.Child("source");
        var type = source?.Text("type")?.Trim().ToLowerInvariant() switch
        {
            "web" => SourceType.Web,
            "file" => SourceType.File,
            "personal" => SourceType.Personal,
            "manual" => SourceType.Manual,
            _ => (SourceType?)null,
        };

        return new ImportedRecipe
        {
            Title = ImportText.Line(title, 200),
            Description = ImportText.Block(Paragraphs(description), RecipeEdit.MaxDescriptionLength),
            Servings = Blank(meta.Text("servings")),
            Yield = Blank(meta.Text("yield")),
            PrepTime = Blank(meta.Text("prep_minutes")),
            CookTime = Blank(meta.Text("cook_minutes")),
            TotalTime = Blank(meta.Text("total_minutes")),
            IsPrecision = string.Equals(meta.Text("precision"), "true", StringComparison.OrdinalIgnoreCase),
            Pan = Blank(meta.Text("pan")),
            FinishedDensity = Blank(meta.Text("finished_density")),
            Tags = meta.List("tags").Select(t => ImportText.Line(t, 100)).Where(t => t is not null).Select(t => t!).ToList(),
            Ingredients = ReadIngredients(Section(sections, "Ingredients")),
            Steps = ReadSteps(Section(sections, "Steps") ?? Section(sections, "Directions") ?? Section(sections, "Method")),
            Notes = ImportText.Block(notes, RecipeEdit.MaxNotesLength),
            SourceType = type,
            SourceName = Blank(source?.Text("name")),
            SourceAuthor = Blank(source?.Text("author")),
            SourceUrl = Blank(source?.Text("url")),
            Method = ExtractionMethod.RecipeMarkdown,
        };
    }

    private static List<string>? Section(Dictionary<string, List<string>> sections, string name) =>
        sections.TryGetValue(name, out var s) ? s : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Paragraphs(List<string>? lines)
    {
        if (lines is null)
        {
            return null;
        }

        var text = string.Join('\n', lines.Select(l => l.TrimEnd())).Trim('\n', ' ');
        return text.Length == 0 ? null : text;
    }

    private static string? ReadIngredients(List<string>? lines)
    {
        if (lines is null)
        {
            return null;
        }

        var output = new List<string>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0 || ImageLine().IsMatch(t))
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                output.Add(t[4..].Trim().TrimEnd(':') + ":");
                continue;
            }

            var m = ListItem().Match(t);
            output.Add(m.Success ? m.Groups["t"].Value.Trim() : t);
        }

        var cleaned = ImportText.Lines(output);
        return cleaned.Count == 0 ? null : string.Join('\n', cleaned);
    }

    private static string? ReadSteps(List<string>? lines)
    {
        if (lines is null)
        {
            return null;
        }

        var steps = new List<StepText>();
        string? section = null;
        string? title = null;
        StringBuilder? text = null;

        void Flush()
        {
            if (text is not null)
            {
                var t = ImportText.Line(text.ToString(), StepTextRules.MaxStepLength);
                if (!string.IsNullOrEmpty(t))
                {
                    steps.Add(new StepText(section, t, title));
                }
                else if (title is not null)
                {
                    steps.Add(new StepText(section, title));
                }
            }

            text = null;
            title = null;
        }

        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0 || ImageLine().IsMatch(t))
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                Flush();
                section = ImportText.Line(t[4..].TrimEnd(':'), 120);
                continue;
            }

            var item = ListItem().Match(t);
            if (item.Success && (NumberedItem().IsMatch(t) || char.IsWhiteSpace(line, 0) is false))
            {
                Flush();
                var first = item.Groups["t"].Value.Trim();
                var bold = BoldLine().Match(first);
                if (bold.Success)
                {
                    title = ImportText.Line(bold.Groups["t"].Value, StepTextRules.MaxTitleLength);
                    text = new StringBuilder();
                }
                else
                {
                    text = new StringBuilder(first);
                }

                continue;
            }

            if (text is null)
            {
                text = new StringBuilder(t);
            }
            else
            {
                text.Append(text.Length > 0 ? " " : string.Empty).Append(t);
            }
        }

        Flush();
        return steps.Count == 0 ? null : StepTextRules.Join(steps);
    }

    /// <summary>
    /// Writes one recipe as recipe-md v1. <paramref name="scale"/> (other than 1) is written as <c>scale:</c> when the
    /// ingredient lines are already scaled (ADR 0031). <paramref name="finishImage"/> and <paramref name="stepImages"/>
    /// are relative paths inside an export .zip ("images/finish.webp"); without them no image lines are written.
    /// </summary>
    public static string Write(RecipeEdit recipe, string? slug = null, DateOnly? imported = null, decimal? scale = null,
        string? finishImage = null, IReadOnlyList<string?>? stepImages = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("format: ").Append(Format).Append('\n');
        Key(sb, "title", recipe.Title);
        Key(sb, "slug", slug);
        if (recipe.Servings is { } servings)
        {
            sb.Append("servings: ").Append(FrontMatter.Quote(Range(servings, recipe.ServingsMax))).Append('\n');
        }

        Key(sb, "yield", recipe.YieldText);
        Minutes(sb, "prep_minutes", recipe.PrepMinutes, recipe.PrepMinutesMax);
        Minutes(sb, "cook_minutes", recipe.CookMinutes, recipe.CookMinutesMax);
        Minutes(sb, "total_minutes", recipe.TotalMinutes, recipe.TotalMinutesMax);
        if (recipe.IsPrecision)
        {
            sb.Append("precision: true\n");
        }

        Key(sb, "pan", recipe.PanText);
        if (recipe.FinishedDensityGPerMl is { } density)
        {
            sb.Append("finished_density: ").Append(density.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        if (recipe.Tags.Count > 0)
        {
            sb.Append("tags: [").Append(string.Join(", ", recipe.Tags.Select(FrontMatter.Quote))).Append("]\n");
        }

        Key(sb, "image", finishImage);
        if (scale is { } factor && factor != 1m)
        {
            sb.Append("scale: ").Append(factor.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        }

        sb.Append("source:\n");
        sb.Append("  type: ").Append(recipe.SourceType.ToString().ToLowerInvariant()).Append('\n');
        Key(sb, "name", recipe.SourceName, "  ");
        Key(sb, "author", recipe.SourceAuthor, "  ");
        Key(sb, "url", recipe.SourceUrl, "  ");
        if (imported is { } date)
        {
            sb.Append("  imported: ").Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        }

        sb.Append("---\n\n");
        sb.Append("# ").Append(OneLine(recipe.Title)).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(recipe.Description))
        {
            sb.Append(Escape(recipe.Description.Trim())).Append("\n\n");
        }

        sb.Append("## Ingredients\n\n");
        foreach (var line in (recipe.IngredientsText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            sb.Append(IsGroupLine(t) ? "\n### " + t.TrimEnd(':').Trim() + "\n" : "- " + t + "\n");
        }

        sb.Append("\n## Steps\n\n");
        string? section = null;
        var n = 0;
        foreach (var step in StepTextRules.Split(recipe.StepsText))
        {
            if (step.SectionName is not null && step.SectionName != section)
            {
                sb.Append("\n### ").Append(step.SectionName).Append("\n\n");
            }

            section = step.SectionName;
            n++;
            sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(". ");
            if (step.Title is not null)
            {
                sb.Append("**").Append(step.Title).Append("**\n   ");
            }

            sb.Append(step.Text).Append('\n');
            if (stepImages is not null && n - 1 < stepImages.Count && stepImages[n - 1] is { Length: > 0 } image)
            {
                sb.Append("   ![Step ").Append(n.ToString(CultureInfo.InvariantCulture)).Append("](").Append(image).Append(")\n");
            }
        }

        if (!string.IsNullOrWhiteSpace(recipe.Notes))
        {
            sb.Append("\n## Notes\n\n").Append(Escape(recipe.Notes.Trim())).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(recipe.SourceName) || !string.IsNullOrWhiteSpace(recipe.SourceUrl))
        {
            sb.Append("\n## Source\n\n");
            var name = OneLine(recipe.SourceName ?? recipe.SourceUrl);
            sb.Append(string.IsNullOrWhiteSpace(recipe.SourceUrl) ? name : "[" + name + "](" + recipe.SourceUrl!.Trim() + ")");
            if (!string.IsNullOrWhiteSpace(recipe.SourceAuthor))
            {
                sb.Append(" by ").Append(OneLine(recipe.SourceAuthor));
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>An ingredient-group line in the editor text: ends with ":" and holds no amount.</summary>
    private static bool IsGroupLine(string line) => line.EndsWith(':') && !line.Any(char.IsDigit);

    /// <summary>Lines that would read as headings or front matter are indented by one space.</summary>
    private static string Escape(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => l.StartsWith('#') || l.Trim() == "---" ? " " + l : l));

    private static string OneLine(string? text) => (text ?? string.Empty).Replace('\n', ' ').Trim();

    private static void Key(StringBuilder sb, string key, string? value, string indent = "")
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.Append(indent).Append(key).Append(": ").Append(FrontMatter.Quote(value.Trim())).Append('\n');
        }
    }

    private static void Minutes(StringBuilder sb, string key, int? min, int? max)
    {
        if (min is { } low)
        {
            sb.Append(key).Append(": ");
            sb.Append(max is { } high
                ? FrontMatter.Quote(string.Create(CultureInfo.InvariantCulture, $"{low}-{high}"))
                : low.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
        }
    }

    private static string Range(decimal min, decimal? max) =>
        max is { } high
            ? string.Create(CultureInfo.InvariantCulture, $"{min}-{high}")
            : min.ToString(CultureInfo.InvariantCulture);

    private static List<string> Lines(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
}
