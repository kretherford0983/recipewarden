namespace RecipeWarden.Core.Recipes;

/// <summary>
/// ADR 0023: steps are replaced on every save, so each old step's photo moves to the new step with the same text
/// (after trimming), else to the new step in the same position, else it is detached. A new step takes at most one
/// photo; text matches are settled before position matches.
/// </summary>
public static class StepPhotoRules
{
    /// <summary>For each old step, the index of the new step its photo moves to, or null.</summary>
    public static IReadOnlyList<int?> Follow(IReadOnlyList<string> oldTexts, IReadOnlyList<string> newTexts)
    {
        ArgumentNullException.ThrowIfNull(oldTexts);
        ArgumentNullException.ThrowIfNull(newTexts);
        var result = new int?[oldTexts.Count];
        var taken = new bool[newTexts.Count];
        for (var i = 0; i < oldTexts.Count; i++)
        {
            var text = oldTexts[i].Trim();
            for (var j = 0; j < newTexts.Count; j++)
            {
                if (!taken[j] && string.Equals(newTexts[j].Trim(), text, StringComparison.Ordinal))
                {
                    result[i] = j;
                    taken[j] = true;
                    break;
                }
            }
        }

        for (var i = 0; i < oldTexts.Count; i++)
        {
            if (result[i] is null && i < newTexts.Count && !taken[i])
            {
                result[i] = i;
                taken[i] = true;
            }
        }

        return result;
    }
}
