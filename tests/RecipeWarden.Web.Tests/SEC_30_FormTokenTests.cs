using System.Text.RegularExpressions;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// SEC-30, AC-75: every post form a page renders carries an antiforgery token, so it works from a real browser.
/// Razor's form tag helper adds none when the form has a literal action attribute; this caught that (found by the
/// W2 browser test; the HTTP tests post their own token and could not see it).
/// </summary>
[Trait("Req", "SEC-30")]
[Trait("Req", "AC-75")]
public sealed partial class SEC_30_FormTokenTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    [GeneratedRegex("""<form[^>]*method="post"[^>]*>(.*?)</form>""", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PostForm();

    [Fact]
    public async Task Every_post_form_on_every_page_has_a_token()
    {
        using var admin = await factory.SignInAsync("forms-admin@example.com", AppRoles.Admin);
        var slug = await admin.AddRecipeAsync("Form Check", tags: "forms");
        var id = await factory.RecipeIdAsync(slug);
        await admin.PostFormAsync("/manage/collections?handler=Create", ("name", "Form List"));
        var other = await admin.AddRecipeAsync("Form Trash");
        await admin.PostFormAsync($"/manage/recipes/{await factory.RecipeIdAsync(other)}/edit?handler=Delete");
        var list = await admin.PostFormAsync("/shopping/new", ("Name", "Form Trip"));
        var listPath = list.Headers.Location!.OriginalString;
        await admin.PostFormAsync("/shopping/new?handler=AddRecipe", ("slug", slug), ("listId", listPath.Split('/')[^1]));

        var pages = new[]
        {
            "/", "/recipes", "/recipes/" + slug, "/search?q=form", "/tags/forms", "/collections", "/collections/form-list",
            "/manage", "/manage/recipes/new", $"/manage/recipes/{id}/edit", "/manage/trash", "/manage/tags",
            "/manage/collections", "/manage/collections?c=form-list", "/manage/conversions",
            "/recipes/" + slug + "?scale=2&units=metric", "/manage/kitchen", $"/manage/recipes/{id}/cooked",
            $"/manage/recipes/{id}/suggestions", "/manage/import",
            "/shopping", "/shopping/new", listPath, "/recipes/" + slug + "/cook",
            "/recipes/" + slug + "/export-ai?go=1&g=Healthier", "/manage/import/ai-response?parent=" + slug,
            $"/manage/recipes/{id}/nutrition", "/manage/nutrition?map=gochujang",
            "/admin", "/admin/users", "/admin/settings", "/admin/backups", "/admin/health", "/admin/nutrition-data",
        };

        var forms = 0;
        foreach (var path in pages)
        {
            var html = await admin.GetStringAsync(path, TestContext.Current.CancellationToken);
            foreach (Match form in PostForm().Matches(html))
            {
                forms++;
                form.Groups[1].Value.ShouldContain("name=\"__RequestVerificationToken\"", customMessage: $"{path}: {form.Value[..Math.Min(120, form.Value.Length)]}");
            }
        }

        forms.ShouldBeGreaterThan(20); // the pages above hold many forms; make sure they were found
    }

    [Fact]
    public async Task Sign_out_form_in_the_header_has_a_token()
    {
        using var editor = await factory.SignInAsync("forms-editor@example.com");
        var html = await editor.GetStringAsync("/", TestContext.Current.CancellationToken);

        var signOut = PostForm().Matches(html).Single(m => m.Value.Contains("/account/logout", StringComparison.Ordinal));
        signOut.Value.ShouldContain("__RequestVerificationToken");
    }
}
