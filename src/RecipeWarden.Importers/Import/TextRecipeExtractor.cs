using System.Text.RegularExpressions;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Importers.Import;

/// <summary>
/// FR-IMP-04, FR-IMP-05, W4 step 2: finds a recipe in loosely structured text by its section headings — Markdown
/// headings or lines such as "Ingredients", "Directions:", "METHOD". Used for .md and .txt files, pasted text, Word
/// documents and PDFs (converted to the same Markdown-like lines), and as the heuristic HTML fallback (FR-IMP-01).
/// Several "# Title" headings in one file give several recipes; batch sizes of one recipe are folded into the base
/// recipe's notes (BR-124 to BR-126). BR-127: dividers, bulleted facts, in-file links, italics and a Nutrition section.
/// </summary>
public static partial class TextRecipeExtractor
{
    private enum Part
    {
        Intro,
        Ingredients,
        Steps,
        Notes,
        Nutrition,
        Other,
    }

    private static readonly string[] IngredientWords =
        ["ingredients", "ingredient list", "what you need", "what you'll need", "you will need", "you'll need", "shopping list"];

    private static readonly string[] StepWords =
        ["directions", "instructions", "method", "steps", "preparation", "procedure", "how to make it", "how to make", "to make"];

    private static readonly string[] NoteWords =
        ["notes", "note", "tips", "cook's notes", "cooks notes", "recipe notes", "variations", "storage"];

    private static readonly string[] NutritionWords =
        ["nutrition", "nutrition facts", "nutrition information", "nutrition info", "nutrition per serving"];

    private static readonly string[] OtherWords =
        ["equipment", "comments", "reviews", "video", "related recipes"];

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

