using System.Globalization;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.AiExchange;

/// <summary>One recipe-md block found in a reply: its text, what it reads as, and what is wrong with it.</summary>
public sealed record AiReplyBlock(int Index, string Markdown, ImportedRecipe? Recipe, IReadOnlyList<string> Problems)
{
    public bool IsValid => Recipe is not null && Problems.Count == 0;
}

/// <summary>
/// FR-EXP-05, AC-27, BR-93, ADR 0033: finds recipe-md blocks in an AI reply (chat text around them, code fences or
/// not), reads each with the normal recipe-md reader (plain text, BR-13) and checks it against the schema rules.
/// Nothing here saves anything.
/// </summary>
public static class AiReplyReader
{
    public const int MaxReplyLength = 512 * 1024;
    public const int MaxBlocks = 50;
    public const string NoBlock =
        "No recipe was found in the reply. Ask the assistant to reply with the full recipe in recipe-md v1, starting with "
        + "a --- line and \"format: recipe-md/1\".";

    public static IReadOnlyList<AiReplyBlock> Read(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return [];
        }

        var text = reply.Length > MaxReplyLength ? reply[..MaxReplyLength] : reply;
        var blocks = new List<AiReplyBlock>();
        foreach (var markdown in Blocks(text).Take(MaxBlocks))
        {
            var problems = RecipeMarkdownValidator.Validate(markdown).ToList();
            var read = RecipeMarkdown.Read(markdown);
            var recipe = read.Count > 0 ? read[0] : null;
            if (recipe is not null)
            {
                if (string.IsNullOrWhiteSpace(recipe.Title))
                {
                    problems.Add("The recipe has no title (\"title:\" or \"# Title\").");
                }

                if (string.IsNullOrWhiteSpace(recipe.Ingredients))
                {
                    problems.Add("The \"## Ingredients\" section is missing or empty.");
                }

                if (string.IsNullOrWhiteSpace(recipe.Steps))
                {
                    problems.Add("The \"## Steps\" section is missing or empty.");
                }
            }

            blocks.Add(new AiReplyBlock(blocks.Count, markdown, recipe, problems.Distinct(StringComparer.Ordinal).ToList()));
        }

        return blocks;
    }

    /// <summary>
    /// The recipe-md documents in the text. A block in a code fence ends at the closing fence; a bare block ends at
    /// the next block (chat text after the last bare block stays with it, as the reader would read it).
    /// </summary>
    public static IReadOnlyList<string> Blocks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var result = new List<string>();
        var i = 0;
        while (i < lines.Length)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith("~~~", StringComparison.Ordinal))
            {
                var fence = t[..3];
                var close = Array.FindIndex(lines, i + 1, l => l.Trim().StartsWith(fence, StringComparison.Ordinal));
                var end = close < 0 ? lines.Length : close;
                var inner = string.Join('\n', lines[(i + 1)..end]);
                foreach (var b in Bare(inner))
                {
                    result.Add(b);
                }

                i = end + 1;
                continue;
            }

            // Collect bare lines up to the next fence.
            var start = i;
            while (i < lines.Length && !(lines[i].Trim().StartsWith("```", StringComparison.Ordinal) || lines[i].Trim().StartsWith("~~~", StringComparison.Ordinal)))
            {
                i++;
            }

            result.AddRange(Bare(string.Join('\n', lines[start..i])));
        }

        return result;
    }

    private static IEnumerable<string> Bare(string text)
    {
        var lines = text.Split('\n');
        var starts = Enumerable.Range(0, lines.Length).Where(i => lines[i].Trim() == "---" && RecipeMarkdown.IsRecipeMarkdown(string.Join('\n', lines[i..Math.Min(lines.Length, i + 40)]))).ToList();
        for (var k = 0; k < starts.Count; k++)
        {
            var end = k + 1 < starts.Count ? starts[k + 1] : lines.Length;
            yield return string.Join('\n', lines[starts[k]..end]).TrimEnd() + "\n";
        }
    }
}

