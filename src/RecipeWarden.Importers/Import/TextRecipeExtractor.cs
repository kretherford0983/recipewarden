using System.Text.RegularExpressions;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Importers.Import;

/// <summary>
/// FR-IMP-04, FR-IMP-05, W4 step 2: finds a recipe in loosely structured text by its section headings — Markdown
/// headings or lines such as "Ingredients", "Directions:", "METHOD". Used for .md and .txt files, pasted text, Word
/// documents and PDFs (converted to the same Markdown-like lines), and as the heuristic HTML fallback (FR-IMP-01).
/// Several "# Title" headings in one file give several recipes.
/// </summary>
public static partial class TextRecipeExtractor
{
    private enum Part
    {
        Intro,
        Ingredients,
        Steps,
        Notes,
        Other,
    }

    private static readonly string[] IngredientWords =
        ["ingredients", "ingredient list", "what you need", "what you'll need", "you will need", "you'll need", "shopping list"];

    private static readonly string[] StepWords =
        ["directions", "instructions", "method", "steps", "preparation", "procedure", "how to make it", "how to make", "to make"];

    private static readonly string[] NoteWords =
        ["notes", "note", "tips", "cook's notes", "cooks notes", "recipe notes", "variations", "storage"];

    private static readonly string[] OtherWords =
        ["nutrition", "nutrition facts", "equipment", "comments", "reviews", "video", "related recipes"];

