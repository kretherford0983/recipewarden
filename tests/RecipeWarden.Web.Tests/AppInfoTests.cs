using RecipeWarden.Web.Hosting;

namespace RecipeWarden.Web.Tests;

/// <summary>Section 7.1: the footer shows the app version, which the package smoke test checks (docs/branching.md).</summary>
public sealed class AppInfoTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    [Fact]
    public void Version_drops_build_metadata()
    {
        AppInfo.Version.ShouldNotContain("+");
        AppInfo.Version.ShouldStartWith("0.");
    }

    [Fact]
    public void Falls_back_to_the_assembly_version_without_an_informational_version() =>
        AppInfo.FromAssembly(typeof(Shouldly.Should).Assembly).ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public async Task Footer_shows_the_version()
    {
        using var client = factory.CreateHttpsClient();
        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);
        html.ShouldContain($"RecipeWarden {AppInfo.Version}");
    }
}

[Trait("Req", "FR-INST-02")]
public sealed class NetworkConfigPathTests
{
    [Fact]
    public void Network_json_sits_beside_the_main_config() =>
        RecipeWardenHost.NetworkConfigPath("/etc/recipewarden/appsettings.Production.json").ShouldBe("/etc/recipewarden/network.json");
}
