using System.Globalization;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.AiExchange;

/// <summary>A line in a line-by-line comparison: kept, removed from the original, or added by the reply.</summary>
public enum LineChange
{
    Same,
    Removed,
    Added,
}

public sealed record DiffLine(LineChange Change, string Text);

/// <summary>
/// BR-92, BR-93, BR-95, ADR 0033: what an AI reply becomes when saved. The source always comes from the original
/// recipe; the notes say the recipe was adapted with AI assistance and keep the reply's "## Changes".
/// </summary>
public static class AiVariantRules
{
    public const string VariantSuffix = " (AI variant)";
    public const int MaxDiffLines = 400;

    public static string AdaptedNote(DateOnly date) =>
        "Adapted with AI assistance on " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".";

    /// <summary>The edit to save: the reply's content with the original's source (BR-92) and the adapted note.</summary>
    public static RecipeEdit ToEdit(ImportedRecipe reply, RecipeEdit original, DateOnly date, bool asVariant, bool publish)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(original);
        var servings = ServingsRules.ParseText(reply.Servings);
        var prep = TimeText.Parse(reply.PrepTime);
        var cook = TimeText.Parse(reply.CookTime);
        var total = TimeText.Parse(reply.TotalTime);
        var title = string.IsNullOrWhiteSpace(reply.Title) ? original.Title : reply.Title;
        if (asVariant && string.Equals(title?.Trim(), original.Title?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            title = (title ?? string.Empty).Trim() + VariantSuffix;
        }

        decimal? density = decimal.TryParse(reply.FinishedDensity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
            ? d : original.FinishedDensityGPerMl;
        var notes = string.Join("\n\n", new[] { reply.Notes, AdaptedNote(date) }.Where(n => !string.IsNullOrWhiteSpace(n)));
        return original with
        {
            Title = title,
            Description = reply.Description ?? original.Description,
            Servings = servings.Error is null && servings.Min is not null ? servings.Min : original.Servings,
            ServingsMax = servings.Error is null && servings.Min is not null ? servings.Max : original.ServingsMax,
            PrepMinutes = prep.Error is null && prep.Min is not null ? prep.Min : original.PrepMinutes,
            PrepMinutesMax = prep.Error is null && prep.Min is not null ? prep.Max : original.PrepMinutesMax,
            CookMinutes = cook.Error is null && cook.Min is not null ? cook.Min : original.CookMinutes,
            CookMinutesMax = cook.Error is null && cook.Min is not null ? cook.Max : original.CookMinutesMax,
            TotalMinutes = total.Error is null && total.Min is not null ? total.Min : original.TotalMinutes,
            TotalMinutesMax = total.Error is null && total.Min is not null ? total.Max : original.TotalMinutesMax,
            IsPrecision = reply.IsPrecision || original.IsPrecision,
            IngredientsText = reply.Ingredients,
            StepsText = reply.Steps,
            Notes = notes.Length > RecipeEdit.MaxNotesLength ? notes[..RecipeEdit.MaxNotesLength] : notes,
            Tags = reply.Tags.Count > 0 ? reply.Tags : original.Tags,
            YieldText = reply.Yield ?? original.YieldText,
            PanText = reply.Pan ?? original.PanText,
            FinishedDensityGPerMl = density,
            WantsPublish = publish,
            // BR-92: the credit is the original's, whatever the reply says.
            SourceType = original.SourceType,
            SourceName = original.SourceName,
            SourceUrl = original.SourceUrl,
            SourceAuthor = original.SourceAuthor,
        };
    }

    /// <summary>A line-by-line comparison (longest common subsequence), capped for very long inputs.</summary>
    public static IReadOnlyList<DiffLine> Lines(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var a = before.Take(MaxDiffLines).ToArray();
        var b = after.Take(MaxDiffLines).ToArray();
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(a[i].Trim(), b[j].Trim(), StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (string.Equals(a[x].Trim(), b[y].Trim(), StringComparison.Ordinal))
            {
                result.Add(new DiffLine(LineChange.Same, b[y]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new DiffLine(LineChange.Removed, a[x++]));
            }
            else
            {
                result.Add(new DiffLine(LineChange.Added, b[y++]));
            }
        }

        result.AddRange(a.Skip(x).Select(l => new DiffLine(LineChange.Removed, l)));
        result.AddRange(b.Skip(y).Select(l => new DiffLine(LineChange.Added, l)));
        return result;
    }

    /// <summary>Non-empty lines of an editor text field.</summary>
    public static IReadOnlyList<string> SplitLines(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0).ToList();
}
