using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Organization;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-ORG-01, AC-20: one tag per normalized name, enforced by the database too.</summary>
[Trait("Req", "FR-ORG-01")]
[Trait("Req", "AC-20")]
public sealed class FR_ORG_01_TagStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dinner_and_dinner_make_one_tag()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Soup", tags: ["dinner"]);
        await s.AddAsync("Stew", tags: ["Dinner", " DINNER "]);

        // "Dinner" is a built-in tag (ADR 0011): both recipes use it, and no second tag is made.
        var tag = (await s.Tags.ListAsync(cancellationToken: Ct)).Single(t => t.Slug == "dinner");
        tag.Name.ShouldBe("Dinner");
        (await s.Tags.ListAsync(cancellationToken: Ct)).Count(t => t.Name.Equals("dinner", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        tag.Slug.ShouldBe("dinner");
        tag.RecipeCount.ShouldBe(2);
    }

    [Fact]
    public async Task Database_refuses_a_second_normalized_name()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Tags.CreateAsync("Supper", null, Ct);
        s.Db.Tags.Add(new Tag { Name = "SUPPER", NormalizedName = "supper", Slug = "supper-2" });
        await Should.ThrowAsync<DbUpdateException>(() => s.Db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Create_rename_retype_and_delete()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.Tags.CreateAsync("Mexican", TagType.Cuisine, Ct);
        (await s.Tags.CreateAsync("mexican", null, Ct)).Errors.Single().Message.ShouldBe(TagService.Duplicate);
        (await s.Tags.CreateAsync("", null, Ct)).Status.ShouldBe(SaveStatus.Invalid);

        var renamed = await s.Tags.UpdateAsync(created.Id, "Tex-Mex", TagType.Cuisine, Ct);
        renamed.Slug.ShouldBe("tex-mex");
        (await s.Tags.GetBySlugAsync("tex-mex", Ct))!.TagType.ShouldBe(TagType.Cuisine);

        (await s.Tags.DeleteAsync(created.Id, Ct)).ShouldBeTrue();
        (await s.Tags.ListAsync(cancellationToken: Ct)).ShouldNotContain(t => t.Id == created.Id);
        (await s.Tags.ListAsync(cancellationToken: Ct)).Count.ShouldBe(RecipeWarden.Core.Organization.DefaultTags.All.Count);
        (await s.Tags.UpdateAsync(created.Id, "x", null, Ct)).Status.ShouldBe(SaveStatus.NotFound);
    }

    [Fact]
    public async Task Rename_to_another_tags_name_is_refused()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Tags.CreateAsync("Supper", null, Ct);
        var nibbles = await s.Tags.CreateAsync("Nibbles", null, Ct);
        (await s.Tags.UpdateAsync(nibbles.Id, "SUPPER", null, Ct)).Errors.Single().Message.ShouldBe(TagService.Duplicate);
        (await s.Tags.UpdateAsync(nibbles.Id, "dinner", null, Ct)).Errors.Single().Message.ShouldBe(TagService.Duplicate); // a built-in
        (await s.Tags.CreateAsync("DINNER", null, Ct)).Errors.Single().Message.ShouldBe(TagService.Duplicate);
        (await s.Tags.UpdateAsync(nibbles.Id, "bad\u0000", null, Ct)).Status.ShouldBe(SaveStatus.Invalid);
    }

    [Fact]
    public async Task Renaming_a_tag_updates_search_and_deleting_removes_it_from_recipes()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.AddAsync("Soup", "1 onion", "Cook.", true, "Weeknight");
        var tag = (await s.Tags.ListAsync(cancellationToken: Ct)).Single(t => t.Name == "Weeknight");

        await s.Tags.UpdateAsync(tag.Id, "Quick", null, Ct);
        (await s.SearchAsync("quick")).Total.ShouldBe(1);
        (await s.SearchAsync("weeknight")).Total.ShouldBe(0);

        await s.Tags.DeleteAsync(tag.Id, Ct);
        (await s.SearchAsync("quick")).Total.ShouldBe(0);
        (await s.Recipes.GetForReadingAsync("soup", false, Ct)).Recipe!.Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task Typed_tags_are_listed_first_by_type()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        await s.Tags.CreateAsync("zesty", null, Ct);
        await s.Tags.CreateAsync("Thai", TagType.Cuisine, Ct);

        // Grouped by type in enum order (Meal, Course, Season, Method, Cuisine, Diet), untyped last.
        var names = (await s.Tags.ListAsync(cancellationToken: Ct)).Select(t => t.Name).ToList();
        names.IndexOf("Dinner").ShouldBeLessThan(names.IndexOf("Soup"));        // Meal before Course
        names.IndexOf("No Cook").ShouldBeLessThan(names.IndexOf("Thai"));       // Method before Cuisine
        names.IndexOf("Thai").ShouldBeLessThan(names.IndexOf("Vegan"));         // Cuisine before Diet
        names[^1].ShouldBe("zesty");
        (await s.Tags.GetBySlugAsync("Not A Slug", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Tags_with_non_ascii_names_get_a_fallback_slug()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Tags.CreateAsync("和食", null, Ct)).Slug.ShouldBe("tag");
        (await s.Tags.CreateAsync("中華", null, Ct)).Slug.ShouldBe("tag-2");
    }
}
