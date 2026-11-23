using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Nutrition;
using RecipeWarden.Web.Pages.Manage.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage;

/// <summary>
/// FR-NUT-03, FR-NUT-04, FR-NUT-05 (view), BR-54, AC-57, ADR 0034: the unmatched-ingredient report, mapping an
/// ingredient to a USDA or custom food by search, and custom foods from a package label. Editor only.
/// </summary>
public sealed class NutritionModel(NutritionService nutrition, Infrastructure.Ai.AiParserService ai) : PageModel
{
    /// <summary>FR-NUT-06: the AI's pick among the local candidates, for the Editor to confirm.</summary>
    public NutrientChoice? Suggestion { get; private set; }

    public bool AiEnabled { get; private set; }

    public IReadOnlyList<UnmatchedRow> Report { get; private set; } = [];
    public IReadOnlyList<NutrientChoice> Choices { get; private set; } = [];
    public NutrientDataset? Dataset { get; private set; }
    public string? Error { get; private set; }
    public string? Message { get; private set; }

    /// <summary>The ingredient being mapped (from the report), and the search for it.</summary>
    [FromQuery(Name = "map")]
    public string? MapName { get; set; }

    [FromQuery(Name = "food")]
    public int? MapFood { get; set; }

    [FromQuery(Name = "q")]
    public string? Query { get; set; }

    [BindProperty]
    public NutritionInput Label { get; set; } = new();

    public async Task OnGetAsync(string? done)
    {
        await LoadAsync();
        Message = done switch { "mapped" => "Mapped. Every recipe using it now counts it.", "label" => "Food added. Map an ingredient to it below.", _ => null };
    }

    public async Task<IActionResult> OnPostMapAsync(int? foodId, string? name, int nutrientId)
    {
        var result = await nutrition.MapAsync(foodId, name, nutrientId, Permissions.UserId(User), HttpContext.RequestAborted);
        if (result.Ok)
        {
            return Redirect("/manage/nutrition?done=mapped");
        }

        Error = result.Error;
        MapName = name;
        MapFood = foodId;
        await LoadAsync();
        return Page();
    }

    /// <summary>
    /// FR-NUT-06, ADR 0042: asks the AI parser to choose one of the local search results for the ingredient. Only the
    /// ingredient name and the candidates' descriptions are sent (SEC-46); the suggestion is shown with "Use this" and
    /// nothing is mapped until the Editor confirms it.
    /// </summary>
    public async Task<IActionResult> OnPostSuggestAsync(string? name, int? foodId)
    {
        MapName = name;
        MapFood = foodId;
        await LoadAsync();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Page();
        }

        var (index, error) = await ai.SuggestMappingAsync(name, Choices.Select(c => c.Description).ToList(), HttpContext.RequestAborted);
        if (index is { } i)
        {
            Suggestion = Choices[i];
            Message = "Suggested by AI — check it before using it.";
        }
        else
        {
            Error = error;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostLabelAsync(string? labelName, string? serving, string? servingUnit, string? density)
    {
        var (values, bad) = Label.Parse();
        if (bad || !decimal.TryParse(serving, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size)
            || (!string.IsNullOrWhiteSpace(density) && !decimal.TryParse(density, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)))
        {
            Error = NutritionRules.BadServing + " " + Recipes.NutritionModel.BadNumber;
            await LoadAsync();
            return Page();
        }

        decimal? d = decimal.TryParse(density, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var dv) ? dv : null;
        var (result, id) = await nutrition.AddLabelFoodAsync(labelName, size, servingUnit == "ml", d, values!, Permissions.UserId(User), HttpContext.RequestAborted);
        if (!result.Ok)
        {
            Error = result.Error;
            await LoadAsync();
            return Page();
        }

        return Redirect("/manage/nutrition?done=label&q=" + Uri.EscapeDataString(labelName!.Trim()) + "#label-" + id);
    }

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        Report = await nutrition.UnmatchedAsync(ct);
        Dataset = await nutrition.DatasetAsync(ct);
        AiEnabled = (await ai.GetSettingsAsync(ct)).Enabled;
        var search = Query ?? MapName;
        if (!string.IsNullOrWhiteSpace(search))
        {
            Choices = await nutrition.SearchAsync(search, ct);
        }
    }
}