/// <summary>
/// FR-EXP-06, AC-25, ADR 0033: the rules of <c>/docs/recipe-md.schema.json</c> for a recipe-md front matter. Unknown
/// keys are allowed (allergens and nutrition are written by later milestones).
/// </summary>
public static class RecipeMarkdownValidator
{
    public static readonly IReadOnlyList<string> SourceTypes = ["web", "file", "manual", "personal"];

    public static IReadOnlyList<string> Validate(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var open = Array.FindIndex(lines, l => l.Trim().Length > 0);
        if (open < 0 || lines[open].Trim() != "---")
        {
            return ["The recipe must start with front matter between --- lines."];
        }

        var close = Array.FindIndex(lines, open + 1, l => l.Trim() == "---");
        if (close < 0)
        {
            return ["The front matter has no closing --- line."];
        }

        var meta = FrontMatter.Parse(string.Join('\n', lines[(open + 1)..close]));
        var problems = new List<string>();
        if (!string.Equals(meta.Text("format")?.Trim(), RecipeMarkdown.Format, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("\"format\" must be recipe-md/1.");
        }

        if (meta.Text("title") is { } title && (title.Trim().Length == 0 || title.Length > PublishRules.MaxTitleLength))
        {
            problems.Add("\"title\" must be 1 to 200 characters.");
        }

        if (meta.Text("slug") is { } slug && !SlugRules.IsValid(slug.Trim()))
        {
            problems.Add("\"slug\" must be lower-case letters, digits and hyphens.");
        }

        if (meta.Text("servings") is { } servings && ServingsRules.ParseText(servings).Error is not null)
        {
            problems.Add("\"servings\" must be a number or a range such as 6-8.");
        }

        foreach (var key in new[] { "prep_minutes", "cook_minutes", "total_minutes" })
        {
            if (meta.Text(key) is { } minutes && !IsMinutes(minutes))
            {
                problems.Add($"\"{key}\" must be whole minutes or a range such as 20-25.");
            }
        }

        if (meta.Text("precision") is { } precision && precision.Trim() is not ("true" or "false"))
        {
            problems.Add("\"precision\" must be true or false.");
        }

        if (meta.Text("finished_density") is { } density && !IsNumber(density, out _))
        {
            problems.Add("\"finished_density\" must be a number (g/ml).");
        }

        if (meta.Text("scale") is { } scale && !(IsNumber(scale, out var factor) && ScaleRules.InRange(factor)))
        {
            problems.Add("\"scale\" must be a number from 0.1 to 100.");
        }

        if (meta.Text("image") is { } image && !IsRelativePath(image))
        {
            problems.Add("\"image\" must be a relative path inside the export, e.g. images/finish.webp.");
        }

        if (meta.ContainsKey("source"))
        {
            if (meta.Child("source") is not { } source)
            {
                problems.Add("\"source\" must be a block with type, name, author and url.");
            }
            else
            {
                if (source.Text("type") is { } type && !SourceTypes.Contains(type.Trim().ToLowerInvariant()))
                {
                    problems.Add("\"source.type\" must be web, file, manual or personal.");
                }

                if (source.Text("url") is { } url && !SourceRules.IsHttpUrl(url.Trim()))
                {
                    problems.Add("\"source.url\" must be an http or https address.");
                }
            }
        }

        return problems;
    }

    private static bool IsMinutes(string text)
    {
        var parts = text.Trim().Split('-', StringSplitOptions.TrimEntries);
        return parts.Length is 1 or 2 && parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 && n <= 100_000);
    }

    private static bool IsNumber(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    private static bool IsRelativePath(string path)
    {
        var p = path.Trim();
        return p.Length is > 0 and <= 200 && !p.StartsWith('/') && !p.Contains("..", StringComparison.Ordinal)
               && !p.Contains(':', StringComparison.Ordinal) && !p.Contains('\\', StringComparison.Ordinal);
    }
}
