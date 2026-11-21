using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-02, ADR 0008: slugs from titles, unique, and published slugs kept as redirects on title change.</summary>
[Trait("Req", "BR-02")]
public sealed class BR_02_SlugRulesTests
{
    private static bool NoneTaken(string _) => false;

    [Theory]
    [InlineData("Chicken Lime Soup", "chicken-lime-soup")]
    [InlineData("  Chicken   Lime -- Soup!  ", "chicken-lime-soup")]
    [InlineData("Crème Brûlée", "creme-brulee")]
    [InlineData("Jalapeño Poppers", "jalapeno-poppers")]
    [InlineData("Straßenküche Smørrebrød", "strassenkuche-smorrebrod")]
    [InlineData("Œufs & Æbleskiver", "oeufs-aebleskiver")]
    [InlineData("Mom's \"Best\" Chili", "mom-s-best-chili")]
    [InlineData("3-Ingredient Cookies", "3-ingredient-cookies")]
    [InlineData("ÇA_VA_BIEN", "ca-va-bien")]
    public void Slug_is_lower_case_ascii_with_single_hyphens(string title, string expected)
    {
        SlugRules.FromText(title).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("日本料理")]
    [InlineData(null)]
    public void Text_without_ascii_letters_or_digits_gets_the_fallback(string? title)
    {
        SlugRules.FromText(title).ShouldBe("recipe");
        SlugRules.FromText(title, fallback: "tag").ShouldBe("tag");
    }

    [Fact]
    public void Long_titles_are_cut_at_a_word_boundary_within_80_characters()
    {
        var title = string.Join(' ', Enumerable.Repeat("delicious", 20));
        var slug = SlugRules.FromText(title);

        slug.Length.ShouldBeLessThanOrEqualTo(SlugRules.MaxLength);
        slug.ShouldNotEndWith("-");
        slug.Split('-').ShouldAllBe(w => w == "delicious");
    }

    [Fact]
    public void A_single_long_word_is_cut_hard()
    {
        SlugRules.FromText(new string('a', 120)).ShouldBe(new string('a', 80));
    }

    [Fact]
    public void Unique_slug_appends_the_next_free_number()
    {
        var taken = new HashSet<string> { "soup", "soup-2" };
        SlugRules.MakeUnique("soup", taken.Contains).ShouldBe("soup-3");
        SlugRules.MakeUnique("stew", taken.Contains).ShouldBe("stew");
    }

    [Fact]
    public void Unique_slug_with_suffix_stays_within_the_maximum()
    {
        var baseSlug = new string('a', 80);
        var slug = SlugRules.MakeUnique(baseSlug, s => s == baseSlug);

        slug.Length.ShouldBeLessThanOrEqualTo(SlugRules.MaxLength);
        slug.ShouldEndWith("-2");
    }

    [Fact]
    public void Unique_slug_gives_up_after_too_many_collisions()
    {
        Should.Throw<InvalidOperationException>(() => SlugRules.MakeUnique("soup", _ => true));
    }

    [Theory]
    [InlineData("soup", true)]
    [InlineData("soup-2", true)]
    [InlineData("3-ingredient-cookies", true)]
    [InlineData("Soup", false)]
    [InlineData("-soup", false)]
    [InlineData("soup-", false)]
    [InlineData("so--up", false)]
    [InlineData("so up", false)]
    [InlineData("../etc", false)]
    [InlineData("", false)]
    public void Valid_slugs_are_recognized(string slug, bool valid)
    {
        SlugRules.IsValid(slug).ShouldBe(valid);
    }

    [Fact]
    public void Too_long_slug_is_not_valid()
    {
        SlugRules.IsValid(new string('a', 81)).ShouldBeFalse();
    }

    // ADR 0008: title changes.

    [Fact]
    public void Draft_title_change_moves_the_slug_and_keeps_no_alias()
    {
        var change = SlugRules.PlanTitleChange("old-name", RecipeStatus.Draft, "New Name", NoneTaken);

        change.NewSlug.ShouldBe("new-name");
        change.AliasToKeep.ShouldBeNull();
    }

    [Fact]
    public void Published_title_change_moves_the_slug_and_keeps_the_old_one_as_alias()
    {
        var change = SlugRules.PlanTitleChange("old-name", RecipeStatus.Published, "New Name", NoneTaken);

        change.NewSlug.ShouldBe("new-name");
        change.AliasToKeep.ShouldBe("old-name");
    }

    [Theory]
    [InlineData("chicken-soup", "Chicken Soup!")]
    [InlineData("chicken-soup-2", "Chicken soup")]
    public void Title_change_that_gives_the_same_slug_changes_nothing(string current, string newTitle)
    {
        var change = SlugRules.PlanTitleChange(current, RecipeStatus.Published, newTitle, _ => true);

        change.NewSlug.ShouldBe(current);
        change.AliasToKeep.ShouldBeNull();
        change.Changed.ShouldBeFalse();
    }

    [Fact]
    public void Published_title_change_to_a_taken_slug_gets_a_number()
    {
        var change = SlugRules.PlanTitleChange("old", RecipeStatus.Published, "Soup", s => s == "soup");

        change.NewSlug.ShouldBe("soup-2");
        change.AliasToKeep.ShouldBe("old");
    }
}
