using RecipeWarden.Web.Layout;

namespace RecipeWarden.Web.Tests;

/// <summary>UX-13, ADR 0047: the Manage and Admin tabs mark the page you are on, including pages under a tab.</summary>
[Trait("Req", "UX-13")]
public sealed class UX_13_SectionNavTests
{
    [Theory]
    [InlineData("/manage", "/manage")]
    [InlineData("/manage/", "/manage")]
    [InlineData("/manage/tags", "/manage/tags")]
    [InlineData("/manage/import/12/review", "/manage/import")]
    [InlineData("/manage/import/ai-response", "/manage/import")]
    [InlineData("/MANAGE/Kitchen", "/manage/kitchen")]
    public void Manage_tabs_mark_the_current_page(string path, string expected) =>
        Current(SectionNav.Manage, path).ShouldBe(expected);

    [Theory]
    [InlineData("/admin", "/admin")]
    [InlineData("/admin/backups", "/admin/backups")]
    [InlineData("/admin/nutrition-data", "/admin/nutrition-data")]
    public void Admin_tabs_mark_the_current_page(string path, string expected) =>
        Current(SectionNav.Admin, path).ShouldBe(expected);

    [Fact]
    public void A_page_outside_every_tab_marks_none()
    {
        Current(SectionNav.Manage, "/manage/recipes/3/edit").ShouldBeNull();
        Current(SectionNav.Admin, "/recipes").ShouldBeNull();
    }

    private static string? Current(IReadOnlyList<SectionLink> links, string path) =>
        links.SingleOrDefault(l => SectionNav.IsCurrent(l, links, path))?.Href;
}
