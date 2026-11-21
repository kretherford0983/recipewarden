using RecipeWarden.Core.Security;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-INST-04, AC-100: account emails are checked before anything else happens.</summary>
[Trait("Req", "FR-INST-04")]
[Trait("Req", "AC-100")]
public sealed class FR_INST_04_EmailRulesTests
{
    [Theory]
    [InlineData("admin@example.com")]
    [InlineData("first.last@example.com")]
    [InlineData("first.last+recipes@mail.example.co.uk")]
    [InlineData("o-neil_99@sub-domain.example.org")]
    [InlineData("A.Person@Example.COM")]
    public void Accepts_ordinary_addresses(string email) =>
        AccountRules.ValidateEmail(email).ShouldBeNull();

    [Theory]
    [InlineData("first.last@#example.com", "after the @")]
    [InlineData("name@exa mple.com", "after the @")]
    [InlineData("name@example", "after the @")]
    [InlineData("name@example.c0m", "after the @")]
    [InlineData("name@-example.com", "after the @")]
    [InlineData("name@example-.com", "after the @")]
    [InlineData("name@example..com", "after the @")]
    [InlineData("name@.example.com", "after the @")]
    [InlineData("name@", "after the @")]
    [InlineData("na#me@example.com", "before the @")]
    [InlineData("na me@example.com", "before the @")]
    [InlineData("x;id@example.com", "before the @")]
    [InlineData(".name@example.com", "before the @")]
    [InlineData("name.@example.com", "before the @")]
    [InlineData("na..me@example.com", "before the @")]
    [InlineData("@example.com", "before the @")]
    [InlineData("name.example.com", "one @")]
    [InlineData("name@@example.com", "one @")]
    [InlineData("a@b@example.com", "one @")]
    [InlineData("", "required")]
    [InlineData("   ", "required")]
    public void Rejects_invalid_addresses_with_the_reason(string email, string reason)
    {
        var error = AccountRules.ValidateEmail(email).ShouldNotBeNull();

        error.ShouldContain(reason);
    }

    [Fact]
    public void Rejects_null() =>
        AccountRules.ValidateEmail(null).ShouldNotBeNull().ShouldContain("required");

    [Fact]
    public void Rejects_surrounding_spaces() =>
        AccountRules.ValidateEmail(" admin@example.com").ShouldNotBeNull();

    [Fact]
    public void Rejects_non_ascii_letters_because_the_email_is_also_the_user_name() =>
        AccountRules.ValidateEmail("josé@example.com").ShouldNotBeNull().ShouldContain("before the @");

    [Fact]
    public void Rejects_addresses_longer_than_254_characters()
    {
        var email = new string('a', 64) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 4)) + ".com";

        email.Length.ShouldBeGreaterThan(254);
        AccountRules.ValidateEmail(email).ShouldNotBeNull().ShouldContain("254");
    }

    [Fact]
    public void Rejects_a_local_part_longer_than_64_characters() =>
        AccountRules.ValidateEmail(new string('a', 65) + "@example.com").ShouldNotBeNull().ShouldContain("64");

    [Fact]
    public void Every_character_of_a_valid_email_is_an_allowed_user_name_character()
    {
        const string email = "first.last+recipes_99-x@mail.example.co.uk";

        AccountRules.ValidateEmail(email).ShouldBeNull();
        email.ShouldAllBe(c => AccountRules.UserNameCharacters.Contains(c));
    }

    [Fact]
    public void The_message_quotes_the_address_so_the_operator_sees_what_was_typed() =>
        AccountRules.ValidateEmail("first.last@#example.com").ShouldNotBeNull().ShouldContain("'first.last@#example.com'");
}
