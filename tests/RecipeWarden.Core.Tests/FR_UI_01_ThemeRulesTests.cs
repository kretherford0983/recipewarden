using RecipeWarden.Core.Preferences;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "FR-UI-01")]
[Trait("Req", "UX-03")]
public sealed class FR_UI_01_ThemeRulesTests
{
    [Theory]
    [InlineData("light", Theme.Light)]
    [InlineData("DARK", Theme.Dark)]
    [InlineData(" system ", Theme.System)]
    [InlineData(null, Theme.System)]
    [InlineData("", Theme.System)]
    [InlineData("purple", Theme.System)]
    [InlineData("<script>", Theme.System)]
    public void Parse_falls_back_to_system_for_unknown_values(string? input, Theme expected) =>
        ThemeRules.Parse(input).ShouldBe(expected);

    [Theory]
    [InlineData("light", true, Theme.Light)]
    [InlineData("Dark", true, Theme.Dark)]
    [InlineData("system", true, Theme.System)]
    [InlineData("purple", false, Theme.System)]
    [InlineData(null, false, Theme.System)]
    public void TryParseStrict_rejects_unknown_values(string? input, bool ok, Theme expected)
    {
        ThemeRules.TryParseStrict(input, out var theme).ShouldBe(ok);
        theme.ShouldBe(expected);
    }

    [Theory]
    [InlineData(Theme.System, "system")]
    [InlineData(Theme.Light, "light")]
    [InlineData(Theme.Dark, "dark")]
    public void ToValue_round_trips(Theme theme, string value)
    {
        ThemeRules.ToValue(theme).ShouldBe(value);
        ThemeRules.Parse(value).ShouldBe(theme);
    }

    [Fact]
    public void Profile_theme_wins_over_cookie() =>
        ThemeRules.Resolve(Theme.Light, "dark").ShouldBe(Theme.Light);

    [Fact]
    public void Cookie_is_used_without_a_profile() =>
        ThemeRules.Resolve(null, "dark").ShouldBe(Theme.Dark);

    [Fact]
    public void Defaults_to_system_with_neither() =>
        ThemeRules.Resolve(null, null).ShouldBe(Theme.System);
}
