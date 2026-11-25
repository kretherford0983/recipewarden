using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Measurement;

namespace RecipeWarden.Web.Pages.Manage;

/// <summary>
/// FR-MEAS-05, route /manage/conversions (7.2): densities and aliases, vague amounts and yield conversions.
/// Editor-only by folder policy. Every change is checked by <see cref="FoodRules"/> in Core.
/// </summary>
public sealed class ConversionsModel(FoodCatalogService foods) : PageModel
{
    public const string BadNumber = "Enter a number, e.g. 0.53";

    public IReadOnlyList<FoodRow> Foods { get; private set; } = [];
    public IReadOnlyList<VagueRow> Vague { get; private set; } = [];
    public IReadOnlyList<YieldRow> Yields { get; private set; } = [];
    public string? Filter { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(string? q, string? done)
    {
        Message = done switch { "saved" => "Saved.", "deleted" => "Deleted.", "added" => "Added.", _ => null };
        await LoadAsync(q);
    }

    public async Task<IActionResult> OnPostAddFoodAsync(string? name, string? density, string? aliases)
    {
        if (!TryNumber(density, out var d))
        {
            return await FailAsync(BadNumber);
        }

        return await ResultAsync(await foods.AddFoodAsync(name, d, aliases, UserId, HttpContext.RequestAborted), "added");
    }

    public async Task<IActionResult> OnPostUpdateFoodAsync(int id, string? density, string? aliases, string? form, string? q)
    {
        if (!TryNumber(density, out var d))
        {
            return await FailAsync(BadNumber, q);
        }

        FoodForm? parsedForm = Enum.TryParse<FoodForm>(form, ignoreCase: false, out var f) && Enum.IsDefined(f) ? f : null;
        if (!string.IsNullOrEmpty(form) && parsedForm is null)
        {
            return BadRequest();
        }

        return await ResultAsync(await foods.UpdateFoodAsync(id, d, aliases, parsedForm, UserId, HttpContext.RequestAborted), "saved", q);
    }

    /// <summary>ADR 0030: store section, pantry staple and package size for shopping lists.</summary>
    public async Task<IActionResult> OnPostUpdateShoppingAsync(int id, string? section, bool pantry, string? packageSize, string? packageUnit,
        string? packageName, string? q)
    {
        if (!TryNumber(packageSize, out var size))
        {
            return await FailAsync(BadNumber, q);
        }

        return await ResultAsync(await foods.UpdateShoppingAsync(id, section, pantry, size, packageUnit, packageName, UserId,
            HttpContext.RequestAborted), "saved", q);
    }

    public async Task<IActionResult> OnPostDeleteFoodAsync(int id)
    {
        await foods.DeleteFoodAsync(id, HttpContext.RequestAborted);
        return Redirect("/manage/conversions?done=deleted");
    }

    public async Task<IActionResult> OnPostUpdateVagueAsync(int id, string? teaspoons)
    {
        if (!TryNumber(teaspoons, out var tsp) || tsp is null)
        {
            return await FailAsync(BadNumber);
        }

        return await ResultAsync(await foods.UpdateVagueAsync(id, tsp.Value, HttpContext.RequestAborted), "saved");
    }

    public async Task<IActionResult> OnPostUpdateYieldAsync(int id, string? lowPct, string? highPct, string? density, string? basis)
    {
        // Entered as cooking-yield percentages (as the USDA table gives them); stored as grams bought per gram prepared.
        if (!TryNumber(lowPct, out var low) || !TryNumber(highPct, out var high) || low is not > 0m || high is not > 0m
            || !TryNumber(density, out var d))
        {
            return await FailAsync(BadNumber);
        }

        var ratio = Math.Round(100m / high.Value, 4);
        var ratioMax = Math.Round(100m / low.Value, 4);
        return await ResultAsync(await foods.UpdateYieldAsync(id, ratio, ratioMax, d, basis, UserId, HttpContext.RequestAborted), "saved");
    }

    /// <summary>A yield ratio shown as the cooking-yield percentage it came from (1.1364 → 88).</summary>
    public static string Percent(decimal ratio) => Math.Round(100m / ratio, 1).ToString("0.#", CultureInfo.InvariantCulture);

    public static string Number(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Blank means "none"; anything else must be a plain number.</summary>
    private static bool TryNumber(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (text.Length <= ScaleRules.MaxTextLength
            && decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v))
        {
            value = v;
            return true;
        }

        return false;
    }

    private async Task<IActionResult> ResultAsync(ConversionEditResult result, string done, string? q = null)
    {
        if (result.Ok)
        {
            return Redirect("/manage/conversions?done=" + done + (string.IsNullOrWhiteSpace(q) ? string.Empty : "&q=" + Uri.EscapeDataString(q)));
        }

        return result.Error == FoodCatalogService.NotFound ? NotFound() : await FailAsync(result.Error!, q);
    }

    private async Task<IActionResult> FailAsync(string error, string? q = null)
    {
        Error = error;
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await LoadAsync(q);
        return Page();
    }

    private async Task LoadAsync(string? q)
    {
        var ct = HttpContext.RequestAborted;
        Filter = q?.Trim();
        Foods = await foods.ListFoodsAsync(Filter, ct);
        Vague = await foods.ListVagueAsync(ct);
        Yields = await foods.ListYieldsAsync(ct);
    }
}
