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
/// BR-133: versions of one recipe under "## " headings are one recipe each; BR-134: facts under Yield and time headings.
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

        var recipes = chunks.SelectMany(SplitVersions).SelectMany(SplitBatchSection)
            .Select(c => ExtractOne(c, fallbackTitle)).Where(r => r.HasContent).ToList();
        return BatchRules.Fold(recipes, Parse);
    }

    private static readonly string[] VersionWords =
        ["version", "versions", "style", "based", "variation", "variant", "method", "option", "alternative", "classic", "traditional"];

    /// <summary>Headings whose lines are facts even without a keyword (BR-134): "### Yield" then "- About 2 cups".</summary>
    private static string? FactHeading(string? heading) => heading?.TrimEnd(':').Trim().ToLowerInvariant() switch
    {
        "yield" or "yields" or "makes" or "yield & servings" or "yield and servings" => "yield",
        "servings" or "serves" => "servings",
        "prep time" or "prep" or "preparation time" => "prep",
        "cook time" or "cooking time" or "cook" or "bake time" or "baking time" => "cook",
        "total time" or "total" => "total",
        "time" or "times" or "timing" => "time",
        _ => null,
    };

    private static IEnumerable<(int Index, int Level, string Text)> Headings(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var m = MarkdownHeading().Match(lines[i].Trim());
            if (m.Success)
            {
                yield return (i, m.Groups["h"].Value.Length, Inline(m.Groups["t"].Value));
            }
        }
    }

    /// <summary>
    /// BR-133 (owner decision Q1, 2026-11-25): one "# Title" holding versions of a recipe under "## " headings, each with
    /// its own Ingredients and Steps and either its own facts (yield, servings, times) or a version word in its heading
    /// ("Oil Based Sauce"), is split into one recipe per version titled "Title (Version)". The lines before the first
    /// "## " heading are every version's description; the shared sections before the first version go into every
    /// version's notes (Q3). Parts of one dish ("For the frosting") have no facts of their own and are not split.
    /// </summary>
    private static IEnumerable<List<string>> SplitVersions(List<string> lines)
    {
        var headings = Headings(lines).ToList();
        var seconds = headings.Where(h => h.Level == 2 && SectionOf(h.Text, true) is null).ToList();
        var versions = new List<(int Index, string Text, int End)>();
        for (var k = 0; k < seconds.Count; k++)
        {
            var end = k + 1 < seconds.Count ? seconds[k + 1].Index : lines.Count;
            var inside = headings.Where(h => h.Index > seconds[k].Index && h.Index < end).ToList();
            var hasIngredients = inside.Any(h => SectionOf(h.Text, true) == Part.Ingredients);
            var hasSteps = inside.Any(h => SectionOf(h.Text, true) == Part.Steps);
            var firstSection = inside.Where(h => SectionOf(h.Text, true) is not null).Select(h => h.Index).DefaultIfEmpty(end).First();
            var hasFacts = inside.Any(h => h.Index < firstSection && FactHeading(h.Text) is not null)
                || lines.Skip(seconds[k].Index + 1).Take(firstSection - seconds[k].Index - 1).Any(IsFact);
            var versionWord = seconds[k].Text.ToLowerInvariant().Split([' ', '-', '–', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Any(VersionWords.Contains);
            if (hasIngredients && hasSteps && (hasFacts || versionWord))
            {
                versions.Add((seconds[k].Index, seconds[k].Text, end));
            }
        }

        if (versions.Count < 2)
        {
            yield return lines;
            yield break;
        }

        // A version's part runs to the next version (non-version "## " sections after it stay with it).
        var title = headings.FirstOrDefault(h => h.Level == 1 && h.Index < versions[0].Index);
        var firstSub = headings.Where(h => h.Level >= 2 && h.Index < versions[0].Index).Select(h => h.Index).DefaultIfEmpty(versions[0].Index).First();
        var start = title.Text is null ? 0 : title.Index + 1;
        var description = lines.Skip(start).Take(firstSub - start).ToList();
        var shared = lines.Skip(firstSub).Take(versions[0].Index - firstSub)
            .Select(l => MarkdownHeading().Match(l.Trim()) is { Success: true } m ? Inline(m.Groups["t"].Value) : l).ToList();
        for (var k = 0; k < versions.Count; k++)
        {
            var end = k + 1 < versions.Count ? versions[k + 1].Index : lines.Count;
            var part = new List<string> { "# " + VersionTitle(title.Text, versions[k].Text) };
            part.AddRange(description);
            part.AddRange(lines.Skip(versions[k].Index + 1).Take(end - versions[k].Index - 1));
            if (shared.Any(l => l.Trim().Length > 0))
            {
                part.Add(string.Empty);
                part.Add("## Notes");
                part.AddRange(shared);
            }

            yield return part;
        }
    }

    /// <summary>"Medium Sauce (Homemade)" + "Oil Based Sauce" → "Medium Sauce (Homemade, Oil Based Sauce)"; no title → the version.</summary>
    private static string VersionTitle(string? title, string version)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return version;
        }

        var t = title.Trim();
        return t.EndsWith(')') && t.LastIndexOf('(') > 0 && BatchRules.SplitTitle(t).Qualifier is null
            ? t[..^1].TrimEnd() + ", " + version + ")"
            : t + " (" + version + ")";
    }

    /// <summary>
    /// BR-133: a second Ingredients section whose heading names a batch ("### Ingredients (16oz Extra-Thick Batch)"),
    /// with the Steps after it, becomes "Title (16oz Extra-Thick Batch)" so BR-124 can fold it into the recipe's notes.
    /// </summary>
    private static IEnumerable<List<string>> SplitBatchSection(List<string> lines)
    {
        var headings = Headings(lines).ToList();
        var title = headings.FirstOrDefault(h => h.Level == 1);
        var ingredients = headings.Where(h => SectionOf(h.Text, true) == Part.Ingredients).ToList();
        var batch = ingredients.Skip(1).FirstOrDefault(h => BatchRules.SplitTitle(h.Text).Qualifier is not null);
        if (title.Text is null || batch.Text is null)
        {
            yield return lines;
            yield break;
        }

        // It runs through its Steps, to the next Ingredients, Notes or other section, or a heading at its level or above.
        var end = headings.Where(h => h.Index > batch.Index && SectionOf(h.Text, true) switch
            {
                Part.Steps => false,
                null => h.Level <= batch.Level,
                _ => true,
            })
            .Select(h => h.Index).DefaultIfEmpty(lines.Count).First();
        var qualifier = BatchRules.SplitTitle(batch.Text).Qualifier!;
        yield return lines.Take(batch.Index).Concat(lines.Skip(end)).ToList();
        yield return ["# " + title.Text + " (" + qualifier + ")", "## Ingredients", .. lines.Skip(batch.Index + 1).Take(end - batch.Index - 1)];
    }

    private static bool IsFact(string line)
    {
        var fact = Bullet().Replace(Inline(line.Trim()), string.Empty).Trim();
        return fact.Length > 0 && MetaSeparator().Split(fact).All(p => Meta().IsMatch(p.Trim()));
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
        string? factHeading = null; // BR-134: the Intro sub-heading the lines are under ("yield", "prep" …)
        var yieldNotes = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.StartsWith('>'))
            {
                trimmed = trimmed.TrimStart('>').Trim(); // BR-133: a block quote is layout; its text is kept
                line = trimmed;
            }
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
                factHeading = null;
                continue;
            }

            if (heading.Success)
            {
                factHeading = part == Part.Intro ? FactHeading(headingText) : null;
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

                // BR-134: under "### Yield", "- Approximately 1 1/2 cups of sauce" is the yield; other lines there
                // ("Enough for 3–4 lb of wings") go to the notes. Under a time heading a bare "- 10 minutes" is that time.
                if (factHeading is "yield" or "servings")
                {
                    if (ImportRules.ServingsAndYield([fact]) is { Servings: null, Yield: null })
                    {
                        yieldNotes.Add(fact);
                    }
                    else
                    {
                        meta.Add((factHeading, fact));
                    }

                    continue;
                }

                if (factHeading is "prep" or "cook" or "total" && ImportRules.Minutes(Loose(fact)) is not null)
                {
                    meta.Add((factHeading, fact));
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
            .Select(m => Loose(m.Value))
            .Select(v => ImportRules.Minutes(v) ?? ImportRules.Minutes(TrailingBracket().Replace(v, string.Empty))) // "25 min (plus chilling)"
            .FirstOrDefault(v => v is not null);

        var ingredientText = ImportText.Lines(ingredients.Where(l => l.Length > 0).Select(l => Bullet().Replace(l, string.Empty)));
        var stepList = StepHeadings(StepTextRules.Split(string.Join('\n', steps)));
        if (yieldNotes.Count > 0)
        {
            notes.InsertRange(0, [.. yieldNotes, string.Empty]);
        }

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
            remarks.Add(BatchRules.AlternativeGroupsRemark(alternatives)); // owner decision D3 (ADR 0056)
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

    /// <summary>"~10 minutes", "about 10 min", "approx. 10 min" → "10 minutes" for the time reader.</summary>
    private static string Loose(string value) => LooseLeadIn().Replace(value.Trim(), string.Empty);

    [GeneratedRegex(@"^(?:~|about\b|approx\.?|approximately\b|around\b|roughly\b)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex LooseLeadIn();

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
