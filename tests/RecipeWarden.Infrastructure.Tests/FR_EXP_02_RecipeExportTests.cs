using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-EXP-02/03, AC-25, AC-12, ADR 0031: recipe-md export, zips with images and bulk export.</summary>
[Trait("Req", "FR-EXP-02")]
public sealed class FR_EXP_02_RecipeExportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, byte[]> Entries(MemoryStream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            using var m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        });
    }

    [Fact]
    [Trait("Req", "AC-12")]
    public async Task A_web_recipe_exports_and_reads_back_with_its_source()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var edit = RecipeTestScope.Edit("Tortilla Soup", "For the soup:\n1 1/2 lb chicken breast\n2 cups onion", "Brown.\nSimmer 20 minutes.")
            with { Servings = 6m, SourceType = Core.Recipes.SourceType.Web, SourceName = "Example Kitchen", SourceUrl = "https://example.com/soup", Notes = "# not a heading" };
        var saved = await s.Recipes.CreateAsync(edit, RecipeTestScope.UserId, Ct);

        var md = (await s.Exporter.MarkdownAsync(saved.Id, null, Ct)).ShouldNotBeNull();
        md.ShouldStartWith("---\nformat: recipe-md/1\n");
        md.ShouldContain("[Example Kitchen](https://example.com/soup)");
        md.ShouldNotContain("scale:");
        md.ShouldNotContain("image:");

        var back = RecipeMarkdown.Read(md).ShouldHaveSingleItem();
        back.Title.ShouldBe("Tortilla Soup");
        back.Ingredients.ShouldBe("For the soup:\n1 1/2 lb chicken breast\n2 cups onion");
        back.Steps.ShouldBe("Brown.\nSimmer 20 minutes.");
        back.SourceUrl.ShouldBe("https://example.com/soup");
        back.Notes.ShouldBe("# not a heading");
        (await s.Exporter.MarkdownAsync(saved.Id + 99, null, Ct)).ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "AC-24")]
    public async Task A_scaled_export_writes_the_scaled_lines_servings_and_scale()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var saved = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Rice", "1 cup rice\n2 cups water") with { Servings = 4m },
            RecipeTestScope.UserId, Ct);
        var view = (await s.Recipes.GetForReadingAsync(saved.Slug!, true, Ct)).Recipe!;
        var scaled = await s.Scaler.ScaleAsync(view, 2m, DisplayUnits.AsWritten, Ct);

        var md = (await s.Exporter.MarkdownAsync(saved.Id, scaled, Ct))!;
        md.ShouldContain("\nscale: 2\n");
        md.ShouldContain("servings: \"8\"");
        md.ShouldContain("- 2 cups rice");
        md.ShouldContain("water");
        md.ShouldContain("\nscale: 2\nsource:");
        md.ShouldNotContain("2 cups water");
        var at1 = await s.Scaler.ScaleAsync(view, 1m, DisplayUnits.AsWritten, Ct);
        (await s.Exporter.MarkdownAsync(saved.Id, at1, Ct))!.ShouldNotContain("scale:");
    }

    [Fact]
    [Trait("Req", "AC-25")]
    public async Task A_zip_holds_the_markdown_and_images_linked_relatively()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var saved = await s.Recipes.CreateAsync(RecipeTestScope.Edit("Bread", "3 cups flour", "Mix.\nKnead.\nBake 30 minutes."),
            RecipeTestScope.UserId, Ct);
        var (finish, _) = await s.Images.SaveAsync(TestImages.Jpeg(), null, null, Ct);
        var (step, _) = await s.Images.SaveAsync(TestImages.Png(), null, null, Ct);
        await s.Recipes.SetFinishImageAsync(saved.Id, finish, RecipeTestScope.UserId, Ct);
        await s.Recipes.SetStepImageAsync(saved.Id, 1, step, RecipeTestScope.UserId, Ct);

        using var zip = new MemoryStream();
        (await s.Exporter.WriteZipAsync(zip, saved.Id, null, Ct)).ShouldBeTrue();
        var entries = Entries(zip);
        entries.Keys.ShouldBe(["bread.md", "images/finish.webp", "images/step-02.webp"], ignoreOrder: true);
        var md = Encoding.UTF8.GetString(entries["bread.md"]);
        md.ShouldContain("image: \"images/finish.webp\"\nsource:");
        md.ShouldContain("2. Knead.\n   ![Step 2](images/step-02.webp)\n3. Bake");
        Encoding.ASCII.GetString(entries["images/finish.webp"], 8, 4).ShouldBe("WEBP");
        RecipeMarkdown.Read(md).Single().Steps.ShouldBe("Mix.\nKnead.\nBake 30 minutes.");

        using var none = new MemoryStream();
        (await s.Exporter.WriteZipAsync(none, saved.Id + 99, null, Ct)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "FR-EXP-03")]
    public async Task Bulk_export_gives_readers_published_recipes_and_editors_drafts_and_a_collection_its_own()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var a = await s.AddAsync("Apple Pie");
        var b = await s.AddAsync("Banana Bread", publish: false);
        var c = await s.AddAsync("Cherry Tart");
        await s.Recipes.DeleteAsync(c.Id, RecipeTestScope.UserId, Ct);
        var col = await s.Collections.CreateAsync("Desserts", null, RecipeTestScope.UserId, Ct);
        var colId = await s.Db.Collections.Where(x => x.Slug == col.Slug).Select(x => x.Id).SingleAsync(Ct);
        await s.Collections.AddRecipeAsync(colId, b.Id, Ct);

        (await s.Exporter.BulkIdsAsync(false, null, Ct))!.ShouldBe([a.Id]);
        (await s.Exporter.BulkIdsAsync(true, null, Ct))!.ShouldBe([a.Id, b.Id]);
        (await s.Exporter.BulkIdsAsync(false, col.Slug, Ct))!.ShouldBeEmpty();
        (await s.Exporter.BulkIdsAsync(true, col.Slug, Ct))!.ShouldBe([b.Id]);
        (await s.Exporter.BulkIdsAsync(true, "no-such", Ct)).ShouldBeNull();

        using var zip = new MemoryStream();
        await s.Exporter.WriteBulkZipAsync(zip, [a.Id, b.Id, 9999], Ct);
        Entries(zip).Keys.ShouldBe(["apple-pie/apple-pie.md", "banana-bread/banana-bread.md"], ignoreOrder: true);
    }
}
