using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Text;

namespace RecipeWarden.Importers.Ingredients;

/// <summary>
/// FR-REC-03, BR-04, section 9.3 step 3: splits an ingredient line such as "1 ½ cups all-purpose flour, sifted"
/// into quantity (with ranges and unicode fractions), unit (via <see cref="UnitCatalog"/>), ingredient name,
/// preparation, state, optional flag and notes, keeping the original text. Pure and deterministic; never throws on
/// any input. Order of work: tidy the text, take out "(optional)" and parenthetical notes, read the amount, read the
/// unit, read a leading state or preparation word, then split the rest on commas into name, preparation and notes.
/// </summary>
public static partial class IngredientLineParser
{
    /// <summary>Lines longer than this are cut before parsing (the raw text is still kept whole).</summary>
    public const int MaxParseLength = 300;

    private const decimal MaxQuantity = 100_000m;

    private static readonly Dictionary<char, string> FractionGlyphs = new()
    {
        ['½'] = "1/2", ['⅓'] = "1/3", ['⅔'] = "2/3", ['¼'] = "1/4", ['¾'] = "3/4", ['⅕'] = "1/5", ['⅖'] = "2/5",
        ['⅗'] = "3/5", ['⅘'] = "4/5", ['⅙'] = "1/6", ['⅚'] = "5/6", ['⅛'] = "1/8", ['⅜'] = "3/8", ['⅝'] = "5/8",
        ['⅞'] = "7/8",
    };

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
    };

    private static readonly HashSet<string> StateWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cooked", "uncooked", "raw", "frozen",
    };

    private static readonly HashSet<string> PrepWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "chopped", "diced", "minced", "sliced", "grated", "shredded", "melted", "softened", "crushed", "cubed",
        "peeled", "halved", "quartered", "beaten", "sifted", "packed", "toasted", "mashed", "crumbled", "julienned",
        "rinsed", "drained", "trimmed", "zested", "juiced",
    };

    // Words that are preparation only after an adverb ("freshly ground pepper", but "ground cumin" is a food).
    private static readonly HashSet<string> PrepAfterAdverb = new(StringComparer.OrdinalIgnoreCase)
    {
        "ground", "squeezed", "cracked",
    };

    private static readonly HashSet<string> Adverbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "finely", "coarsely", "thinly", "roughly", "freshly", "lightly", "firmly", "loosely", "well", "very", "stiffly",
    };

    [GeneratedRegex(@"^(?<a>\d+\s+\d+/\d+|\d+-\d+/\d+|\d+/\d+|\d+(?:\.\d+)?|\.\d+)(?:\s*(?:-|to|or)\s*(?<b>\d+\s+\d+/\d+|\d+-\d+/\d+|\d+/\d+|\d+(?:\.\d+)?|\.\d+))?(?=\s|$|[A-Za-z])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex QuantityPattern();

    [GeneratedRegex(@"^\d+(?:\.\d+)?-(?:ounce|oz|inch|pound|lb|quart|gram|g)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex SizeAdjective();

    [GeneratedRegex(@"\(\s*optional\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex OptionalParen();

    [GeneratedRegex(@"^optional\s*:\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex OptionalPrefix();

    [GeneratedRegex(@"^(?:or (?:more )?to taste|to taste|divided|as needed|or as needed|if needed|plus more\b.*|for (?:serving|garnish|garnishing|dusting|frying|greasing|brushing|drizzling|topping|decorating|sprinkling|the pan)\b.*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex NotePhrase();

    [GeneratedRegex(@"\s+(?<note>(?:or (?:more )?)?to taste|as needed|for (?:serving|garnish|garnishing|dusting|frying|greasing|brushing|drizzling|topping|decorating|sprinkling))$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex TrailingNote();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Spaces();

    /// <summary>Parses one line. Blank input gives an empty result with confidence 0.</summary>
    public static ParsedIngredient Parse(string? line)
    {
        var raw = SafeText.ReplaceLoneSurrogates(line ?? string.Empty).Trim();
        try
        {
            return ParseCore(raw);
        }
        catch (RegexMatchTimeoutException)
        {
            return new ParsedIngredient { RawText = raw, Name = raw, Confidence = 0m };
        }
    }

    /// <summary>
    /// Parses pasted text, one ingredient per line. Blank lines are skipped. A line ending in ":" (or a short
    /// "For the …" line) with no amount starts a group (FR-REC-01 ingredient groups).
    /// </summary>
    public static IReadOnlyList<IngredientLine> ParseList(string? text)
    {
        var result = new List<IngredientLine>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var heading = AsHeading(line);
            result.Add(heading is not null ? new IngredientLine(heading, null) : new IngredientLine(null, Parse(line)));
        }

        return result;
    }

    private static string? AsHeading(string line)
    {
        var tidy = Tidy(line);
        if (tidy.Length == 0 || QuantityPattern().IsMatch(tidy) || OptionalPrefix().IsMatch(tidy))
        {
            return null;
        }

        var words = tidy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tidy.EndsWith(':') && words.Length <= 8)
        {
            var name = tidy.TrimEnd(':').Trim();
            return name.Length == 0 ? null : name;
        }

        var isForThe = words.Length is >= 3 and <= 5
            && words[0].Equals("for", StringComparison.OrdinalIgnoreCase)
            && words[1].Equals("the", StringComparison.OrdinalIgnoreCase);
        return isForThe ? tidy : null;
    }

    private static ParsedIngredient ParseCore(string raw)
    {
        if (raw.Length == 0)
        {
            return new ParsedIngredient { RawText = raw, Confidence = 0m };
        }

        var notes = new List<string>();
        var text = Tidy(raw.Length > MaxParseLength ? raw[..MaxParseLength] : raw);
        var penalty = 0m;

        // Optional markers.
        var isOptional = false;
        if (OptionalPrefix().IsMatch(text))
        {
            isOptional = true;
            text = OptionalPrefix().Replace(text, string.Empty);
        }

        if (OptionalParen().IsMatch(text))
        {
            isOptional = true;
            text = OptionalParen().Replace(text, " ");
        }

        // Parenthetical notes: "(15 oz)", "(about 1 cup)".
        text = ExtractParentheses(text, notes, ref penalty);
        text = Spaces().Replace(text, " ").Trim();

        // Amount.
        decimal? quantity = null, quantityMax = null;
        string? quantityText = null;
        var hadAmount = false;
        var match = QuantityPattern().Match(text);
        if (match.Success)
        {
            var a = ParseNumber(match.Groups["a"].Value);
            var b = match.Groups["b"].Success ? ParseNumber(match.Groups["b"].Value) : null;
            if (a is not null && (!match.Groups["b"].Success || b is not null))
            {
                quantity = a;
                quantityMax = b;
                hadAmount = true;
                if (match.Value.Contains('/', StringComparison.Ordinal))
                {
                    quantityText = QuantityTextOf(match.Groups["a"].Value) +
                        (match.Groups["b"].Success ? " - " + QuantityTextOf(match.Groups["b"].Value) : string.Empty);
                }

                text = text[match.Length..].TrimStart();
            }
            else
            {
                penalty += 0.5m; // e.g. "1/0"
            }
        }

        var words = new List<string>(text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (!hadAmount && words.Count >= 2
            && words[0].Equals("a", StringComparison.OrdinalIgnoreCase) && words[1].Equals("few", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("a few");
            words.RemoveRange(0, 2);
            hadAmount = true;
        }
        else if (!hadAmount && words.Count >= 2 && NumberWords.TryGetValue(words[0], out var wordNumber))
        {
            quantity = wordNumber;
            words.RemoveAt(0);
            hadAmount = true;
        }

        // Size adjective before the unit: "1 8-ounce package".
        if (hadAmount && words.Count > 0 && SizeAdjective().IsMatch(words[0]))
        {
            notes.Insert(0, words[0]);
            words.RemoveAt(0);
        }

        // Unit: right after the amount, or a vague unit followed by "of" ("pinch of cayenne").
        string? unitCode = null;
        var unitAllowed = hadAmount
            || (words.Count >= 2 && UnitCatalog.FindByAlias(words[0]) is { IsVague: true } && words[1].Equals("of", StringComparison.OrdinalIgnoreCase));
        if (unitAllowed && words.Count >= 2)
        {
            for (var n = Math.Min(UnitCatalog.MaxAliasWords, words.Count - 1); n >= 1; n--)
            {
                var candidate = string.Join(' ', words.Take(n)).TrimEnd(',');
                var unit = UnitCatalog.FindByAlias(candidate);
                if (unit is not null)
                {
                    unitCode = unit.Code;
                    words.RemoveRange(0, n);
                    break;
                }
            }

            if (unitCode is not null && words.Count > 1 && words[0].Equals("of", StringComparison.OrdinalIgnoreCase))
            {
                words.RemoveAt(0);
            }
        }

        // "1 cup plus 2 tablespoons flour", "1 cup + 2 tbsp water".
        if (unitCode is not null && words.Count >= 3 && (words[0].Equals("plus", StringComparison.OrdinalIgnoreCase) || words[0] == "+"))
        {
            var extra = QuantityPattern().Match(words[1]);
            if (extra.Success && extra.Length == words[1].Length && UnitCatalog.FindByAlias(words[2]) is not null)
            {
                notes.Add($"plus {words[1]} {words[2]}");
                words.RemoveRange(0, 3);
            }
        }

        // Leading state and preparation: "cooked shredded chicken", "freshly ground black pepper".
        string? state = null;
        if (words.Count > 1 && StateWords.Contains(words[0]))
        {
            state = Lower(words[0]);
            words.RemoveAt(0);
        }

        string? leadingPrep = null;
        if (words.Count > 1 && PrepWords.Contains(words[0].TrimEnd(',')))
        {
            leadingPrep = Lower(words[0].TrimEnd(','));
            words.RemoveAt(0);
        }
        else if (words.Count > 2 && Adverbs.Contains(words[0])
                 && (PrepWords.Contains(words[1].TrimEnd(',')) || PrepAfterAdverb.Contains(words[1].TrimEnd(','))))
        {
            leadingPrep = Lower(words[0]) + " " + Lower(words[1].TrimEnd(','));
            words.RemoveRange(0, 2);
        }

        // Name, then comma-separated preparation and notes.
        var rest = string.Join(' ', words);
        var segments = rest.Split(',').Select(s => s.Trim()).ToList();
        var name = segments[0];
        var preps = new List<string>();
        if (leadingPrep is not null)
        {
            preps.Add(leadingPrep);
        }

        var trailing = TrailingNote().Match(name);
        if (trailing.Success)
        {
            notes.Add(Lower(trailing.Groups["note"].Value));
            name = name[..trailing.Index];
        }

        foreach (var segment in segments.Skip(1))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            if (segment.Equals("optional", StringComparison.OrdinalIgnoreCase))
            {
                isOptional = true;
            }
            else if (NotePhrase().IsMatch(segment))
            {
                notes.Add(segment);
            }
            else
            {
                preps.Add(segment);
            }
        }

        name = name.Trim().TrimEnd('.').Trim();
        var preparation = preps.Count > 0 ? string.Join(", ", preps).TrimEnd('.') : null;

        return new ParsedIngredient
        {
            RawText = raw,
            Quantity = quantity,
            QuantityMax = quantityMax,
            QuantityText = quantityText,
            UnitCode = unitCode,
            Name = name,
            Preparation = preparation,
            State = state,
            IsOptional = isOptional,
            Note = notes.Count > 0 ? string.Join("; ", notes) : null,
            Confidence = Score(name, quantity, hadAmount, notes.Count > 0, penalty),
        };
    }

    private static decimal Score(string name, decimal? quantity, bool hadAmount, bool hasNote, decimal penalty)
    {
        if (name.Length == 0)
        {
            return 0.1m;
        }

        var score = 1m - penalty;
        if (UnitCatalog.FindByAlias(name) is not null)
        {
            score -= 0.5m; // "1 cup": the unit was read as the ingredient
        }

        if (!hadAmount && !hasNote)
        {
            score -= 0.1m; // "Salt": fine, but slightly less certain
        }

        if (name.Any(char.IsAsciiDigit))
        {
            score -= 0.25m; // leftover numbers usually mean a missed amount or unit
        }

        var wordCount = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 6 || name.Length > 60)
        {
            score -= 0.25m;
        }

        if (name.IndexOfAny(['(', ')', '[', ']', '{', '}', '<', '>', ';', '=']) >= 0)
        {
            score -= 0.3m;
        }

        if (quantity is not null && quantity > 1000m)
        {
            score -= 0.2m;
        }

        return Math.Clamp(score, 0m, 1m);
    }

    /// <summary>Unicode fractions to ASCII, dashes to hyphens, bullets removed, spaces collapsed.</summary>
    private static string Tidy(string line)
    {
        var text = SafeText.Normalize(line, NormalizationForm.FormC);
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (FractionGlyphs.TryGetValue(c, out var ascii))
            {
                if (sb.Length > 0 && char.IsAsciiDigit(sb[^1]))
                {
                    sb.Append(' ');
                }

                sb.Append(ascii);
            }
            else if (c is '–' or '—' or '−')
            {
                sb.Append('-');
            }
            else if (c == '⁄')
            {
                sb.Append('/');
            }
            else if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }

        var tidy = Spaces().Replace(sb.ToString(), " ").Trim();
        if (tidy.Length >= 2 && tidy[0] is '-' or '*' or '•' && tidy[1] == ' ')
        {
            tidy = tidy[2..].TrimStart();
        }

        return tidy;
    }

    private static string ExtractParentheses(string text, List<string> notes, ref decimal penalty)
    {
        var sb = new StringBuilder(text.Length);
        var depth = 0;
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (c == '(')
            {
                if (depth > 0)
                {
                    current.Append(c);
                }

                depth++;
                continue;
            }

            if (c == ')' && depth > 0)
            {
                depth--;
                if (depth == 0)
                {
                    var note = Spaces().Replace(current.ToString(), " ").Trim();
                    if (note.Length > 0)
                    {
                        notes.Add(note);
                    }

                    current.Clear();
                    sb.Append(' ');
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            (depth > 0 ? current : sb).Append(c);
        }

        if (depth > 0)
        {
            penalty += 0.3m; // unbalanced: keep the text rather than lose it
            sb.Append(" (").Append(current);
        }

        return sb.ToString();
    }

    private static decimal? ParseNumber(string text)
    {
        text = text.Trim();
        decimal whole = 0;
        string fraction;

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (space > 0 || dash > 0)
        {
            var split = space > 0 ? space : dash;
            if (!decimal.TryParse(text[..split], NumberStyles.None, CultureInfo.InvariantCulture, out whole))
            {
                return null;
            }

            fraction = text[(split + 1)..].Trim();
        }
        else if (text.Contains('/', StringComparison.Ordinal))
        {
            fraction = text;
        }
        else
        {
            return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d <= MaxQuantity
                ? d
                : null;
        }

        var parts = fraction.Split('/');
        if (parts.Length != 2
            || !decimal.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var num)
            || !decimal.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var den)
            || den == 0 || den > 1000)
        {
            return null;
        }

        var value = whole + (num / den);
        return value <= MaxQuantity ? decimal.Round(value, 6) : null;
    }

    private static string QuantityTextOf(string text) => text.Trim().Replace('-', ' ');

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "Preparation, state and note words are shown in lower case as they read in a recipe.")]
    private static string Lower(string s) => s.ToLowerInvariant();
}
