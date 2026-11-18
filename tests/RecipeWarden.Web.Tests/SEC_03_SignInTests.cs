using System.Net;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

[Trait("Req", "SEC-03")]
[Trait("Req", "SEC-02")]
public sealed class SEC_03_SignInTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    [Fact]
    public async Task Auth_cookie_is_host_prefixed_secure_httponly_lax()
    {
        await factory.EnsureUserAsync("cookie@example.com", AppRoles.Editor);
        using var client = factory.CreateHttpsClient();
        var response = await RecipeWardenFactory.PostLoginAsync(client, "cookie@example.com", RecipeWardenFactory.Password);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-rw-auth=", StringComparison.Ordinal)).ToLowerInvariant();
        cookie.ShouldContain("secure");
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("samesite=lax");
        cookie.ShouldContain("path=/");
        cookie.ShouldNotContain("domain=");
    }

    [Fact]
    [Trait("Req", "AC-75")]
    [Trait("Req", "SEC-30")]
    public async Task Login_post_without_antiforgery_token_is_rejected()
    {
        using var client = factory.CreateHttpsClient();
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "x@example.com",
            ["Input.Password"] = "whatever-long-password",
        }), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_get_the_same_message()
    {
        await factory.EnsureUserAsync("known@example.com", AppRoles.Editor);
        using var client = factory.CreateHttpsClient();

        var unknown = await (await RecipeWardenFactory.PostLoginAsync(client, "nobody@example.com", "wrong password here"))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var wrong = await (await RecipeWardenFactory.PostLoginAsync(client, "known@example.com", "wrong password here"))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        unknown.ShouldContain(AccountRules.InvalidSignInMessage);
        wrong.ShouldContain(AccountRules.InvalidSignInMessage);
    }

    [Fact]
    [Trait("Req", "AC-77")]
    public async Task Account_locks_after_five_failures_without_revealing_it()
    {
        await factory.EnsureUserAsync("lockme@example.com", AppRoles.Editor);
        using var client = factory.CreateHttpsClient();

        for (var i = 0; i < 6; i++)
        {
            await RecipeWardenFactory.PostLoginAsync(client, "lockme@example.com", "wrong password here");
        }

        var response = await RecipeWardenFactory.PostLoginAsync(client, "lockme@example.com", RecipeWardenFactory.Password);
        response.StatusCode.ShouldBe(HttpStatusCode.OK); // stays on the form: locked out
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldContain(AccountRules.InvalidSignInMessage);
        html.ShouldNotContain("locked", Case.Insensitive);
    }

    [Fact]
    public async Task Open_redirects_are_refused_after_sign_in()
    {
        await factory.EnsureUserAsync("redirect@example.com", AppRoles.Editor);
        using var client = factory.CreateHttpsClient();
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, "/account/login");
        var response = await client.PostAsync("/account/login?ReturnUrl=https%3A%2F%2Fevil.example", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "redirect@example.com",
            ["Input.Password"] = RecipeWardenFactory.Password,
            ["__RequestVerificationToken"] = token,
        }), TestContext.Current.CancellationToken);

        response.Headers.Location!.ToString().ShouldBe("/");
    }

    [Fact]
    public async Task Sign_out_is_post_only_and_ends_the_session()
    {
        using var client = await factory.SignInAsync("signout@example.com");
        (await client.GetAsync("/account/logout", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, "/");
        var response = await client.PostAsync("/account/logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await client.GetAsync("/manage", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }
}
