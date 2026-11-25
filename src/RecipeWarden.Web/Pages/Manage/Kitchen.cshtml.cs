using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Infrastructure.Kitchen;

namespace RecipeWarden.Web.Pages.Manage;

/// <summary>The fields of the add and edit forms (SEC-13: only these bind).</summary>
public sealed class KitchenToolInput
{
    public ToolType Type { get; set; } = ToolType.MeasuringSpoon;
    public string? Name { get; set; }

    /// <summary>"⅛ tsp", "16 fl oz", "1 g", "9x13".</summary>
    public string? Size { get; set; }

    /// <summary>A scale's units: g, oz.</summary>
    public string[] Shows { get; set; } = [];

    public int Quantity { get; set; } = 1;
    public string? FillPct { get; set; }

    /// <summary>BR-50: markings, one set per line ("4 cups by 1/2").</summary>
    public string? Markings { get; set; }
}

/// <summary>
/// FR-TOOL-01/02, W5, route /manage/kitchen (7.2): My Kitchen. Editor-only by folder policy. Every change goes through
/// <see cref="KitchenService"/>, which increments the inventory version (BR-30).
/// </summary>
public sealed class KitchenModel(KitchenService kitchen) : PageModel
{
    public IReadOnlyList<KitchenToolRow> Tools { get; private set; } = [];
    public int Version { get; private set; }
    public string? Message { get; private set; }

    /// <summary>BR-51: a retire waiting for confirmation, with the sizes it would take away.</summary>
    public (KitchenToolInfo Tool, IReadOnlyList<string> Lost)? ConfirmRetire { get; private set; }
    public IReadOnlyList<string> Errors { get; private set; } = [];

    [BindProperty]
    public KitchenToolInput Input { get; set; } = new();

    public async Task OnGetAsync(string? done, int? tool)
    {
        Message = done switch { "added" => "Added.", "saved" => "Saved.", "retired" => "Retired.", "restored" => "Back in use.", _ => null };
        await LoadAsync();
        if (done == "retired" && Tools.FirstOrDefault(r => r.Tool.Id == tool) is { } retired)
        {
            // BR-51: which of its sizes another tool still gives.
            var (_, covered) = MeasureScales.IfRetired(retired.Tool, Tools.Where(r => r.Tool.IsActive).Select(r => r.Tool));
            if (covered.Count > 0)
            {
                Message += " " + string.Join("; ", covered.Select(c => c.Size + " is still covered by " + c.By)) + ".";
            }
        }

    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        var (tool, errors) = Read(Input);
        if (errors.Count > 0)
        {
            return await FailAsync(errors);
        }

        var result = await kitchen.AddAsync(tool, UserId, HttpContext.RequestAborted);
        return result.Ok ? Redirect("/manage/kitchen?done=added") : await FailAsync(result.Errors);
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id)
    {
        var (tool, errors) = Read(Input);
        if (errors.Count > 0)
        {
            return await FailAsync(errors);
        }

        var result = await kitchen.UpdateAsync(id, tool, UserId, HttpContext.RequestAborted);
        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Ok ? Redirect("/manage/kitchen?done=saved") : await FailAsync(result.Errors);
    }

    public async Task<IActionResult> OnPostPresetAsync(string? key)
    {
        var result = await kitchen.AddPresetAsync(key, UserId, HttpContext.RequestAborted);
        return result.NotFound ? NotFound() : Redirect("/manage/kitchen?done=added");
    }

    /// <summary>
    /// BR-51: retiring a measure that is the only one giving a size asks first ("Retiring this leaves you without
    /// ⅓ cup"); otherwise it retires and says which sizes another tool still covers.
    /// </summary>
    public async Task<IActionResult> OnPostRetireAsync(int id, bool confirm = false)
    {
        var rows = await kitchen.ListAsync(HttpContext.RequestAborted);
        if (rows.FirstOrDefault(r => r.Tool.Id == id) is not { } row)
        {
            return NotFound();
        }

        var (lost, covered) = MeasureScales.IfRetired(row.Tool, rows.Where(r => r.Tool.IsActive).Select(r => r.Tool));
        if (lost.Count > 0 && !confirm)
        {
            ConfirmRetire = (row.Tool, lost);
            await LoadAsync();
            return Page();
        }

        var result = await kitchen.SetActiveAsync(id, false, UserId, HttpContext.RequestAborted);
        if (result.NotFound)
        {
            return NotFound();
        }

        return Redirect("/manage/kitchen?done=retired&tool=" + id.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id) => await ActiveAsync(id, true);

    public static string Label(ToolType type) => type switch
    {
        ToolType.MeasuringSpoon => "Measuring spoon",
        ToolType.MeasuringCup => "Measuring cup",
        ToolType.LiquidMeasure => "Liquid measure (jug)",
        ToolType.Container => "Container",
        _ => type.ToString(),
    };

    public static string Fill(decimal pct) => pct.ToString("0.#", CultureInfo.InvariantCulture);

    private async Task<IActionResult> ActiveAsync(int id, bool active)
    {
        var result = await kitchen.SetActiveAsync(id, active, UserId, HttpContext.RequestAborted);
        return result.NotFound ? NotFound() : Redirect("/manage/kitchen?done=" + (active ? "restored" : "retired"));
    }

    private static (KitchenToolInfo Tool, IReadOnlyList<string> Errors) Read(KitchenToolInput input)
    {
        decimal fill = 100m;
        if (!string.IsNullOrWhiteSpace(input.FillPct)
            && !decimal.TryParse(input.FillPct.Trim().TrimEnd('%'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out fill))
        {
            return (new KitchenToolInfo(0, string.Empty, input.Type), ["Fill % must be a number, e.g. 90"]);
        }

        if (string.IsNullOrWhiteSpace(input.FillPct) && input.Type == ToolType.Container)
        {
            fill = 90m; // BR-110 default for bottles and jars
        }

        return KitchenRules.FromInput(input.Type, input.Name, input.Size, input.Shows.Where(u => u is "g" or "oz").ToList(),
            input.Quantity, fill, input.Markings);
    }

    private async Task<IActionResult> FailAsync(IReadOnlyList<string> errors)
    {
        Errors = errors;
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Tools = await kitchen.ListAsync(HttpContext.RequestAborted);
        Version = await kitchen.CurrentVersionAsync(HttpContext.RequestAborted);
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
