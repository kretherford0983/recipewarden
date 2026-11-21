using System.Text;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Search;
using RecipeWarden.Core.Text;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// Unpaired UTF-16 surrogates (possible in form and query-string input) must never throw in Core rules.
/// Found by SEC_21_FtsQueryBuilderTests.Random_input_always_yields_a_safe_expression.
/// </summary>
[Trait("Req", "SEC-21")]
[Trait("Req", "AC-22")]
public sealed class SafeTextTests
{
    public static TheoryData<string> Broken() => new("\ud800", "abc\udc00def", "x\ud83c", "\udf72\ud83c", "a\ud800\ud800b");

    [Theory]
    [MemberData(nameof(Broken))]
    public void Lone_surrogates_become_replacement_characters(string input)
    {
        var fixedText = SafeText.ReplaceLoneSurrogates(input);
        fixedText.ShouldNotBe(input);
        fixedText.Any(char.IsSurrogate).ShouldBeFalse();
        Should.NotThrow(() => fixedText.Normalize(NormalizationForm.FormC));
    }

    [Fact]
    public void Valid_text_is_returned_unchanged()
    {
        const string text = "crème 🍲 料理";
        SafeText.ReplaceLoneSurrogates(text).ShouldBeSameAs(text);
        SafeText.Normalize(text, NormalizationForm.FormC).ShouldBe(text.Normalize(NormalizationForm.FormC));
    }

    [Theory]
    [MemberData(nameof(Broken))]
    public void Search_slugs_and_names_do_not_throw(string input)
    {
        Should.NotThrow(() => FtsQuery.Build("chicken " + input));
        var slug = SlugRules.FromText("Soup " + input);
        SlugRules.IsValid(slug).ShouldBeTrue(slug);
        slug.ShouldStartWith("soup");
        Should.NotThrow(() => NameRules.Normalize(input));
    }

    [Theory]
    [MemberData(nameof(Broken))]
    public void Names_with_lone_surrogates_are_refused(string input)
    {
        NameRules.ValidateTag("tag" + input).ShouldBe(NameRules.BadCharacters);
        NameRules.ValidateCollection("list" + input).ShouldBe(NameRules.BadCharacters);
    }

    [Fact]
    public void Null_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => SafeText.ReplaceLoneSurrogates(null!));
        Should.Throw<ArgumentNullException>(() => SafeText.Normalize(null!, NormalizationForm.FormC));
    }
}