    // BR-127: *italic* and _italic_ (not snake_case, not a "* " bullet).
    [GeneratedRegex(@"(?<![*\w])\*(?=\S)(?<t>[^*\n]{1,200}?)(?<=\S)\*(?![*\w])|(?<!\w)_(?=\S)(?<t>[^_\n]{1,200}?)(?<=\S)_(?!\w)",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Italic();

    // BR-127: a Markdown divider.
    [GeneratedRegex(@"^(?:(?:-\s*){3,}|(?:\*\s*){3,}|(?:_\s*){3,})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Divider();

    // BR-127: a link to a heading in the same file ("[Double Batch](#kansas-corn-bake-double-batch)"), alone on its line.
    [GeneratedRegex(@"^(?:[-*+•]\s+)?\[[^\]]*\]\(#[^)]*\)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex AnchorLink();

    [GeneratedRegex(@"\s*\([^()]*\)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex TrailingBracket();

    [GeneratedRegex(@"^(?<k>serves|servings|yield|yields|makes|prep(?:aration)?(?:\s+time)?|cook(?:ing)?(?:\s+time)?|bake(?:\s+time)?|baking(?:\s+time)?|total(?:\s+time)?|ready\s+in)\s*[:\-–]?\s*(?<v>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
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

        var recipes = chunks.Select(c => ExtractOne(c, fallbackTitle)).Where(r => r.HasContent).ToList();
        return BatchRules.Fold(recipes, Parse);
    }

    private static IReadOnlyList<ParsedIngredient> Parse(string? ingredients) =>
        IngredientLineParser.ParseList(ingredients).Where(l => !l.IsHeading).Select(l => l.Ingredient!).ToList();

    private static ImportedRecipe ExtractOne(List<string> lines, string? fallbackTitle)
    {
        string? title = null;
        var intro = new List<string>();
        var ingredients = new List<string>();
        var steps = new List<string>();
        var notes = new List<string>();
        var meta = new List<(string Key, string Value)>();
        var nutrition = new List<string>();
        string? pan = null;
        var part = Part.Intro;
        var sawSection = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && (Divider().IsMatch(trimmed) || AnchorLink().IsMatch(trimmed)))
            {
                continue; // BR-127: dividers and in-file links are layout, not content
            }

            if (part == Part.Nutrition)
            {
                if (!trimmed.StartsWith('#'))
                {
                    nutrition.Add(trimmed);
                    continue;
                }
            }

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
                // "Serves 8 · Prep time: 15 minutes" holds two facts; BR-127: "- **Prep:** 10 minutes" is one too.
                var fact = Bullet().Replace(Inline(trimmed), string.Empty).Trim();
                var pieces = MetaSeparator().Split(fact).Select(p => Meta().Match(p.Trim())).ToList();
                if (pieces.All(m => m.Success))
                {
                    meta.AddRange(pieces.Select(m => (MetaKey(m.Groups["k"].Value), m.Groups["v"].Value)));
                    continue;
                }

                // BR-127: "- One 8 × 12 inch baking dish" under Yield is the pan (ADR 0020).
                if (pan is null && Bullet().IsMatch(trimmed) && PanRules.FromSteps(fact) is { } found)
                {
                    pan = PanRules.Describe(found);
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
        string? Time(string prefix) => meta.Where(m => m.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(m => ImportRules.Minutes(m.Value) ?? ImportRules.Minutes(TrailingBracket().Replace(m.Value, string.Empty))) // "25 min (plus chilling)"
            .FirstOrDefault(v => v is not null);

        var ingredientText = ImportText.Lines(ingredients.Where(l => l.Length > 0).Select(l => Bullet().Replace(l, string.Empty)));
        var stepList = StepHeadings(StepTextRules.Split(string.Join('\n', steps)));
        var notesText = ImportText.Block(string.Join('\n', notes), RecipeEdit.MaxNotesLength);
        if (remark is not null)
        {
            notesText = notesText is null ? remark : remark + "\n\n" + notesText;
        }

        var remarks = new List<string>();
        if (remark is not null)
        {
            remarks.Add(remark);
        }

        if (BatchRules.AlternativeGroups(ingredientText) is { Count: > 0 } alternatives)
        {
            remarks.Add(BatchRules.AlternativeGroupsRemark(alternatives)); // owner decision D3 (ADR 0055)
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
            Pan = pan,
            Nutrition = NutritionText.FromLabelLines(nutrition),
            Remarks = remarks,
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

        if (Has(NutritionWords))
        {
            return Part.Nutrition;
        }

        return Has(OtherWords) ? Part.Other : null;
    }

    /// <summary>Removes Markdown inline markup: images, links (keeping their text), bold, italics, code marks.</summary>
    private static string Inline(string text) =>
        Italic().Replace(Emphasis().Replace(MarkdownLink().Replace(MarkdownImage().Replace(text, string.Empty), "${t}"), string.Empty), "${t}").Trim();

    /// <summary>
    /// Step lines keep their numbering, indentation and bold (step headings, ADR 0015) for <see cref="StepTextRules.Split"/>,
    /// minus images, links and single-mark italics.
    /// </summary>
    private static string Steps(string line) => Italic().Replace(MarkdownLink().Replace(MarkdownImage().Replace(line, string.Empty), "${t}"), "${t}");

    private static string MetaKey(string key)
    {
        var k = key.ToLowerInvariant();
        return k.StartsWith("bak", StringComparison.Ordinal) ? "cook" : k; // "Bake: 30–40 minutes" is the cook time
    }

    /// <summary>
    /// BR-127: when every "### Heading" in the steps holds exactly one step, the headings are step headings
    /// (ADR 0015), not sections.
    /// </summary>
    private static IReadOnlyList<StepText> StepHeadings(IReadOnlyList<StepText> steps)
    {
        var sections = steps.Where(s => s.SectionName is not null).GroupBy(s => s.SectionName).ToList();
        if (sections.Count < 2 || steps.Any(s => s.SectionName is null || s.Title is not null) || sections.Any(g => g.Count() != 1)
            || sections.Count != steps.Count)
        {
            return steps;
        }

        return steps.Select(s => new StepText(null, s.Text, s.SectionName)).ToList();
    }
}
