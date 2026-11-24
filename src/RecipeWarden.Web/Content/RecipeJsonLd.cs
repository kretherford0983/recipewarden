using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Html;
using RecipeWarden.Core.Organization;
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

    /// <summary>
    /// The JSON text (already safe to place inside a script element). Follows the WebKit HomeView example and Google's
    /// Recipe guidance: absolute URLs, yield as "N servings", ISO 8601 times, typed tags as category and cuisine (not
    /// repeated as keywords), named step sections as HowToSection, calories only from a complete estimate.
    /// </summary>
    /// <param name="baseUrl">This site's address ("https://kitchen.example"), for absolute links.</param>
    /// <param name="caloriesPerServing">Calories per serving when the nutrition estimate is complete; otherwise null.</param>
    public static string Json(RecipeView recipe, string baseUrl, decimal? caloriesPerServing)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(baseUrl);
        var root = baseUrl.TrimEnd('/');
        string? Media(string? id) => id is null ? null : $"{root}/media/{id}";

        var category = recipe.Tags.Where(t => t.TagType is TagType.Meal or TagType.Course).Select(t => t.Name).ToList();
        var cuisine = recipe.Tags.Where(t => t.TagType is TagType.Cuisine).Select(t => t.Name).ToList();
        var keywords = recipe.Tags.Where(t => t.TagType is not (TagType.Meal or TagType.Course or TagType.Cuisine)).Select(t => t.Name).ToList();

        Dictionary<string, object?> Step(StepView step) => Present(new Dictionary<string, object?>
        {
            ["@type"] = "HowToStep",
            ["name"] = step.Title,
            ["text"] = step.Text,
            ["image"] = Media(step.ImageId),
        });
        var instructions = new List<object>();
        foreach (var section in recipe.Steps)
        {
            if (section.Name is null)
            {
                instructions.AddRange(section.Steps.Select(Step));
            }
            else
            {
                instructions.Add(new Dictionary<string, object?>
                {
                    ["@type"] = "HowToSection",
                    ["name"] = section.Name,
                    ["itemListElement"] = section.Steps.Select(Step).ToList(),
                });
            }
        }

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Recipe",
            ["name"] = recipe.Title,
            ["url"] = $"{root}/recipes/{recipe.Slug}",
            ["description"] = CardSummary.PlainText(recipe.Description),
            ["image"] = Media(recipe.FinishImageId),
            ["author"] = recipe.SourceAuthor is { } author ? new Dictionary<string, object?> { ["@type"] = "Person", ["name"] = author } : null,
            ["dateModified"] = recipe.UpdatedAt == default ? null : recipe.UpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["recipeYield"] = ServingsRules.Format(recipe.Servings, recipe.ServingsMax) + " servings",
            ["prepTime"] = Duration(recipe.PrepMinutes),
            ["cookTime"] = Duration(recipe.CookMinutes),
            ["totalTime"] = Duration(recipe.TotalMinutes),
            ["recipeCategory"] = category.Count > 0 ? string.Join(", ", category) : null,
            ["recipeCuisine"] = cuisine.Count > 0 ? string.Join(", ", cuisine) : null,
            ["keywords"] = keywords.Count > 0 ? string.Join(", ", keywords) : null,
            ["nutrition"] = caloriesPerServing is { } kcal
                ? new Dictionary<string, object?>
                {
                    ["@type"] = "NutritionInformation",
                    ["calories"] = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(kcal, MidpointRounding.AwayFromZero):0} calories"),
                }
                : null,
            ["recipeIngredient"] = recipe.Ingredients.SelectMany(g => g.Lines).Select(l => IngredientText.Clean(l.Text)).ToList(),
            ["recipeInstructions"] = instructions,
        };
        return JsonSerializer.Serialize(Present(data), Options);
    }

    /// <summary>The JSON as markup for the script element; see the class remarks for why this is safe.</summary>
    public static IHtmlContent Html(RecipeView recipe, string baseUrl, decimal? caloriesPerServing) => new HtmlString(Json(recipe, baseUrl, caloriesPerServing));

    /// <summary>Leaves out the properties with no value.</summary>
    private static Dictionary<string, object?> Present(Dictionary<string, object?> values) =>
        values.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>ISO 8601 duration ("PT1H15M"); null when unknown.</summary>
    public static string? Duration(int? minutes) => minutes switch
    {
        null or <= 0 => null,
        < 60 => string.Create(CultureInfo.InvariantCulture, $"PT{minutes}M"),
        _ when minutes % 60 == 0 => string.Create(CultureInfo.InvariantCulture, $"PT{minutes / 60}H"),
        _ => string.Create(CultureInfo.InvariantCulture, $"PT{minutes / 60}H{minutes % 60}M"),
    };
}
