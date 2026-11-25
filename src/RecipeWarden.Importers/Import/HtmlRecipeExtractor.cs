using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Importers.Import;

/// <summary>
/// FR-IMP-01, FR-IMP-03, AC-10, BR-13: reads a recipe from a web page — schema.org Recipe JSON-LD first, then
/// microdata, then the heading-based fallback. Only text is taken; scripts never run (AngleSharp without a
/// scripting engine), and JSON-LD is read as data. The page address becomes the source (BR-10).
/// </summary>
public static class HtmlRecipeExtractor
{
    public const int MaxJsonLength = 2 * 1024 * 1024;
    private const int MaxTagsFromPage = 6;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64,
    };

    /// <summary>The recipe on the page, or null when none was found.</summary>
    public static ImportedRecipe? Extract(string html, Uri? pageUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        var parser = new HtmlParser(new HtmlParserOptions { IsScripting = false });
        using var doc = parser.ParseDocument(html);

        var recipe = FromJsonLd(doc, pageUrl) ?? FromMicrodata(doc, pageUrl) ?? FromHeadings(doc);
        if (recipe is null || !recipe.HasContent)
        {
            return null;
        }

        var siteName = Meta(doc, "og:site_name");
        var host = pageUrl?.IdnHost is { Length: > 0 } h ? (h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..] : h) : null;
        return recipe with
        {
            Title = recipe.Title ?? ImportText.Line(Meta(doc, "og:title") ?? doc.QuerySelector("h1")?.TextContent ?? doc.Title, 200),
            Description = recipe.Description ?? ImportText.Block(Meta(doc, "og:description") ?? Meta(doc, "description"), RecipeEdit.MaxDescriptionLength),
            ImageUrl = recipe.ImageUrl ?? Absolute(Meta(doc, "og:image"), pageUrl),
            SourceType = pageUrl is null ? null : SourceType.Web,
            SourceUrl = pageUrl?.AbsoluteUri,
            SourceName = recipe.SourceName ?? ImportText.Line(siteName, 200) ?? host,
        };
    }

    private static ImportedRecipe? FromJsonLd(IHtmlDocument doc, Uri? pageUrl)
    {
        foreach (var script in doc.QuerySelectorAll("script"))
        {
            var type = script.GetAttribute("type") ?? string.Empty;
            if (!type.Contains("ld+json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = script.TextContent;
            if (json.Length > MaxJsonLength)
            {
                continue;
            }

            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(json.Trim(), JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            using (parsed)
            {
                if (FindRecipe(parsed.RootElement, 0) is { } node)
                {
                    return FromJsonRecipe(node, pageUrl);
                }
            }
        }

        return null;
    }

    private static JsonElement? FindRecipe(JsonElement el, int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (FindRecipe(item, depth + 1) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (el.TryGetProperty("@type", out var type) && Strings(type).Any(t => t.Equals("Recipe", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("/Recipe", StringComparison.OrdinalIgnoreCase)))
        {
            return el;
        }

        foreach (var key in new[] { "@graph", "mainEntity", "mainEntityOfPage", "itemListElement", "item" })
        {
            if (el.TryGetProperty(key, out var child) && FindRecipe(child, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static ImportedRecipe FromJsonRecipe(JsonElement r, Uri? pageUrl)
    {
        string? Prop(string name) => r.TryGetProperty(name, out var v) ? Str(v) : null;
        IEnumerable<string> List(string name) => r.TryGetProperty(name, out var v) ? Strings(v) : [];

        var ingredients = ImportText.Lines(List("recipeIngredient").Concat(List("ingredients")), RecipeEdit.MaxIngredientLineLength);
        var steps = r.TryGetProperty("recipeInstructions", out var ins) ? Instructions(ins) : [];
        var (servings, yield, remark) = ImportRules.ServingsAndYield(List("recipeYield"));
        var tags = List("recipeCategory").Concat(List("recipeCuisine"))
            .SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(t => ImportText.Line(t, NameRules.MaxTagLength))
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!)
            .DistinctBy(NameRules.Normalize)
            .Take(MaxTagsFromPage)
            .ToList();
        string? publisher = null;
        if (r.TryGetProperty("publisher", out var pub))
        {
            publisher = Str(pub);
        }

        return new ImportedRecipe
        {
            Title = ImportText.Line(Prop("name") ?? Prop("headline"), 200),
            Description = ImportText.Block(Prop("description"), RecipeEdit.MaxDescriptionLength),
            Servings = servings,
            Yield = yield,
            PrepTime = ImportRules.Minutes(Prop("prepTime")),
            CookTime = ImportRules.Minutes(Prop("cookTime")),
            TotalTime = ImportRules.Minutes(Prop("totalTime")),
            Ingredients = ingredients.Count == 0 ? null : string.Join('\n', ingredients),
            Steps = steps.Count == 0 ? null : StepTextRules.Join(steps),
            Notes = remark,
            Tags = tags,
            SourceAuthor = ImportText.Line(r.TryGetProperty("author", out var author) ? Str(author) : null, 200),
            SourceName = ImportText.Line(publisher, 200),
            ImageUrl = r.TryGetProperty("image", out var image) ? Absolute(ImageOf(image), pageUrl) : null,
            Remarks = remark is null ? [] : [remark],
            Method = ExtractionMethod.JsonLd,
            Nutrition = r.TryGetProperty("nutrition", out var nutrition) && nutrition.ValueKind == JsonValueKind.Object
                ? NutritionText.FromSchemaOrg(name => nutrition.TryGetProperty(name, out var v) ? Str(v) : null)
                : null,
        };
    }

    /// <summary>recipeInstructions: text, a list of strings, HowToStep, HowToSection or ItemList.</summary>
    private static List<StepText> Instructions(JsonElement el)
    {
        var steps = new List<StepText>();
        Add(el, null, 0);
        return steps;

        void Add(JsonElement e, string? section, int depth)
        {
            if (depth > 6 || steps.Count >= StepTextRules.MaxSteps)
            {
                return;
            }

            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    foreach (var line in (ImportText.Block(e.GetString()) ?? string.Empty).Split('\n'))
                    {
                        var t = ImportText.Line(line, StepTextRules.MaxStepLength);
                        if (!string.IsNullOrEmpty(t))
                        {
                            steps.Add(new StepText(section, t));
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray())
                    {
                        Add(item, section, depth + 1);
                    }

                    break;
                case JsonValueKind.Object:
                    var types = e.TryGetProperty("@type", out var type) ? Strings(type).ToList() : [];
                    if (e.TryGetProperty("itemListElement", out var items))
                    {
                        var name = types.Any(t => t.Contains("Section", StringComparison.OrdinalIgnoreCase)) && e.TryGetProperty("name", out var n)
                            ? ImportText.Line(Str(n), 120)
                            : section;
                        Add(items, name, depth + 1);
                        break;
                    }

                    var text = e.TryGetProperty("text", out var tx) ? ImportText.Line(Str(tx), StepTextRules.MaxStepLength) : null;
                    var title = e.TryGetProperty("name", out var nm) ? ImportText.Line(Str(nm), StepTextRules.MaxTitleLength) : null;
                    if (text is null)
                    {
                        text = title;
                        title = null;
                    }

                    // Many sites repeat the text (or its start) as the step name.
                    if (title is not null && text is not null && (text.StartsWith(title.TrimEnd('.', '…'), StringComparison.OrdinalIgnoreCase) || title.Length > 60))
                    {
                        title = null;
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        steps.Add(new StepText(section, text, title));
                    }

                    break;
            }
        }
    }

    private static ImportedRecipe? FromMicrodata(IHtmlDocument doc, Uri? pageUrl)
    {
        var scope = doc.QuerySelectorAll("[itemscope][itemtype]")
            .FirstOrDefault(e => (e.GetAttribute("itemtype") ?? string.Empty).Split(' ')
                .Any(t => t.TrimEnd('/').EndsWith("schema.org/Recipe", StringComparison.OrdinalIgnoreCase)));
        if (scope is null)
        {
            return null;
        }

        List<IElement> Props(string name) => scope.QuerySelectorAll("[itemprop]")
            .Where(e => (e.GetAttribute("itemprop") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.OrdinalIgnoreCase)
                && OwnerScope(e) == scope)
            .ToList();
        string? One(string name) => Props(name).Select(ValueOf).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var ingredients = ImportText.Lines(Props("recipeIngredient").Concat(Props("ingredients")).Select(ValueOf), RecipeEdit.MaxIngredientLineLength);
        var steps = new List<StepText>();
        foreach (var el in Props("recipeInstructions"))
        {
            var lis = el.QuerySelectorAll("li").ToList();
            List<string?> parts = el.HasAttribute("itemscope")
                ? [ValueOf(el.QuerySelector("[itemprop~=text]") ?? el)]
                : lis.Count > 0 ? lis.Select(li => (string?)HtmlText.TextOf(li)).ToList() : HtmlText.TextOf(el).Split('\n').Select(s => (string?)s).ToList();
            steps.AddRange(ImportText.Lines(parts, StepTextRules.MaxStepLength).Select(t => new StepText(null, t)));
        }

        var (servings, yield, remark) = ImportRules.ServingsAndYield(Props("recipeYield").Select(ValueOf));
        var image = Props("image").Select(e => e.GetAttribute("src") ?? e.GetAttribute("content") ?? e.GetAttribute("href")).FirstOrDefault(v => v is not null);
        return new ImportedRecipe
        {
            Title = ImportText.Line(One("name"), 200),
            Description = ImportText.Block(One("description"), RecipeEdit.MaxDescriptionLength),
            Servings = servings,
            Yield = yield,
            PrepTime = ImportRules.Minutes(One("prepTime")),
            CookTime = ImportRules.Minutes(One("cookTime")),
            TotalTime = ImportRules.Minutes(One("totalTime")),
            Ingredients = ingredients.Count == 0 ? null : string.Join('\n', ingredients),
            Steps = steps.Count == 0 ? null : StepTextRules.Join(steps),
            Notes = remark,
            SourceAuthor = ImportText.Line(One("author"), 200),
            ImageUrl = Absolute(image, pageUrl),
            Remarks = remark is null ? [] : [remark],
            Method = ExtractionMethod.Microdata,
        };

        static IElement? OwnerScope(IElement e)
        {
            for (var p = e.ParentElement; p is not null; p = p.ParentElement)
            {
                if (p.HasAttribute("itemscope"))
                {
                    return p;
                }
            }

            return null;
        }
    }

    private static string? ValueOf(IElement e) => e.LocalName switch
    {
        "meta" => e.GetAttribute("content"),
        "time" => e.GetAttribute("datetime") ?? e.TextContent,
        "img" or "source" => e.GetAttribute("src"),
        "a" or "link" when e.GetAttribute("itemprop")?.Contains("image", StringComparison.OrdinalIgnoreCase) == true => e.GetAttribute("href"),
        "data" or "meter" => e.GetAttribute("value"),
        _ => e.GetAttribute("content") ?? HtmlText.TextOf(e),
    };

    private static ImportedRecipe? FromHeadings(IHtmlDocument doc)
    {
        var root = (IElement?)doc.QuerySelector("article") ?? (IElement?)doc.QuerySelector("main") ?? doc.Body;
        if (root is null)
        {
            return null;
        }

        var text = HtmlText.Of(root);
        var all = TextRecipeExtractor.Extract(text);
        var found = all.Count > 0 ? all[0] : null;
        return found is null ? null : found with { Method = ExtractionMethod.Heuristic, Title = ImportText.Line(doc.QuerySelector("h1")?.TextContent, 200) ?? found.Title };
    }

    private static string? Meta(IHtmlDocument doc, string name) =>
        doc.QuerySelectorAll("meta").FirstOrDefault(m =>
            string.Equals(m.GetAttribute("property"), name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase))?.GetAttribute("content");

    /// <summary>A string, number, the first string of an array, or an object's name, @value or text.</summary>
    private static string? Str(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.Array => e.EnumerateArray().Select(Str).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
        JsonValueKind.Object => (e.TryGetProperty("name", out var n) ? Str(n) : null)
            ?? (e.TryGetProperty("@value", out var v) ? Str(v) : null)
            ?? (e.TryGetProperty("text", out var t) ? Str(t) : null),
        _ => null,
    };

    private static List<string> Strings(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.EnumerateArray().Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList(),
        _ => Str(e) is { } s ? [s] : [],
    };

    private static string? ImageOf(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Array => e.EnumerateArray().Select(ImageOf).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
        JsonValueKind.Object => (e.TryGetProperty("url", out var u) ? ImageOf(u) : null)
            ?? (e.TryGetProperty("contentUrl", out var c) ? ImageOf(c) : null),
        _ => null,
    };

    /// <summary>An absolute http(s) address, resolved against the page; anything else (data:, javascript:) is dropped.</summary>
    private static string? Absolute(string? url, Uri? pageUrl)
    {
        var t = url?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > SourceRules.MaxUrlLength)
        {
            return null;
        }

        // On Linux "/img/a.jpg" parses as an absolute file: URI, so only addresses with a scheme count as absolute.
        Uri? uri;
        var hasScheme = t.Contains("://", StringComparison.Ordinal) || t.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);
        if (hasScheme ? !Uri.TryCreate(t, UriKind.Absolute, out uri) : (pageUrl is null || !Uri.TryCreate(pageUrl, t, out uri)))
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;
    }
}
