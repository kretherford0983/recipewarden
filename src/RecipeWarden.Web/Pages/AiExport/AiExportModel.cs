using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.AiExchange;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.AiExchange;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.AiExport;

/// <summary>
/// FR-EXP-04, FR-EXP-07, BR-90, BR-91, BR-94 to BR-97, AC-26, AC-28, AC-29, ADR 0033: Export for AI for one recipe, a
/// collection or all recipes. GET only (nothing is stored); readers get published recipes, editors drafts too. The
/// text is built here and copied or downloaded in the browser exactly as shown.
/// </summary>
public sealed class AiExportModel(RecipeService recipes, RecipeExporter exporter, AiExchangeService ai, Permissions permissions,
    UserPreferenceStore preferences) : PageModel
{
    public string Heading { get; private set; } = string.Empty;
    public string BackLink { get; private set; } = "/";
    public string? Slug { get; private set; }
    public bool IsSingle { get; private set; }
    public bool CanEdit { get; private set; }
    public int RecipeCount { get; private set; }
    public IReadOnlyList<string> Conflicts { get; private set; } = [];
    public string? Text { get; private set; }
    public string FileName { get; private set; } = "recipes-for-ai.md";

    /// <summary>The form's values, read from the query (GET form).</summary>
    public IReadOnlySet<AiGoalKind> Chosen { get; private set; } = new HashSet<AiGoalKind>();

    public string? Value(string key) => Request.Query[key].ToString() is { Length: > 0 } v ? v : null;

    public bool Checked(string key, bool whenNew) => Value("go") is null ? whenNew : Value(key) == "1";

    public async Task<IActionResult> OnGetAsync([FromRoute] string? slug, CancellationToken cancellationToken)
    {
        CanEdit = await permissions.CanEditAsync(User);
        var page = RouteData.Values["page"] as string;
        IReadOnlyList<int>? ids;
        if (page == "/Recipes/ExportAi")
        {
            var lookup = await recipes.GetForReadingAsync(slug ?? string.Empty, CanEdit, cancellationToken);
            if (lookup.RedirectToSlug is { } current)
            {
                return RedirectPermanent("/recipes/" + current + "/export-ai" + Request.QueryString);
            }

            if (lookup.Recipe is not { } recipe)
            {
                return NotFound();
            }

            ids = [recipe.Id];
            IsSingle = true;
            Slug = recipe.Slug;
            Heading = recipe.Title;
            BackLink = "/recipes/" + recipe.Slug;
            FileName = recipe.Slug + "-for-ai.md";
        }
        else
        {
            ids = await exporter.BulkIdsAsync(CanEdit, page == "/Collections/ExportAi" ? slug : null, cancellationToken);
            if (ids is null)
            {
                return NotFound();
            }

            Heading = slug is null ? "All recipes" : "Collection: " + slug;
            BackLink = slug is null ? "/recipes" : "/collections/" + slug;
            FileName = (slug ?? "recipes") + "-for-ai.md";
        }

        var export = await ai.RecipesAsync(ids, cancellationToken);
        RecipeCount = export.Titles.Count;
        if (Value("go") is null)
        {
            return Page();
        }

        var goals = new List<AiGoal>();
        var chosen = new HashSet<AiGoalKind>();
        foreach (var value in Request.Query["g"])
        {
            if (Enum.TryParse<AiGoalKind>(value, ignoreCase: false, out var kind) && Enum.IsDefined(kind) && chosen.Add(kind))
            {
                goals.Add(new AiGoal(kind, Value("t_" + kind), Value("a_" + kind)));
            }
        }

        Chosen = chosen;
        var includeKitchen = Checked("kitchen", false) || chosen.Contains(AiGoalKind.AdaptToTools);
        var request = new AiPromptRequest
        {
            Titles = export.Titles,
            Goals = goals,
            LastTime = Value("last"),
            KeepServings = Checked("keep", false),
            Servings = export.Servings,
            MaxTotalMinutes = int.TryParse(Value("max"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var max) ? max : null,
            MustKeep = Value("must"),
            AskQuestions = Checked("ask", false),
            KeepConflicts = Checked("both", false),
            Kitchen = includeKitchen ? await ai.KitchenAsync(cancellationToken) : null,
            Units = includeKitchen ? DisplayUnitsRules.Label(await UnitsAsync(cancellationToken)) : null,
        };

        Conflicts = AiPromptBuilder.Conflicts(goals);
        var prompt = AiPromptBuilder.Build(request);
        Text = prompt is null ? null : AiPromptBuilder.Compose(prompt, export.Markdown, Checked("recipe", true));
        return Page();
    }

    private async Task<DisplayUnits> UnitsAsync(CancellationToken cancellationToken)
    {
        DisplayUnits? profile = null;
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id)
        {
            profile = (await preferences.GetDisplayAsync(id, cancellationToken)).Units;
        }

        Request.Cookies.TryGetValue(DisplayUnitsRules.CookieName, out var cookie);
        var units = DisplayUnitsRules.Resolve(null, profile, cookie);
        return units == DisplayUnits.AsWritten ? DisplayUnits.US : units;
    }
}
