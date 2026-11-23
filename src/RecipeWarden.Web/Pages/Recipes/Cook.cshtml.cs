using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Recipes;

/// <summary>A piece of step text, or a timer inside it.</summary>
public sealed record StepPart(string Text, StepTimer? Timer);

/// <summary>
/// FR-VIEW-02, AC-23, ADR 0031: cook mode. Large type, ingredients and steps to tick (kept in this browser only),
/// timers found in the steps, and the screen kept awake where the browser allows. Read-only on the server (SEC-31).
/// </summary>
public sealed class CookModel(RecipeService recipes, Permissions permissions, RecipeScaler scaler, UserPreferenceStore preferences)
    : RecipeViewModel(recipes, permissions, scaler, preferences)
{
    protected override string RouteSuffix => "/cook";

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug, string? scale, string? servings, string? units, int? from, string? have) =>
        await LoadAsync(slug, scale, servings, units, from, have) ?? Page();

    /// <summary>The step text split around its timers, in order.</summary>
    public static IReadOnlyList<StepPart> Parts(string text)
    {
        var parts = new List<StepPart>();
        var at = 0;
        foreach (var timer in TimerRules.Find(text))
        {
            if (timer.Start > at)
            {
                parts.Add(new StepPart(text[at..timer.Start], null));
            }

            parts.Add(new StepPart(timer.Text, timer));
            at = timer.Start + timer.Length;
        }

        if (at < text.Length)
        {
            parts.Add(new StepPart(text[at..], null));
        }

        return parts;
    }
}
