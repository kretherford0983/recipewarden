using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Setup;

namespace RecipeWarden.Web.Tests;

/// <summary>SEC-15, AC-84: /setup exists only with the installer's token and before the first Admin; then 404 for good.</summary>
[Trait("Req", "SEC-15")]
[Trait("Req", "AC-84")]
[Trait("Req", "FR-BKP-06")]
public sealed class SEC_15_SetupPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Setup_needs_the_token_offers_both_choices_and_is_gone_after_the_first_admin()
    {
        using var client = factory.CreateHttpsClient();
        (await client.GetAsync("/setup", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound, "no token issued yet");

        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            token = (await scope.ServiceProvider.GetRequiredService<SetupService>().IssueTokenAsync()).ShouldNotBeNull();
        }

        var page = await client.GetAsync("/setup", Ct);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await page.TextAsync();
        html.ShouldContain("Start new");
        html.ShouldContain("Restore from backup");
        html.ShouldNotContain(token);

        var noToken = await client.PostFormAsync("/setup?handler=Create", [new("email", "first@example.com"), new("password", RecipeWardenFactory.Password)], tokenPage: "/setup");
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var wrong = await client.PostFormAsync("/setup?handler=Create", [new("token", token + "x"), new("email", "first@example.com"), new("password", RecipeWardenFactory.Password)],
            tokenPage: "/setup");
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var restoreNoToken = await client.PostFilesAsyncOn("/setup?handler=Restore", "/setup", [("archive", "b.tar.gz", new byte[] { 0x1F, 0x8B })]);
        restoreNoToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var created = await client.PostFormAsync("/setup?handler=Create", [new("token", token), new("email", "first@example.com"), new("password", RecipeWardenFactory.Password)],
            tokenPage: "/setup");
        created.StatusCode.ShouldBe(HttpStatusCode.Redirect, await created.TextAsync());
        created.Headers.Location!.ToString().ShouldBe("/account/login?setup=done");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            (await users.IsInRoleAsync((await users.FindByEmailAsync("first@example.com"))!, AppRoles.Admin)).ShouldBeTrue();
            (await scope.ServiceProvider.GetRequiredService<SetupService>().IssueTokenAsync()).ShouldBeNull();
        }

        (await client.GetAsync("/setup", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var fresh = factory.CreateHttpsClient();
        var again = await fresh.GetAsync("/setup", Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound, "permanently");
        (await RecipeWardenFactory.PostLoginAsync(fresh, "first@example.com", RecipeWardenFactory.Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }
}

internal static class SetupMultipart
{
    /// <summary>A multipart post with the antiforgery token taken from <paramref name="tokenPage"/> (anonymous pages).</summary>
    public static async Task<HttpResponseMessage> PostFilesAsyncOn(this HttpClient client, string url, string tokenPage, IEnumerable<(string Field, string FileName, byte[] Bytes)> files)
    {
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, tokenPage);
        using var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (field, name, bytes) in files)
        {
            content.Add(new ByteArrayContent(bytes), field, name);
        }

        return await client.PostAsync(url, content, TestContext.Current.CancellationToken);
    }
}
