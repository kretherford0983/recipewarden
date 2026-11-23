using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>ADR 0023, FR-REC-01: step photos follow their step when the steps are edited.</summary>
[Trait("Req", "ADR-0023")]
[Trait("Req", "FR-REC-01")]
public sealed class ADR_0023_StepPhotoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Photos_follow_reordered_and_reworded_steps_and_do_not_change_the_version()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var saved = await t.AddAsync("Stew", steps: "Brown the beef.\nAdd the onions.\nSimmer.");
        var (a, _) = await t.Images.SaveAsync(TestImages.Jpeg(), null, null, Ct);
        var (b, _) = await t.Images.SaveAsync(TestImages.Jpeg(), null, null, Ct);
        (await t.Recipes.SetStepImageAsync(saved.Id, 0, a, RecipeTestScope.UserId, Ct)).ShouldBeTrue();
        (await t.Recipes.SetStepImageAsync(saved.Id, 2, b, RecipeTestScope.UserId, Ct)).ShouldBeTrue();
        (await t.Recipes.SetStepImageAsync(saved.Id, 9, b, RecipeTestScope.UserId, Ct)).ShouldBeFalse();
        (await t.Recipes.SetFinishImageAsync(saved.Id, a, RecipeTestScope.UserId, Ct)).ShouldBeTrue();

        var before = (await t.Recipes.GetForEditAsync(saved.Id, Ct))!;
        before.Version.ShouldBe(saved.Version);
        before.FinishImageId.ShouldBe(a);
        before.StepPhotos.Select(p => p.ImageId).ShouldBe([a, null, b]);

        // "Simmer." moves to the top (text match); "Brown the beef." is reworded and stays first in position terms.
        var edit = before.Edit with { StepsText = "Simmer.\nBrown the beef well.\nAdd the onions." };
        (await t.Recipes.UpdateAsync(saved.Id, edit, before.Version, RecipeTestScope.UserId, isAdmin: false, Ct)).Status.ShouldBe(SaveStatus.Saved);

        var after = (await t.Recipes.GetForEditAsync(saved.Id, Ct))!;
        after.StepPhotos.Select(p => (p.Text, p.ImageId)).ShouldBe(
        [
            ("Simmer.", b),
            ("Brown the beef well.", null),
            ("Add the onions.", null),
        ]);
        after.FinishImageId.ShouldBe(a);
    }

    [Fact]
    public async Task A_removed_step_releases_its_photo_and_reading_shows_photos()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var saved = await t.AddAsync("Toast", steps: "Toast.\nButter.");
        var (a, _) = await t.Images.SaveAsync(TestImages.Jpeg(), null, null, Ct);
        await t.Recipes.SetStepImageAsync(saved.Id, 1, a, RecipeTestScope.UserId, Ct);

        var view = (await t.Recipes.GetForReadingAsync(saved.Slug!, false, Ct)).Recipe!;
        view.Steps.Single().Steps.Select(s => s.ImageId).ShouldBe([null, a]);

        var before = (await t.Recipes.GetForEditAsync(saved.Id, Ct))!;
        await t.Recipes.UpdateAsync(saved.Id, before.Edit with { StepsText = "Toast." }, before.Version, RecipeTestScope.UserId, false, Ct);
        (await t.Recipes.GetForEditAsync(saved.Id, Ct))!.StepPhotos.ShouldHaveSingleItem().ImageId.ShouldBeNull();
    }
}
