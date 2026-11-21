using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>BR-06: of two editors working from the same version, the second save is refused with a diff.</summary>
[Trait("Req", "BR-06")]
public sealed class BR_06_ConcurrencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Second_save_from_the_same_version_is_a_conflict_with_differences()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Soup", "1 onion", "Cook.");

        // Alice saves first, from version 1.
        await using (var alice = s.NewScope())
        {
            var first = await alice.ServiceProvider.GetRequiredService<RecipeService>()
                .UpdateAsync(created.Id, RecipeTestScope.Edit("Soup v2", "1 onion", "Cook."), created.Version, "alice", false, Ct);
            first.Status.ShouldBe(SaveStatus.Saved);
        }

        // Bob still has version 1 open.
        await using var bob = s.NewScope();
        var second = await bob.ServiceProvider.GetRequiredService<RecipeService>()
            .UpdateAsync(created.Id, RecipeTestScope.Edit("Better soup", "1 onion\n1 carrot", "Cook."), created.Version, "bob", false, Ct);

        second.Status.ShouldBe(SaveStatus.Conflict);
        second.Version.ShouldBe(2);
        second.Differences.ShouldBe(
        [
            new FieldDifference(RecipeField.Title, "Better soup", "Soup v2"),
            new FieldDifference(RecipeField.Ingredients, "1 onion\n1 carrot", "1 onion"),
        ]);

        // Nothing of Bob's was written.
        var edit = (await s.Recipes.GetForEditAsync(created.Id, Ct))!;
        edit.Edit.Title.ShouldBe("Soup v2");
        edit.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Saving_from_the_current_version_succeeds_and_moves_it_on()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Soup");
        var first = await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Soup 2"), 1, "u", false, Ct);
        await using var other = s.NewScope();
        var second = await other.ServiceProvider.GetRequiredService<RecipeService>()
            .UpdateAsync(created.Id, RecipeTestScope.Edit("Soup 3"), first.Version, "u", false, Ct);

        second.Status.ShouldBe(SaveStatus.Saved);
        second.Version.ShouldBe(3);
    }

    [Fact]
    public async Task A_concurrent_write_between_read_and_save_is_caught_by_the_row_version()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var created = await s.AddAsync("Soup");

        // Load in this scope so the context holds version 1, then change the row underneath it.
        _ = await s.Recipes.GetForEditAsync(created.Id, Ct);
        await using (var other = s.NewScope())
        {
            await other.ServiceProvider.GetRequiredService<RecipeService>()
                .UpdateAsync(created.Id, RecipeTestScope.Edit("Theirs"), 1, "u", false, Ct);
        }

        var outcome = await s.Recipes.UpdateAsync(created.Id, RecipeTestScope.Edit("Mine"), 1, "u", false, Ct);
        outcome.Status.ShouldBe(SaveStatus.Conflict);
        outcome.Differences.ShouldContain(d => d.Field == RecipeField.Title && d.Theirs == "Theirs");
    }
}
