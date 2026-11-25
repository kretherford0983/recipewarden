using System.Globalization;
using System.Text;

namespace RecipeWarden.Core.Measurement;

/// <summary>A food's names for matching: the name, the plural and the aliases (section 8.3 Food, FoodAlias).</summary>
public sealed record FoodNames(int Id, string Name, string? PluralName, IReadOnlyList<string> Aliases);

/// <summary>
/// ADR 0013: links an ingredient's name to a food by exact normalized name or alias, singular or plural. Size,
/// state and preparation words in front are dropped one at a time ("large eggs" → "egg", "shredded chicken" →
/// "chicken"). There is no fuzzy or substring matching, so "peanut butter" never links to butter: a wrong link
/// would give a wrong conversion, while no link only keeps the original unit (BR-23).
/// </summary>
public sealed class FoodMatcher
{
    /// <summary>Words that describe size, state or preparation rather than which food it is.</summary>
    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "large", "medium", "small", "extra-large", "jumbo", "fresh", "freshly", "frozen", "thawed", "cooked", "uncooked",
        "raw", "leftover", "shredded", "diced", "chopped", "minced", "sliced", "grated", "cubed", "crushed", "packed",
        "softened", "melted", "cold", "warm", "hot", "room-temperature", "finely", "coarsely", "roughly", "thinly",
        "lightly", "firmly", "beaten", "whisked", "boneless", "skinless", "trimmed", "peeled", "drained", "rinsed",
        "organic", "good-quality", "ripe", "sifted", "unsifted", "heaping", "level", "scant", "about", "plus", "of",
    };

    /// <summary>Words that look plural but are not (or have no singular in recipes).</summary>
    private static readonly HashSet<string> Invariant = new(StringComparer.Ordinal)
    {
        "molasses", "hummus", "asparagus", "couscous", "swiss", "grits", "greens", "brussels", "series", "species",
    };

    private readonly Dictionary<string, int> byKey = new(StringComparer.Ordinal);

    public FoodMatcher(IEnumerable<FoodNames> foods)
    {
        ArgumentNullException.ThrowIfNull(foods);
        foreach (var food in foods)
        {
            foreach (var name in new[] { food.Name, food.PluralName }.Concat(food.Aliases))
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // The first food to claim a key keeps it, so a later alias cannot steal a name.
                byKey.TryAdd(Normalize(name), food.Id);
                byKey.TryAdd(Singular(Normalize(name)), food.Id);
            }
        }
    }

    public int Count => byKey.Count;

    /// <summary>True for a size, state or preparation word ("large", "diced"), which does not say which food it is.</summary>
    public static bool IsModifier(string word) => Modifiers.Contains(word);

    /// <summary>The food for an ingredient name, or null when nothing matches exactly.</summary>
    public int? Match(string? ingredientName)
    {
        var key = Normalize(ingredientName);
        if (key.Length == 0)
        {
            return null;
        }

        var words = key.Split(' ');
        for (var start = 0; start < words.Length; start++)
        {
            var candidate = string.Join(' ', words, start, words.Length - start);
            if (byKey.TryGetValue(candidate, out var id) || byKey.TryGetValue(Singular(candidate), out id))
            {
                return id;
            }

            if (!Modifiers.Contains(words[start]))
            {
                break;
            }
        }

        return null;
    }

    /// <summary>English plural of the last word: onion → onions, tomato → tomatoes, berry → berries.</summary>
    public static string Plural(string phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        if (phrase.Length == 0 || Singular(phrase) != phrase || Invariant.Contains(phrase.Split(' ')[^1]))
        {
            return phrase; // already plural (or has no plural)
        }

        if (phrase.EndsWith('y') && phrase.Length > 1 && !"aeiou".Contains(phrase[^2], StringComparison.Ordinal))
        {
            return phrase[..^1] + "ies";
        }

        return phrase.EndsWith('s') || phrase.EndsWith('x') || phrase.EndsWith("ch", StringComparison.Ordinal)
               || phrase.EndsWith("sh", StringComparison.Ordinal) || phrase.EndsWith('o')
            ? phrase + "es"
            : phrase + "s";
    }

    /// <summary>Lower case, Unicode-normalized, accents kept, brackets and punctuation (except hyphens) removed.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var ch in text.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture))
        {
            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '%')
            {
                sb.Append(ch);
            }
            else if (ch is ' ' or ',' or '/' or '\t')
            {
                sb.Append(' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>English singular of the last word: berries → berry, tomatoes → tomato, peaches → peach, eggs → egg.</summary>
    public static string Singular(string phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        var space = phrase.LastIndexOf(' ');
        var head = space < 0 ? string.Empty : phrase[..(space + 1)];
        var word = space < 0 ? phrase : phrase[(space + 1)..];
        string single;
        if (Invariant.Contains(word))
        {
            single = word;
        }
        else if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
        {
            single = word[..^3] + "y";
        }
        else if (word.Length > 4 && (word.EndsWith("oes", StringComparison.Ordinal) || word.EndsWith("ches", StringComparison.Ordinal)
                 || word.EndsWith("shes", StringComparison.Ordinal) || word.EndsWith("sses", StringComparison.Ordinal)
                 || word.EndsWith("xes", StringComparison.Ordinal)))
        {
            single = word[..^2];
        }
        else if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)
                 && !word.EndsWith("us", StringComparison.Ordinal) && !word.EndsWith("is", StringComparison.Ordinal))
        {
            single = word[..^1];
        }
        else
        {
            single = word;
        }

        return head + single;
    }
}
