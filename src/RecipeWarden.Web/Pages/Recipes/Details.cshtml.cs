using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Kitchen;
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

    /// <summary>FR-SCL-04/05, ADR 0019: the container form's state and results.</summary>
    public ContainerSetup? Containers { get; private set; }
    public ContainerFit? Fit { get; private set; }
    public string? HowMany { get; private set; }
    public string? FitMessage { get; private set; }
    public bool JustCooked { get; private set; }

    [FromQuery(Name = "fit")]
    public int? FitToolId { get; set; }

    [FromQuery(Name = "capacity")]
    public string? FitCapacity { get; set; }

    [FromQuery(Name = "count")]
    public int? FitCount { get; set; }

    [FromQuery(Name = "fill")]
    public string? FitFill { get; set; }

    [FromQuery(Name = "yield")]
    public string? TypedYield { get; set; }

    [FromQuery(Name = "howmany")]
    public int? HowManyToolId { get; set; }

    /// <summary>ADR 0017: a reader's "Show me" — weights for this view only.</summary>
    [FromQuery(Name = "weights")]
    public string? Weights { get; set; }

    public bool ShowWeights => string.Equals(Weights, "show", StringComparison.Ordinal);

    public bool FitRequested => FitToolId is not null || !string.IsNullOrWhiteSpace(FitCapacity);

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug, string? scale, string? servings, string? units,
        int? from, string? have, string? made)
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

        JustCooked = made == "1" && CanEdit;
        var factor = 1m;
        var ct2 = HttpContext.RequestAborted;
        Containers = await scaler.ContainersAsync(Recipe, TypedYield, ct2);

        if (FitRequested)
        {
            if (Containers!.Error is { } yieldError)
            {
                FitMessage = yieldError;
            }
            else if (Choice(FitToolId, FitCapacity) is not { } choice)
            {
                FitMessage = "Choose a container from My Kitchen or type a capacity, e.g. 16 fl oz.";
            }
            else
            {
                Fit = ContainerRules.Fit(Containers.Yield!, choice);
                if (Fit.Factor is { } fitFactor)
                {
                    factor = fitFactor;
                    ScaleSource = "fit to " + (choice.Count > 1 ? choice.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " : string.Empty)
                                  + (choice.Count > 1 ? FoodMatcher.Plural(choice.Noun) : choice.Noun);
                }
                else
                {
                    FitMessage = Fit.Error;
                }
            }
        }
        else if (from is { } line && !string.IsNullOrWhiteSpace(have))
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

        Scaled = await scaler.ScaleAsync(Recipe, factor, display, ShowWeights, ct);
        if (HowManyToolId is { } howManyTool && Containers?.Yield is { } yield && Choice(howManyTool, null) is { } bottle)
        {
            HowMany = ContainerRules.HowMany(yield.Ml * factor, bottle);
        }
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

    /// <summary>The current URL with <c>weights=show</c> added (ADR 0017 "Show me").</summary>
    public string ShowMeLink()
    {
        var query = Request.Query.Where(q => q.Key != "weights").Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value.ToString()));
        return "/recipes/" + Recipe!.Slug + "?" + string.Join('&', query.Append("weights=show"));
    }

    /// <summary>A container from My Kitchen (by id) or a typed capacity, with count and fill % (BR-110).</summary>
    private ContainerChoice? Choice(int? toolId, string? capacity)
    {
        var count = Math.Clamp(FitCount ?? 1, 1, 20);
        decimal? fill = decimal.TryParse(FitFill?.Trim().TrimEnd('%'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var f) ? f : null;
        if (toolId is { } id && Containers?.Containers.FirstOrDefault(c => c.Id == id) is { CapacityMl: { } ml } tool)
        {
            return new ContainerChoice(ContainerRules.Noun(tool.Name), ml, UnitCatalog.FindByCode(tool.CapacityUnit)!, fill ?? tool.DefaultFillPct,
                count, tool.QuantityOwned);
        }

        if (AmountText.TryParse(capacity, out var typed) && typed!.Unit.Kind == UnitKind.Volume)
        {
            return new ContainerChoice("container", typed.Base, typed.Unit, fill ?? 90m, count, null);
        }

        return null;
    }

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
