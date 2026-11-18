using System.Net;

namespace RecipeWarden.Web.Tests;

[Trait("Req", "FR-OPS-01")]
public sealed class FR_OPS_01_HealthTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    [Fact]
    public async Task Health_returns_ok_only_without_sign_in()
    {
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("ok");
    }
}
