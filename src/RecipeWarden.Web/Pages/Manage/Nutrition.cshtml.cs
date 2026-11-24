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
/// FR-NUT-03, FR-NUT-04, FR-NUT-05 (view), FR-NUT-07, BR-54, BR-116, AC-57, AC-101 to AC-103, ADR 0034, ADR 0051: the
/// unmatched-ingredient report with local suggestions, mapping an ingredient to a USDA or custom food by search, and
/// custom foods from a package label. Editor only.
/// </summary>
public sealed class NutritionModel(NutritionService nutrition, Infrastructure.Ai.AiParserService ai, FoodSuggestionService suggestions,
    Infrastructure.Python.PythonFeatures python) : PageModel
{
    public const string SuggestionsUnavailable =
        "Suggestions are not available right now, so nothing was suggested. You can still map each ingredient by searching below.";

    /// <summary>FR-NUT-07: the matcher's suggestion per report row (normalized name), for an Editor to confirm.</summary>
    public IReadOnlyDictionary<string, SuggestedMatch> Suggested { get; private set; } = new Dictionary<string, SuggestedMatch>();

    /// <summary>False when an Admin or the server owner turned Python features off (FR-OPS-06).</summary>
    public bool SuggestionsOn { get; private set; }

    /// <summary>FR-NUT-06: the AI's pick among the local candidates, for the Editor to confirm.</summary>
    public NutrientChoice? Suggestion { get; private set; }

    public bool AiEnabled { get; private set; }

    public IReadOnlyList<UnmatchedRow> Report { get; private set; } = [];
    public IReadOnlyList<NutrientChoice> Choices { get; private set; } = [];
    public DatasetSummary? Dataset { get; private set; }
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
        Message = done switch
        {
            "mapped" => "Mapped. Every recipe using it now counts it.",
            "label" => "Food added. Map an ingredient to it below.",
            "dismissed" => "Suggestion dismissed. It will not be suggested again.",
            _ => null,
        };
    }

    /// <summary>
    /// FR-NUT-07: asks the bundled matcher about the unmatched names that have no suggestion yet. Nothing is mapped;
    /// when the worker is unavailable the page says so and works as before (AC-103, BR-119).
    /// </summary>
    public async Task<IActionResult> OnPostSuggestMatchesAsync()
    {
        var report = await nutrition.UnmatchedAsync(HttpContext.RequestAborted);
        var run = await suggestions.SuggestAsync(report.Select(r => r.Name), HttpContext.RequestAborted);
        await LoadAsync();
        if (run.Failure != Core.Python.WorkerFailure.None)
        {
            Error = SuggestionsUnavailable;
        }
        else
        {
            Message = run.Asked == 0
                ? "Every ingredient on the list has already been looked at."
                : $"Looked at {run.Asked} ingredient{(run.Asked == 1 ? "" : "s")}: {run.Found} suggested match{(run.Found == 1 ? "" : "es")} to check.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDismissAsync(string? name)
    {
        await suggestions.DismissAsync(name, Permissions.UserId(User), HttpContext.RequestAborted);
        return Redirect("/manage/nutrition?done=dismissed");
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

        var candidates = await nutrition.SuggestionCandidatesAsync(name, HttpContext.RequestAborted);
        var (index, error) = await ai.SuggestMappingAsync(name, candidates.Select(c => c.Description).ToList(), HttpContext.RequestAborted);
        if (index is { } i)
        {
            Suggestion = candidates[i];
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
        Suggested = await suggestions.ForAsync(Report.Select(r => r.Name), ct);
        SuggestionsOn = (await python.StatusAsync(ct)).Status != Infrastructure.Python.PythonStatus.TurnedOff;
        Dataset = await nutrition.DatasetSummaryAsync(ct);
        AiEnabled = (await ai.GetSettingsAsync(ct)).Enabled;
        var search = Query ?? MapName;
        if (!string.IsNullOrWhiteSpace(search))
        {
            Choices = await nutrition.SearchAsync(search, ct);
        }
    }
}
