using System.Globalization;
using System.Text;

namespace RecipeWarden.Core.Import;

/// <summary>
/// The small YAML subset recipe-md v1 front matter uses (section 9.7): <c>key: value</c> scalars (plain, 'single' or
/// "double" quoted), inline lists <c>[a, "b c"]</c>, block lists (<c>- item</c>), one level of nested maps by
/// indentation (<c>source:</c>), inline maps <c>{ k: v }</c>, and <c># comments</c>. Anything else is ignored rather
/// than guessed. No anchors, tags or multi-line scalars, so no YAML package is needed and nothing can be
/// instantiated from the text.
/// </summary>
public static class FrontMatter
{
    public const int MaxLength = 64 * 1024;

    /// <summary>Values are a string, a list of strings, or a nested map of the same.</summary>
    public sealed class Map : Dictionary<string, object>
    {
        public Map()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public string? Text(string key) => TryGetValue(key, out var v) && v is string s ? s : null;

        public IReadOnlyList<string> List(string key) => TryGetValue(key, out var v)
            ? v switch
            {
                List<string> l => l,
                string s when s.Length > 0 => [s],
                _ => [],
            }
            : [];

        public Map? Child(string key) => TryGetValue(key, out var v) ? v as Map : null;
    }

    public static Map Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var root = new Map();
        if (text.Length > MaxLength)
        {
            return root;
        }

        Map? nested = null;
        string? listKey = null;
        Map? listOwner = null;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = StripComment(rawLine).TrimEnd();
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            var body = line.Trim();

            if (body.StartsWith("- ", StringComparison.Ordinal) || body == "-")
            {
                if (listKey is not null && listOwner is not null)
                {
                    if (listOwner[listKey] is not List<string> items)
                    {
                        items = [];
                        listOwner[listKey] = items;
                    }

                    items.Add(Scalar(body.Length > 1 ? body[2..] : string.Empty));
                }

                continue;
            }

            var colon = KeyEnd(body);
            if (colon <= 0)
            {
                continue;
            }

            var key = body[..colon].Trim().Trim('"', '\'');
            var value = body[(colon + 1)..].Trim();
            var target = indent > 0 && nested is not null ? nested : root;
            if (indent == 0)
            {
                nested = null;
            }

            if (value.Length == 0)
            {
                // A nested map or a block list follows.
                var child = new Map();
                target[key] = child;
                if (indent == 0)
                {
                    nested = child;
                }

                listKey = key;
                listOwner = target;
                continue;
            }

            listKey = null;
            target[key] = Value(value);
        }

        // Keys that turned out to be block lists replaced their empty map; empty maps with no entries stay maps.
        return root;
    }

    private static object Value(string value)
    {
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            return SplitTop(value[1..^1]).Select(Scalar).Where(s => s.Length > 0).ToList();
        }

        if (value.StartsWith('{') && value.EndsWith('}'))
        {
            var map = new Map();
            foreach (var part in SplitTop(value[1..^1]))
            {
                var c = KeyEnd(part);
                if (c > 0)
                {
                    map[part[..c].Trim().Trim('"', '\'')] = Value(part[(c + 1)..].Trim());
                }
            }

            return map;
        }

        return Scalar(value);
    }

    /// <summary>A plain, 'single' or "double" quoted scalar.</summary>
    public static string Scalar(string value)
    {
        var v = value.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < v.Length - 1; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length - 1)
                {
                    i++;
                    sb.Append(v[i] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => v[i],
                    });
                }
                else
                {
                    sb.Append(v[i]);
                }
            }

            return sb.ToString();
        }

        if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'')
        {
            return v[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return v is "~" or "null" ? string.Empty : v;
    }

    /// <summary>A string written so <see cref="Scalar"/> reads it back unchanged.</summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => c.ToString(CultureInfo.InvariantCulture),
            });
        }

        return sb.Append('"').ToString();
    }

    /// <summary>The colon that ends the key, outside quotes and followed by a space or the end.</summary>
    private static int KeyEnd(string body)
    {
        char? quote = null;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (quote is not null)
            {
                if (c == quote)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'' && i == 0)
            {
                quote = c;
            }
            else if (c == ':' && (i + 1 == body.Length || body[i + 1] == ' '))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Splits on commas that are not inside quotes, brackets or braces.</summary>
    private static List<string> SplitTop(string s)
    {
        var parts = new List<string>();
        var depth = 0;
        char? quote = null;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quote is not null)
            {
                if (c == '\\' && quote == '"')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '[' or '{')
            {
                depth++;
            }
            else if (c is ']' or '}')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(s[start..i].Trim());
                start = i + 1;
            }
        }

        if (start < s.Length)
        {
            parts.Add(s[start..].Trim());
        }

        return parts;
    }

    private static string StripComment(string line)
    {
        char? quote = null;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote is not null)
            {
                if (c == '\\' && quote == '"')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '#' && (i == 0 || line[i - 1] == ' '))
            {
                return line[..i];
            }
        }

        return line;
    }
}
