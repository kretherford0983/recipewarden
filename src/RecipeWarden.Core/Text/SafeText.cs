using System.Text;

namespace RecipeWarden.Core.Text;

/// <summary>
/// Text from the browser can hold unpaired UTF-16 surrogates, which make <see cref="string.Normalize()"/> throw.
/// Every Core rule that normalizes user text goes through here, so bad input never becomes a server error.
/// </summary>
public static class SafeText
{
    /// <summary>Replaces unpaired surrogates with U+FFFD, then applies the normalization form.</summary>
    public static string Normalize(string text, NormalizationForm form)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ReplaceLoneSurrogates(text).Normalize(form);
    }

    public static string ReplaceLoneSurrogates(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var paired = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            if (paired)
            {
                sb?.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                sb ??= new StringBuilder(text, 0, i, text.Length);
                sb.Append('�');
                continue;
            }

            sb?.Append(c);
        }

        return sb?.ToString() ?? text;
    }
}