    [GeneratedRegex(@"^(?<h>#{1,6})\s+(?<t>.+?)\s*#*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"^(?:[-*+•▢□☐◻▪●○]|\d{1,3}[.)])\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex MarkdownImage();

    [GeneratedRegex(@"\[(?<t>[^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(\*\*|__|`)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"^(?<k>serves|servings|yield|yields|makes|prep(?:aration)?(?:\s+time)?|cook(?:ing)?(?:\s+time)?|total(?:\s+time)?|ready\s+in)\s*[:\-–]?\s*(?<v>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Meta();

    [GeneratedRegex(@"\s+[·•|]\s+|\s{3,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex MetaSeparator();

    /// <summary>Reads every recipe in the text; <paramref name="fallbackTitle"/> (e.g. the file name) is used when a recipe has no title.</summary>
    public static IReadOnlyList<ImportedRecipe> Extract(string? text, string? fallbackTitle = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lines = text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        var titleLines = lines.Select((l, i) => (l, i)).Where(x => x.l.StartsWith("# ", StringComparison.Ordinal)).Select(x => x.i).ToList();
        var chunks = new List<List<string>>();
        if (titleLines.Count > 1)
        {
            for (var k = 0; k < titleLines.Count; k++)
            {
                var end = k + 1 < titleLines.Count ? titleLines[k + 1] : lines.Count;
                chunks.Add(lines.Skip(titleLines[k]).Take(end - titleLines[k]).ToList());
            }
        }
        else
        {
            chunks.Add(lines);
        }

        return chunks.Select(c => ExtractOne(c, fallbackTitle)).Where(r => r.HasContent).ToList();
    }

    private static ImportedRecipe ExtractOne(List<string> lines, string? fallbackTitle)
    {
        string? title = null;
        var intro = new List<string>();
        var ingredients = new List<string>();
        var steps = new List<string>();
        var notes = new List<string>();
        var meta = new List<(string Key, string Value)>();
        var part = Part.Intro;
        var sawSection = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                Target(part)?.Add(string.Empty);
                continue;
            }

            var heading = MarkdownHeading().Match(trimmed);
            var headingText = heading.Success ? Inline(heading.Groups["t"].Value) : null;
            var level = heading.Success ? heading.Groups["h"].Value.Length : 0;

            if (level == 1 && title is null)
            {
                title = headingText;
                continue;
            }

            var section = SectionOf(headingText ?? trimmed, heading.Success);
            if (section is not null)
            {
                part = section.Value;
                sawSection = true;
                continue;
            }

            if (heading.Success)
            {
                // A sub-heading inside a section: an ingredient group or a step section; elsewhere it ends the section.
                if (part is Part.Ingredients or Part.Steps)
                {
                    Target(part)!.Add(headingText!.TrimEnd(':') + ":");
                }
                else if (title is null && !sawSection && part == Part.Intro)
                {
                    title = headingText;
                }

                continue;
            }

            if (part == Part.Intro)
            {
                // "Serves 8 · Prep time: 15 minutes" holds two facts.
                var pieces = MetaSeparator().Split(Inline(trimmed)).Select(p => Meta().Match(p.Trim())).ToList();
                if (pieces.All(m => m.Success))
                {
                    meta.AddRange(pieces.Select(m => (m.Groups["k"].Value.ToLowerInvariant(), m.Groups["v"].Value)));
                    continue;
                }

                if (title is null && intro.All(string.IsNullOrEmpty) && trimmed.Length <= 120)
                {
                    title = Inline(trimmed);
                    continue;
                }
            }

            Target(part)?.Add(part == Part.Steps ? Steps(line) : Inline(trimmed));
        }

        var (servings, yield, remark) = ImportRules.ServingsAndYield(meta.Where(m => m.Key is "serves" or "servings" or "yield" or "yields" or "makes").Select(m => m.Value));
        string? Time(string prefix) => meta.Where(m => m.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(m => ImportRules.Minutes(m.Value)).FirstOrDefault(v => v is not null);

        var ingredientText = ImportText.Lines(ingredients.Where(l => l.Length > 0).Select(l => Bullet().Replace(l, string.Empty)));
        var stepList = StepTextRules.Split(string.Join('\n', steps));
        var notesText = ImportText.Block(string.Join('\n', notes), RecipeEdit.MaxNotesLength);
        if (remark is not null)
        {
            notesText = notesText is null ? remark : remark + "\n\n" + notesText;
        }

        return new ImportedRecipe
        {
            Title = ImportText.Line(title ?? fallbackTitle, 200),
            Description = ImportText.Block(string.Join('\n', intro), RecipeEdit.MaxDescriptionLength),
            Servings = servings,
            Yield = yield,
            PrepTime = Time("prep"),
            CookTime = Time("cook"),
            TotalTime = Time("total") ?? Time("ready"),
            Ingredients = ingredientText.Count == 0 ? null : string.Join('\n', ingredientText),
            Steps = stepList.Count == 0 ? null : StepTextRules.Join(stepList),
            Notes = notesText,
            Remarks = remark is null ? [] : [remark],
            Method = ExtractionMethod.Headings,
        };

        List<string>? Target(Part p) => p switch
        {
            Part.Intro => intro,
            Part.Ingredients => ingredients,
            Part.Steps => steps,
            Part.Notes => notes,
            _ => null,
        };
    }

    /// <summary>Which section a heading or a stand-alone line ("Ingredients:", "DIRECTIONS") starts, if any.</summary>
    private static Part? SectionOf(string text, bool isHeading)
    {
        var t = Inline(text).TrimEnd(':', '.', ' ').Trim().ToLowerInvariant();
        if (t.Length == 0 || t.Length > 40)
        {
            return null;
        }

        // A plain line must be nothing but the keyword; a heading may add words ("Ingredients for 4").
        bool Has(string[] words) => words.Any(w => t == w || (isHeading && (t.StartsWith(w + " ", StringComparison.Ordinal) || t.EndsWith(" " + w, StringComparison.Ordinal))));
        if (Has(IngredientWords))
        {
            return Part.Ingredients;
        }

        if (Has(StepWords))
        {
            return Part.Steps;
        }

        if (Has(NoteWords))
        {
            return Part.Notes;
        }

        return Has(OtherWords) ? Part.Other : null;
    }

    /// <summary>Removes Markdown inline markup: images, links (keeping their text), bold, code marks.</summary>
    private static string Inline(string text) =>
        Emphasis().Replace(MarkdownLink().Replace(MarkdownImage().Replace(text, string.Empty), "${t}"), string.Empty).Trim();

    /// <summary>Step lines keep their numbering and indentation for <see cref="StepTextRules.Split"/>, minus images and links.</summary>
    private static string Steps(string line) => MarkdownLink().Replace(MarkdownImage().Replace(line, string.Empty), "${t}");
}
