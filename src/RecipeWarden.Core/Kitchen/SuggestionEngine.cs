using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Kitchen;

/// <summary>One ingredient line a suggestion would change.</summary>
public sealed record SuggestionLine(int Index, string Before, string After);

/// <summary>FR-TOOL-04: a keyed offer (BR-32) and the lines it changes.</summary>
public sealed record Suggestion(string Key, string Message, IReadOnlyList<SuggestionLine> Lines);

/// <summary>
/// FR-TOOL-04/05, BR-31, BR-32, ADR 0017: suggestions are worked out on every view, without writing. Spoons and
/// cups apply at once (FR-TOOL-03, AC-40). A scale is an offer (AC-41, "show flour by weight?"): weights come
/// first only for recipes whose accepted inventory (<c>InventoryVersionSeen</c>) already had a scale.
/// </summary>
public static class SuggestionEngine
{
    /// <summary>True when BR-22's automatic weight applies to this recipe: a scale now, and one when it was last accepted.</summary>
    public static bool WeightAccepted(KitchenTools seen, KitchenTools current)
    {
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(current);
        return current.Scale is not null && seen.Scale is not null;
    }

    public static string WeightKey(KitchenToolInfo scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return "weight:scale-" + scale.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The suggestions for a recipe. <paramref name="settled"/> holds keys an editor dismissed or applied; they do
    /// not return (a different scale has a different key). Only suggestions that change a shown line are returned.
    /// </summary>
    public static IReadOnlyList<Suggestion> Find(IReadOnlyList<ScaleInput> lines, ScaleContext context, KitchenTools seen,
        KitchenTools current, IReadOnlySet<string> settled)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settled);
        if (current?.Scale is not { } scale || seen?.Scale is not null || settled.Contains(WeightKey(scale)))
        {
            return [];
        }

        var before = context with { Tools = current, PreferWeight = false };
        var after = context with { Tools = current, PreferWeight = true };
        var changed = new List<SuggestionLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var a = IngredientScaler.Scale(lines[i], before);
            var b = IngredientScaler.Scale(lines[i], after);
            if (b.ByWeight && a.Text != b.Text)
            {
                changed.Add(new SuggestionLine(i, a.Text, b.Text));
            }
        }

        if (changed.Count == 0)
        {
            return [];
        }

        var message = "You now have a scale (" + scale.Name + "). Show " + changed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      + (changed.Count == 1 ? " ingredient" : " ingredients") + " by weight?";
        return [new Suggestion(WeightKey(scale), message, changed)];
    }
}
