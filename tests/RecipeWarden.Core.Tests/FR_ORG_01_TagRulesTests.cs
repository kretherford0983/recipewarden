using RecipeWarden.Core.Organization;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-ORG-01, AC-20: free-form tags, de-duplicated case-insensitively, optionally typed.</summary>
[Trait("Req", "FR-ORG-01")]
[Trait("Req", "AC-20")]
public sealed class FR_ORG_01_TagRulesTests
{
    [Theory]
    [InlineData("Dinner", "dinner")]
    [InlineData("dinner", "dinner")]
    [InlineData("  DINNER  ", "dinner")]
    [InlineData("Weeknight   dinner", "weeknight dinner")]
    [InlineData("Weeknight\tdinner", "weeknight dinner")]
    [InlineData("CAFÉ", "café")]
    [InlineData("Café", "café")]   // combining accent normalizes to the same tag as the precomposed form
    public void Names_normalize_for_de_duplication(string name, string normalized)
    {
        NameRules.Normalize(name).ShouldBe(normalized);
    }

    [Fact]
    [Trait("Req", "AC-20")]
    public void Dinner_and_dinner_are_the_same_tag()
    {
        NameRules.Normalize("Dinner").ShouldBe(NameRules.Normalize("dinner"));
    }

    [Fact]
    public void Display_name_is_trimmed_and_collapsed_but_keeps_case()
    {
        NameRules.Clean("  Weeknight   Dinner ").ShouldBe("Weeknight Dinner");
    }

    [Theory]
    [InlineData("", "Enter a name")]
    [InlineData("   ", "Enter a name")]
    [InlineData(null, "Enter a name")]
    [InlineData("bad\u0000name", "The name contains characters that are not allowed")]
    [InlineData("bad‮name", "The name contains characters that are not allowed")]
    public void Invalid_tag_names_are_refused(string? name, string message)
    {
        NameRules.ValidateTag(name).ShouldBe(message);
    }

    [Fact]
    public void Tag_names_are_limited_to_50_characters()
    {
        NameRules.ValidateTag(new string('a', 50)).ShouldBeNull();
        NameRules.ValidateTag(new string('a', 51)).ShouldBe("Tag names are limited to 50 characters");
    }

    [Fact]
    public void Collection_names_are_limited_to_100_characters()
    {
        NameRules.ValidateCollection(new string('a', 100)).ShouldBeNull();
        NameRules.ValidateCollection(new string('a', 101)).ShouldBe("Collection names are limited to 100 characters");
        NameRules.ValidateCollection(" ").ShouldBe("Enter a name");
    }

    [Fact]
    public void Tag_types_are_the_six_from_the_spec()
    {
        Enum.GetNames<TagType>().ShouldBe(["Meal", "Course", "Season", "Method", "Cuisine", "Diet"]);
    }

    [Theory]
    [InlineData("Cuisine", TagType.Cuisine)]
    [InlineData("cuisine", TagType.Cuisine)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Allergen", null)]
    [InlineData("1", null)]   // numbers are not accepted for enum names
    public void Tag_type_parses_names_only(string? text, TagType? expected)
    {
        NameRules.ParseTagType(text).ShouldBe(expected);
    }
}
