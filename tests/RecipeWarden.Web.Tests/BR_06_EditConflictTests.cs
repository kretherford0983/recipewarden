using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>BR-06: the second of two concurrent saves gets "changed by someone else" and a diff; nothing is overwritten.</summary>
[Trait("Req", "BR-06")]
public sealed class BR_06_EditConflictTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    [Fact]
    public async Task Stale_save_shows_the_diff_and_a_deliberate_second_save_wins()
    {
        using var alice = await factory.SignInAsync("alice@example.com");
        using var bob = await factory.SignInAsync("bob@example.com");
        var slug = await alice.AddRecipeAsync("Shared Soup", "1 onion", "Cook.");
        var id = await factory.RecipeIdAsync(slug);

        // Both opened version 1. Alice saves first.
        (await alice.PostFormAsync($"/manage/recipes/{id}/edit", ("Input.Title", "Shared Soup"), ("Input.Ingredients", "1 onion\n1 leek"),
            ("Input.Steps", "Cook."), ("Input.SourceType", "Manual"), ("Input.Version", "1"), ("Action", "publish")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var stale = await bob.PostFormAsync($"/manage/recipes/{id}/edit", ("Input.Title", "Bob's Soup"), ("Input.Ingredients", "1 onion"),
            ("Input.Steps", "Cook."), ("Input.SourceType", "Manual"), ("Input.Version", "1"), ("Action", "publish"));

        stale.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await stale.TextAsync();
        html.ShouldContain("changed by someone else");
        html.ShouldContain("<caption>Your version and the saved version</caption>");
        html.ShouldContain("Bob&#x27;s Soup");
        html.ShouldContain("1 leek");
        html.ShouldContain("name=\"Input.Version\" value=\"2\"");

        using var reader = factory.CreateHttpsClient();
        (await reader.GetStringAsync("/recipes/" + slug, TestContext.Current.CancellationToken)).ShouldContain("1 leek");

        // Bob saves again from the page he was shown: that is a deliberate overwrite.
        (await bob.PostFormAsync($"/manage/recipes/{id}/edit", ("Input.Title", "Bob's Soup"), ("Input.Ingredients", "1 onion"),
            ("Input.Steps", "Cook."), ("Input.SourceType", "Manual"), ("Input.Version", "2"), ("Action", "publish")))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }
}
