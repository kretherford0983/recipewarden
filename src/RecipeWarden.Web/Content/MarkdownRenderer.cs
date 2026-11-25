using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Html;

namespace RecipeWarden.Web.Content;

/// <summary>
/// SEC-22, AC-76: user Markdown (recipe descriptions and notes, collection descriptions) is rendered with Markdig
/// with raw HTML disabled, then passed through an HtmlSanitizer allow-list. This is the only place the app turns
/// user text into markup; Html.Raw stays banned (BannedSymbols.txt).
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()                 // <script>, <img onerror> etc. become literal text
        .UseReferralLinks("nofollow", "noopener", "noreferrer")
        .UseAutoLinks()
        .UseEmphasisExtras()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static IHtmlContent Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return HtmlString.Empty;
        }

        var html = Markdown.ToHtml(markdown, Pipeline);
        return new HtmlString(Sanitizer.Sanitize(html));
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "em", "b", "i", "del", "ul", "ol", "li", "a", "code", "pre", "blockquote", "h2", "h3", "h4", "hr" })
        {
            s.AllowedTags.Add(tag);
        }

        s.AllowedAttributes.Clear();
        s.AllowedAttributes.Add("href");
        s.AllowedAttributes.Add("rel");
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");
        s.UriAttributes.Clear();
        s.UriAttributes.Add("href");
        s.AllowedCssProperties.Clear();
        s.AllowedAtRules.Clear();
        s.AllowDataAttributes = false;
        s.KeepChildNodes = true;   // a removed tag keeps its text
        return s;
    }
}
