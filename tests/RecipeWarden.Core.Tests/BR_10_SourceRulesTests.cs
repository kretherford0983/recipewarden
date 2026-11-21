using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-10, BR-11: Web sources need a URL and a name; only an Admin can take a recipe off Web.</summary>
[Trait("Req", "BR-10")]
[Trait("Req", "BR-11")]
public sealed class BR_10_SourceRulesTests
{
    [Fact]
    public void Web_source_needs_url_and_name()
    {
        var problems = SourceRules.Validate(SourceType.Web, url: null, name: " ");

        problems.ShouldBe(
        [
            new SourceProblem(RecipeField.SourceUrl, "A web recipe needs the address it came from"),
            new SourceProblem(RecipeField.SourceName, "A web recipe needs the source's name"),
        ]);
    }

    [Fact]
    public void Complete_web_source_is_valid()
    {
        SourceRules.Validate(SourceType.Web, "https://example.com/soup", "Example Kitchen").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(SourceType.Manual)]
    [InlineData(SourceType.Personal)]
    [InlineData(SourceType.File)]
    public void Other_source_types_need_nothing(SourceType type)
    {
        SourceRules.Validate(type, null, null).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("ftp://example.com/x")]
    [InlineData("//example.com/x")]
    [InlineData("example.com/x")]
    [InlineData("http://")]
    [InlineData("https://user:pass@example.com/")]
    public void Source_url_must_be_an_absolute_http_or_https_address(string url)
    {
        foreach (var type in Enum.GetValues<SourceType>())
        {
            SourceRules.Validate(type, url, "Name")
                .ShouldContain(new SourceProblem(RecipeField.SourceUrl, "The source address must start with http:// or https://"));
        }
    }

    [Fact]
    public void Overlong_source_fields_are_refused()
    {
        var problems = SourceRules.Validate(SourceType.Web, "https://example.com/" + new string('a', 2048), new string('n', 201));
        problems.Select(p => p.Field).ShouldBe([RecipeField.SourceUrl, RecipeField.SourceName]);
    }

    [Theory]
    [InlineData(SourceType.Web, SourceType.Personal, false, false)]
    [InlineData(SourceType.Web, SourceType.Personal, true, true)]
    [InlineData(SourceType.Web, SourceType.Manual, false, false)]   // credit cannot be hidden by another route
    [InlineData(SourceType.Web, SourceType.Manual, true, true)]
    [InlineData(SourceType.Web, SourceType.Web, false, true)]
    [InlineData(SourceType.Manual, SourceType.Web, false, true)]
    [InlineData(SourceType.Manual, SourceType.Personal, false, true)]
    [InlineData(SourceType.File, SourceType.Personal, false, true)]
    public void Only_admin_can_change_a_web_recipe_to_another_source_type(SourceType from, SourceType to, bool isAdmin, bool allowed)
    {
        SourceRules.CanChangeType(from, to, isAdmin).ShouldBe(allowed);
    }

    [Theory]
    [InlineData(SourceType.Web, SourceType.Personal, true)]
    [InlineData(SourceType.Manual, SourceType.Personal, false)]
    [InlineData(SourceType.Web, SourceType.Web, false)]
    public void Leaving_web_is_flagged_for_audit(SourceType from, SourceType to, bool audited)
    {
        SourceRules.ChangeNeedsAudit(from, to).ShouldBe(audited);
    }
}
