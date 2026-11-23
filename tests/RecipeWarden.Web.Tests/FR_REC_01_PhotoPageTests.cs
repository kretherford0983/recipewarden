using System.Net;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Web.Pages.Manage.Recipes;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-REC-01, FR-REC-02 (W2 steps 4–5), SEC-50 to SEC-52, ADR 0022/0023: photos on the edit page and /media access.</summary>
[Trait("Req", "FR-REC-01")]
[Trait("Req", "SEC-52")]
public sealed partial class FR_REC_01_PhotoPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("/media/([0-9a-f]{32})")]
    private static partial Regex MediaLink();

    [Fact]
    [Trait("Req", "ADR-0022")]
    public async Task Finish_and_step_photos_show_on_the_recipe_and_drafts_stay_private()
    {
        using var editor = await factory.SignInAsync("photo-1@example.com", AppRoles.Editor);
        var slug = await editor.AddRecipeAsync("Photo Stew", steps: "Brown the beef.\nSimmer.");
        var id = await factory.RecipeIdAsync(slug);

        var finish = await editor.PostFilesAsync($"/manage/recipes/{id}/edit?handler=FinishImage", [("photo", "dish.jpg", WebTestHelpers.SampleJpeg())]);
        finish.StatusCode.ShouldBe(HttpStatusCode.Redirect, await finish.TextAsync());
        finish.Headers.Location!.ToString().ShouldBe($"/manage/recipes/{id}/edit#photos");
        (await editor.PostFilesAsync($"/manage/recipes/{id}/edit?handler=StepPhoto", [("photo", "step.png", PngBytes())], ("step", "1")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.PostFilesAsync($"/manage/recipes/{id}/edit?handler=StepPhoto", [("photo", "step.png", PngBytes())], ("step", "5")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var reader = factory.CreateHttpsClient();
        var html = await reader.GetStringAsync("/recipes/" + slug, Ct);
        html.ShouldContain("class=\"finish-image\"");
        html.ShouldContain("class=\"step-image\"");
        var ids = MediaLink().Matches(html).Select(m => m.Groups[1].Value).Distinct().ToList();
        ids.Count.ShouldBe(2);
        foreach (var image in ids)
        {
            (await reader.GetAsync("/media/" + image, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Unpublish: the photos are no longer open to readers, but editors still see them.
        var edit = await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct);
        var version = Regex.Match(edit, "name=\"Input.Version\" value=\"(\\d+)\"").Groups[1].Value;
        (await editor.PostFormAsync($"/manage/recipes/{id}/edit", ("Input.Title", "Photo Stew"), ("Input.Ingredients", "1 cup flour"),
            ("Input.Steps", "Brown the beef.\nSimmer."), ("Input.SourceType", "Manual"), ("Input.Version", version), ("Action", "draft")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await reader.GetAsync("/media/" + ids[0], Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.GetAsync("/media/" + ids[0], Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var panel = await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct);
        panel.ShouldContain("Finished dish");
        panel.ShouldContain($"/media/{ids[0]}/thumb");
        (await editor.PostFormAsync($"/manage/recipes/{id}/edit?handler=RemoveFinishImage")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct)).ShouldNotContain("alt=\"Finished dish\"");
    }

    [Fact]
    [Trait("Req", "SEC-50")]
    [Trait("Req", "SEC-51")]
    public async Task Bad_photos_are_refused_with_the_reason()
    {
        using var editor = await factory.SignInAsync("photo-2@example.com", AppRoles.Editor);
        var id = await factory.RecipeIdAsync(await editor.AddRecipeAsync("Refused Photos"));
        var url = $"/manage/recipes/{id}/edit?handler=FinishImage";

        async Task<string> Refused(string name, byte[] bytes)
        {
            var response = await editor.PostFilesAsync(url, [("photo", name, bytes)]);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, name);
            return WebUtility.HtmlDecode(await response.TextAsync());
        }

        (await Refused("anim.gif", "GIF89a"u8.ToArray())).ShouldContain(UploadRules.ImageNotAllowed);
        (await Refused("logo.svg", "<svg onload=alert(1)>"u8.ToArray())).ShouldContain(UploadRules.ImageNotAllowed);
        (await Refused("evil.jpg", "MZ\u0090\0\u0003"u8.ToArray())).ShouldContain(UploadRules.Mismatch);
        var big = new byte[11 * 1024 * 1024];
        WebTestHelpers.SampleJpeg().CopyTo(big, 0);
        (await Refused("big.jpg", big)).ShouldContain(UploadRules.ImageTooBig);
        (await Refused("broken.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3])).ShouldContain(ImageEncoder.Unreadable);

        var empty = await editor.PostFormAsync(url);
        empty.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        WebUtility.HtmlDecode(await empty.TextAsync()).ShouldContain(EditModel.ChoosePhoto);
    }

    [Fact]
    [Trait("Req", "FR-SRCH-02")]
    public async Task The_list_filters_by_photo_and_shows_thumbnails()
    {
        using var editor = await factory.SignInAsync("photo-3@example.com", AppRoles.Editor);
        var with = await editor.AddRecipeAsync("Zz Photo Pie");
        await editor.AddRecipeAsync("Zz Plain Pie");
        await editor.PostFilesAsync($"/manage/recipes/{await factory.RecipeIdAsync(with)}/edit?handler=FinishImage", [("photo", "pie.jpg", WebTestHelpers.SampleJpeg())]);

        using var reader = factory.CreateHttpsClient();
        var filtered = await reader.GetStringAsync("/recipes?image=1&q=zz", Ct);
        filtered.ShouldContain("Zz Photo Pie");
        filtered.ShouldNotContain("Zz Plain Pie");
        filtered.ShouldContain("class=\"thumb\"");
        (await reader.GetStringAsync("/recipes?q=zz", Ct)).ShouldContain("Zz Plain Pie");
    }

    [Fact]
    public async Task Unknown_or_malformed_media_ids_are_not_found()
    {
        using var reader = factory.CreateHttpsClient();
        foreach (var path in new[] { "/media/" + new string('a', 32), "/media/..%2F..%2Fetc%2Fpasswd", "/media/ABCDEF0123456789ABCDEF0123456789" })
        {
            (await reader.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }
    }

    private static byte[] PngBytes()
    {
        using var bitmap = new SkiaSharp.SKBitmap(40, 30);
        bitmap.Erase(SkiaSharp.SKColors.Teal);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
