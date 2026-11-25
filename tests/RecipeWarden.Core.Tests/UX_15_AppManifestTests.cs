using RecipeWarden.Core.Presentation;

namespace RecipeWarden.Core.Tests;

/// <summary>UX-15, AC-134: the web app manifest that makes the site installable.</summary>
[Trait("Req", "UX-15")]
public sealed class UX_15_AppManifestTests
{
    [Fact]
    [Trait("Req", "AC-134")]
    public void The_manifest_has_what_browsers_need_to_install_the_app()
    {
        var m = AppManifest.For(null);
        m.Name.ShouldBe("RecipeWarden");
        m.ShortName.ShouldBe("RecipeWarden");
        m.StartUrl.ShouldBe("/");
        m.Display.ShouldBe("standalone");
        m.Icons.Where(i => i.Purpose == "any").Select(i => i.Sizes).ShouldBe(["192x192", "512x512"]);
        m.Icons.Where(i => i.Purpose == "maskable").Select(i => i.Sizes).ShouldBe(["192x192", "512x512"]);
        m.ThemeColor.ShouldBe("#a33f19");
        m.Shortcuts.Select(s => s.Url).ShouldBe(["/search", "/shopping", "/recipes"]);
    }

    [Theory]
    [InlineData("Rutherford Recipes", "Rutherford Recipes", "Rutherford")]
    [InlineData("  Our Kitchen  ", "Our Kitchen", "Our Kitchen")]
    [InlineData("Supercalifragilistic Kitchen", "Supercalifragilistic Kitchen", "Supercalifra")]
    public void The_site_name_names_the_app(string site, string name, string shortName)
    {
        var m = AppManifest.For(site);
        m.Name.ShouldBe(name);
        m.ShortName.ShouldBe(shortName);
    }
}
