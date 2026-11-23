using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Infrastructure.Nutrition;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>The panel's fields as typed (SEC-13: only these bind).</summary>
public sealed class NutritionInput
{
    public string? Calories { get; set; }
    public string? FatG { get; set; }
    public string? SatFatG { get; set; }
    public string? CholesterolMg { get; set; }
    public string? SodiumMg { get; set; }
    public string? CarbsG { get; set; }
    public string? FiberG { get; set; }
    public string? SugarG { get; set; }
    public string? ProteinG { get; set; }

    public static NutritionInput From(NutrientValues? v) => new()
    {
        Calories = S(v?.Calories), FatG = S(v?.FatG), SatFatG = S(v?.SatFatG), CholesterolMg = S(v?.CholesterolMg), SodiumMg = S(v?.SodiumMg),
        CarbsG = S(v?.CarbsG), FiberG = S(v?.FiberG), SugarG = S(v?.SugarG), ProteinG = S(v?.ProteinG),
    };

    /// <summary>The values, or an error when a field is not a number.</summary>
    public (NutrientValues? Values, bool Bad) Parse()
    {
        var bad = false;
        decimal? N(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n))
            {
                return n;
            }

            bad = true;
            return null;
        }

        var v = new NutrientValues(N(Calories), N(FatG), N(SatFatG), N(CholesterolMg), N(SodiumMg), N(CarbsG), N(FiberG), N(SugarG), N(ProteinG));
        return (v, bad);
    }

    public string? Get(string name) => name switch
    {
        nameof(Calories) => Calories,
        nameof(FatG) => FatG,
        nameof(SatFatG) => SatFatG,
        nameof(CholesterolMg) => CholesterolMg,
        nameof(SodiumMg) => SodiumMg,
        nameof(CarbsG) => CarbsG,
        nameof(FiberG) => FiberG,
        nameof(SugarG) => SugarG,
        nameof(ProteinG) => ProteinG,
        _ => null,
    };

    private static string? S(decimal? v) => v?.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// FR-NUT-01, FR-NUT-02, FR-ALG-02, BR-50, BR-52, AC-56, ADR 0035/0036: a recipe's nutrition and allergens for editors —
/// how each line counted, a manual panel, and allergen overrides. Editor only (the /Manage folder policy).
/// </summary>
public sealed class NutritionModel(RecipeService recipes, NutritionService nutrition) : PageModel
{
    public const string BadNumber = "Nutrition values must be numbers, e.g. 410 or 14.5.";

    public RecipeForEdit? Recipe { get; private set; }
    public RecipeNutrition? Data { get; private set; }
    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    [BindProperty]
    public NutritionInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync([FromRoute] int id, string? saved)
    {
        if (await LoadAsync(id) is { } missing)
        {
            return missing;
        }

        Input = NutritionInput.From(Data!.Manual);
        Saved = saved switch { "panel" => "Nutrition saved.", "cleared" => "Manual nutrition cleared.", "allergens" => "Allergens saved.", _ => null };
        return Page();
    }

    public async Task<IActionResult> OnPostPanelAsync([FromRoute] int id, string? clear)
    {
        var (values, bad) = clear == "1" ? (null, false) : Input.Parse();
        if (bad)
        {
            Error = BadNumber;
            return await LoadAsync(id) ?? Page();
        }

        var result = await nutrition.SetManualAsync(id, values, Permissions.UserId(User), HttpContext.RequestAborted);
        if (!result.Ok)
        {
            if (result.Error == NutritionService.NotFound)
            {
                return NotFound();
            }

            Error = result.Error;
            return await LoadAsync(id) ?? Page();
        }

        return Redirect($"/manage/recipes/{id}/nutrition?saved=" + (values is null || values.IsEmpty ? "cleared" : "panel"));
    }

    /// <summary>One select per allergen: "auto" (use detection), contains, may, none.</summary>
    public async Task<IActionResult> OnPostAllergensAsync([FromRoute] int id)
    {
        var manual = new Dictionary<AllergenCode, AllergenStatus>();
        foreach (var code in AllergenRules.All)
        {
            var value = Request.Form["a_" + code].ToString();
            AllergenStatus? status = value switch
            {
                "contains" => AllergenStatus.Contains,
                "may" => AllergenStatus.MayContain,
                "none" => AllergenStatus.NotPresent,
                _ => null,
            };
            if (status is { } st)
            {
                manual[code] = st;
            }
        }

        var result = await nutrition.SetAllergensAsync(id, manual, HttpContext.RequestAborted);
        return result.Ok ? Redirect($"/manage/recipes/{id}/nutrition?saved=allergens") : NotFound();
    }

    public static string Choice(RecipeNutrition data, AllergenCode code) =>
        data.ManualAllergens.TryGetValue(code, out var s) ? s switch
        {
            AllergenStatus.Contains => "contains",
            AllergenStatus.MayContain => "may",
            _ => "none",
        } : "auto";

    private async Task<IActionResult?> LoadAsync(int id)
    {
        var ct = HttpContext.RequestAborted;
        Recipe = await recipes.GetForEditAsync(id, ct);
        Data = Recipe is null ? null : await nutrition.GetAsync(id, ct);
        return Recipe is null || Data is null ? NotFound() : null;
    }
}
