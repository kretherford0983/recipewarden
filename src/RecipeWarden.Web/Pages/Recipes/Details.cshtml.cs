using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Recipes;

/// <summary>
/// FR-VIEW-01 reading view. Anonymous (7.2); readers never see edit controls (UX-11) or drafts (ADR 0009).
/// An earlier slug of a published recipe redirects permanently to the current one (BR-02, ADR 0008).
/// <para>FR-SCL-01/02/03, FR-UI-02, ADR 0014: <c>?scale=</c>, <c>?servings=</c>, <c>?from=&amp;have=</c> and
/// <c>?units=</c> show the recipe scaled and converted. GET only: nothing is stored.</para>
/// </summary>
public sealed class DetailsModel(RecipeService recipes, CollectionService collections, Permissions permissions,
    RecipeScaler scaler, UserPreferenceStore preferences) : PageModel
{
    public const string ScaleCookie = "rw-scale";

    public RecipeView? Recipe { get; private set; }
    public ScaledRecipe? Scaled { get; private set; }
    public bool CanEdit { get; private set; }
    public IReadOnlyList<CollectionSummary> AvailableCollections { get; private set; } = [];

    /// <summary>Why the requested scale was not used (shown, never an error page).</summary>
    public string? ScaleMessage { get; private set; }

    /// <summary>How the factor was chosen, e.g. "from 3 lb chicken" or "your default".</summary>
    public string? ScaleSource { get; private set; }

    public decimal? TargetServings { get; private set; }

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug, string? scale, string? servings, string? units,
        int? from, string? have)
    {
        var ct = HttpContext.RequestAborted;
        CanEdit = await permissions.CanEditAsync(User);
        var lookup = await recipes.GetForReadingAsync(slug, canSeeDrafts: CanEdit, ct);
        if (lookup.RedirectToSlug is { } current)
        {
            return RedirectPermanent("/recipes/" + current + Request.QueryString);
        }

        if (lookup.Recipe is null)
        {
            return NotFound();
        }

        Recipe = lookup.Recipe;
        var (profileUnits, profileScale) = await ProfileAsync(ct);
        Request.Cookies.TryGetValue(DisplayUnitsRules.CookieName, out var unitsCookie);
        var display = DisplayUnitsRules.Resolve(units, profileUnits, unitsCookie);

        var factor = 1m;
        if (from is { } line && !string.IsNullOrWhiteSpace(have))
        {
            var result = await scaler.FactorFromAsync(Recipe, line, have, ct);
            if (result.Factor is { } f)
            {
                factor = f;
                var name = Recipe.Ingredients.SelectMany(g => g.Lines).ElementAt(line).Parsed?.Name;
                ScaleSource = "from " + have.Trim() + (string.IsNullOrEmpty(name) ? string.Empty : " " + name);
            }
            else
            {
                ScaleMessage = result.Error;
            }
        }
        else if (!string.IsNullOrWhiteSpace(scale) || !string.IsNullOrWhiteSpace(servings))
        {
            var choice = ScaleRules.Parse(scale, servings, Recipe.Servings);
            factor = choice.Factor;
            TargetServings = choice.TargetServings;
            ScaleMessage = choice.Error;
        }
        else if (DefaultScale(profileScale) is { } preferred)
        {
            factor = preferred;
            ScaleSource = "your default";
        }

        Scaled = await scaler.ScaleAsync(Recipe, factor, display, ct);
        if (CanEdit)
        {
            var inThese = Recipe.Collections.Select(c => c.Id).ToHashSet();
            AvailableCollections = (await collections.ListAsync(ct)).Where(c => !inThese.Contains(c.Id)).ToList();
        }

        return Page();
    }

    /// <summary>The query string for this view at another scale or units, keeping the rest.</summary>
    public string Link(decimal? factor = null, DisplayUnits? units = null)
    {
        var f = factor ?? Scaled!.Factor;
        var u = units ?? Scaled!.Units;
        var parts = new List<string>();
        if (f != 1m || factor is not null)
        {
            parts.Add("scale=" + ScaleRules.ToQuery(f));
        }

        if (u != DisplayUnits.AsWritten || units is not null)
        {
            parts.Add("units=" + DisplayUnitsRules.ToValue(u));
        }

        return "/recipes/" + Recipe!.Slug + (parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts));
    }

    public static string Number(decimal value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private async Task<(DisplayUnits? Units, decimal? Scale)> ProfileAsync(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? (null, null) : await preferences.GetDisplayAsync(userId, ct);
    }

    /// <summary>FR-UI-02: the profile's default scale, else the cookie's; ignored when out of range.</summary>
    private decimal? DefaultScale(decimal? profile)
    {
        if (profile is { } p && ScaleRules.InRange(p))
        {
            return p;
        }

        return Request.Cookies.TryGetValue(ScaleCookie, out var cookie) && ScaleRules.TryParseNumber(cookie, out var c)
            && ScaleRules.InRange(c) && c != 1m ? c : null;
    }
}
