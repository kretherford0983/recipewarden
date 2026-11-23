using System.Text;
using RecipeWarden.Core.Import;

namespace RecipeWarden.Core.Shopping;

/// <summary>
/// FR-SHOP-04: a list as plain text or Markdown, for printing, pasting into a phone note or a message. Unticked items
/// only by default; pantry items follow under "You probably have". Names are plain text (BR-13); Markdown escapes them.
/// </summary>
public static class ShoppingText
{
    public static string Plain(string listName, ShoppingView view, bool includeChecked = false)
    {
        ArgumentNullException.ThrowIfNull(view);
        var sb = new StringBuilder();
        sb.Append(Clean(listName)).Append('\n');
        foreach (var section in view.Sections)
        {
            var items = section.Items.Where(i => includeChecked || !i.IsChecked).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            sb.Append('\n').Append(section.Name).Append('\n');
            foreach (var item in items)
            {
                sb.Append(item.IsChecked ? "[x] " : "[ ] ").Append(Describe(item, markdown: false)).Append('\n');
            }
        }

        if (view.Pantry.Count > 0)
        {
            sb.Append('\n').Append(ShoppingRules.PantryHeading).Append('\n');
            foreach (var item in view.Pantry)
            {
                sb.Append("- ").Append(Describe(item, markdown: false)).Append('\n');
            }
        }

        return sb.ToString();
    }

    public static string Markdown(string listName, ShoppingView view, bool includeChecked = false)
    {
        ArgumentNullException.ThrowIfNull(view);
        var sb = new StringBuilder();
        sb.Append("# ").Append(Escape(Clean(listName))).Append('\n');
        foreach (var section in view.Sections)
        {
            var items = section.Items.Where(i => includeChecked || !i.IsChecked).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            sb.Append("\n## ").Append(Escape(section.Name)).Append("\n\n");
            foreach (var item in items)
            {
                sb.Append(item.IsChecked ? "- [x] " : "- [ ] ").Append(Describe(item, markdown: true)).Append('\n');
            }
        }

        if (view.Pantry.Count > 0)
        {
            sb.Append("\n## ").Append(ShoppingRules.PantryHeading).Append("\n\n");
            foreach (var item in view.Pantry)
            {
                sb.Append("- ").Append(Describe(item, markdown: true)).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>"Butter — 1¼ cups (2½ sticks)".</summary>
    public static string Describe(ShoppingItem item, bool markdown)
    {
        ArgumentNullException.ThrowIfNull(item);
        var name = Clean(item.Name);
        var text = markdown ? Escape(name) : name;
        if (item.AmountText is { Length: > 0 } amount)
        {
            text += " — " + amount;
        }

        if (item.PackageText is { Length: > 0 } package)
        {
            text += " (" + package + ")";
        }

        return text;
    }

    private static string Clean(string? text) => ImportText.Line(text) ?? string.Empty;

    /// <summary>Markdown control characters in a name are escaped so a name cannot add links or formatting.</summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '#' or '|' or '!' or '(' or ')')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
