using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.AiParser;
using RecipeWarden.Infrastructure.Ai;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Pages.Admin;

/// <summary>
/// FR-IMP-09/10, SEC-44, SEC-45, AC-18, ADR 0042: AI parser settings (off by default), the write-only API key,
/// "Test connection", this month's usage and cap, and the call log.
/// </summary>
public sealed class AiModel(AiParserService ai, AuditLog audit) : PageModel
{
    public AiSettings Settings { get; private set; } = AiSettings.Off;
    public bool HasKey { get; private set; }
    public AiUsage Usage { get; private set; } = new(0, 0, 0, null);
    public IReadOnlyList<AiCallLog> Calls { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(string? done)
    {
        Message = done switch { "saved" => "AI settings saved.", _ => null };
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync(bool enabled, AiProvider provider, string? endpoint, string? model, string? apiKey, bool clearKey, string? cap,
        bool allowLan)
    {
        long? monthlyCap = null;
        if (!string.IsNullOrWhiteSpace(cap))
        {
            if (!long.TryParse(cap.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) || c < 0)
            {
                return await FailAsync("The monthly token limit must be a whole number.");
            }

            monthlyCap = c == 0 ? null : c;
        }

        var value = new AiSettings(enabled, provider, endpoint, model, monthlyCap, allowLan);
        var error = await ai.SaveSettingsAsync(value, apiKey, clearKey, HttpContext.RequestAborted);
        if (error is not null)
        {
            return await FailAsync(error);
        }

        // SEC-45: the audit says whether a key was set, never the key.
        await audit.RecordAsync("AiSettings.Update", "Settings", "ai",
            new { enabled, provider = provider.ToString(), model, keyChanged = !string.IsNullOrWhiteSpace(apiKey) || clearKey, cap = monthlyCap, allowLan },
            HttpContext.RequestAborted);
        return Redirect("/admin/ai?done=saved");
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        var error = await ai.TestAsync(HttpContext.RequestAborted);
        if (error is not null)
        {
            return await FailAsync("Test failed: " + error);
        }

        Message = "Connection works.";
        await LoadAsync();
        return Page();
    }

    private async Task<IActionResult> FailAsync(string error)
    {
        Error = error;
        await LoadAsync();
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        Settings = await ai.GetSettingsAsync(ct);
        HasKey = await ai.HasKeyAsync(ct);
        Usage = await ai.UsageThisMonthAsync(ct);
        Calls = await ai.RecentCallsAsync(50, ct);
    }
}
