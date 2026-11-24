using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Html;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Web.Content;

/// <summary>
/// UX-14, ADR 0050: the recipe as schema.org Recipe JSON-LD, the data block recipe sites carry for reader modes,
/// search and link previews. It is data, never run (type <c>application/ld+json</c>, so CSP script rules do not apply).
/// Safety (SEC-22): every value goes through System.Text.Json with <see cref="JavaScriptEncoder.Default"/>, which
/// writes &lt; &gt; &amp; ' " and + as \uXXXX escapes, so recipe text can never close the script element or start
/// markup. Besides <see cref="MarkdownRenderer"/> this is the only place the app emits markup from user text.
/// </summary>
public static class RecipeJsonLd
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = false,
    };

    /// <summary>The JSON text (already safe to place inside a script element).</summary>
    public static string Json(RecipeView recipe, string? imageUrl)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var steps = recipe.Steps.SelectMany(s => s.Steps).Select(step => Present(new Dictionary<string, object?>
        {
            ["@type"] = "HowToStep",
            ["name"] = step.Title,
            ["text"] = step.Text,
        })).ToList();
        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Recipe",
            ["name"] = recipe.Title,
            ["description"] = CardSummary.From(recipe.Description),
            ["image"] = imageUrl,
            ["author"] = recipe.SourceAuthor is { } author ? new Dictionary<string, object?> { ["@type"] = "Person", ["name"] = author } : null,
            ["recipeYield"] = ServingsRules.Format(recipe.Servings, recipe.ServingsMax),
            ["prepTime"] = Duration(recipe.PrepMinutes),
            ["cookTime"] = Duration(recipe.CookMinutes),
            ["totalTime"] = Duration(recipe.TotalMinutes),
            ["keywords"] = recipe.Tags.Count > 0 ? string.Join(", ", recipe.Tags.Select(t => t.Name)) : null,
            ["recipeIngredient"] = recipe.Ingredients.SelectMany(g => g.Lines).Select(l => IngredientText.Clean(l.Text)).ToList(),
            ["recipeInstructions"] = steps,
        };
        return JsonSerializer.Serialize(Present(data), Options);
    }

    /// <summary>Leaves out the properties with no value.</summary>
    private static Dictionary<string, object?> Present(Dictionary<string, object?> values) =>
        values.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>The JSON as markup for the script element; see the class remarks for why this is safe.</summary>
    public static IHtmlContent Html(RecipeView recipe, string? imageUrl) => new HtmlString(Json(recipe, imageUrl));

    /// <summary>ISO 8601 duration ("PT1H15M"); null when unknown.</summary>
    public static string? Duration(int? minutes) => minutes switch
    {
        null or <= 0 => null,
        < 60 => string.Create(CultureInfo.InvariantCulture, $"PT{minutes}M"),
        _ when minutes % 60 == 0 => string.Create(CultureInfo.InvariantCulture, $"PT{minutes / 60}H"),
        _ => string.Create(CultureInfo.InvariantCulture, $"PT{minutes / 60}H{minutes % 60}M"),
    };
}
