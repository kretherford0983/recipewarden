using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Identity;

namespace RecipeWarden.Web.Tests;

/// <summary>An RFC 6238 code from a base32 key, as an authenticator app computes it.</summary>
internal static class Totp
{
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var key = Base32(base32Key.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        var counter = (long)((at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30);
        var bytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

#pragma warning disable CA5350 // RFC 6238 uses HMAC-SHA1; test helper only
        var hash = HMACSHA1.HashData(key, bytes);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string s)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}

/// <summary>FR-AUTH-04, SEC-04, AC-73: two-factor sign-in with an authenticator app or a recovery code.</summary>
[Trait("Req", "FR-AUTH-04")]
[Trait("Req", "SEC-04")]
[Trait("Req", "AC-73")]
public sealed partial class SEC_04_TwoFactorTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> KeyOfAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await users.GetAuthenticatorKeyAsync((await users.FindByEmailAsync(email))!))!;
    }

    [Fact]
    public async Task Set_up_then_sign_in_needs_a_valid_code_or_a_recovery_code()
    {
        using var client = await factory.SignInAsync("tfa@example.com", AppRoles.Admin);
        (await client.GetStringAsync("/admin", Ct)).ShouldContain("two-factor sign-in", Case.Insensitive);

        var start = await client.PostFormAsync("/account/2fa?handler=Start", [], tokenPage: "/account/2fa");
        var html = await start.TextAsync();
        start.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("src=\"data:image/svg&#x2B;xml;base64,"); // "+" is HTML-encoded in the attribute; browsers decode it
        html.ShouldContain("alt=\"QR code for your authenticator app");
        var key = await KeyOfAsync("tfa@example.com");
        html.ShouldContain(AccountRules.FormatKey(key));

        (await client.PostFormAsync("/account/2fa?handler=Confirm", [new("code", "000000")], tokenPage: "/account/2fa")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var confirmed = await client.PostFormAsync("/account/2fa?handler=Confirm", [new("code", Totp.Code(key))], tokenPage: "/account/2fa");
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var codes = RecoveryCode().Matches(await confirmed.TextAsync()).Select(m => m.Groups[1].Value).ToList();
        codes.Count.ShouldBe(AccountRules.RecoveryCodeCount);

        // AC-73: the password alone now leads to the second step, and no session is issued.
        using var second = factory.CreateHttpsClient();
        var step1 = await RecipeWardenFactory.PostLoginAsync(second, "tfa@example.com", RecipeWardenFactory.Password);
        step1.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        step1.Headers.Location!.ToString().ShouldStartWith("/account/login-2fa");
        (await second.GetAsync("/manage", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect, "not signed in yet");

        var wrong = await second.PostFormAsync("/account/login-2fa", [new("code", "123456")], tokenPage: "/account/login-2fa");
        wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await wrong.TextAsync()).ShouldContain(AccountRules.InvalidSignInMessage);
        (await second.GetAsync("/manage", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var right = await second.PostFormAsync("/account/login-2fa", [new("code", Totp.Code(key))], tokenPage: "/account/login-2fa");
        right.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await second.GetAsync("/manage", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A recovery code works once.
        foreach (var expectOk in new[] { true, false })
        {
            using var third = factory.CreateHttpsClient();
            await RecipeWardenFactory.PostLoginAsync(third, "tfa@example.com", RecipeWardenFactory.Password);
            var viaRecovery = await third.PostFormAsync("/account/login-2fa", [new("recoveryCode", codes[0])], tokenPage: "/account/login-2fa");
            viaRecovery.StatusCode.ShouldBe(expectOk ? HttpStatusCode.Redirect : HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task The_second_step_without_a_password_first_does_nothing()
    {
        using var client = factory.CreateHttpsClient();
        (await client.GetStringAsync("/account/login-2fa", Ct)).ShouldContain("Sign in with your email and password first");
        var post = await client.PostFormAsync("/account/login-2fa", [new("code", "123456")], tokenPage: "/account/login");
        (await post.TextAsync()).ShouldContain("Sign in with your email and password first");
    }

    [Fact]
    public async Task Turning_it_off_needs_a_current_code()
    {
        await factory.EnsureUserAsync("tfa-off@example.com", AppRoles.Editor);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var u = (await users.FindByEmailAsync("tfa-off@example.com"))!;
            await users.ResetAuthenticatorKeyAsync(u);
            await users.SetTwoFactorEnabledAsync(u, true);
        }

        var key = await KeyOfAsync("tfa-off@example.com");
        using var client = factory.CreateHttpsClient();
        await RecipeWardenFactory.PostLoginAsync(client, "tfa-off@example.com", RecipeWardenFactory.Password);
        await client.PostFormAsync("/account/login-2fa", [new("code", Totp.Code(key))], tokenPage: "/account/login-2fa");

        (await client.PostFormAsync("/account/2fa?handler=Disable", [new("code", "")], tokenPage: "/account/2fa")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostFormAsync("/account/2fa?handler=Disable", [new("code", Totp.Code(key))], tokenPage: "/account/2fa")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var check = factory.Services.CreateAsyncScope();
        var manager = check.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await manager.FindByEmailAsync("tfa-off@example.com"))!.TwoFactorEnabled.ShouldBeFalse();
    }

    [GeneratedRegex("<li><code>([^<]+)</code></li>")]
    private static partial Regex RecoveryCode();
}

/// <summary>SEC-01, SEC-03 and /account/password.</summary>
[Trait("Req", "SEC-01")]
public sealed class SEC_01_PasswordTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("password1234")]
    [InlineData("PASSWORD1234")]
    [InlineData("iloveyou1234")]
    public void Common_passwords_are_on_the_bundled_list(string password) => CommonPasswordValidator.IsCommon(password).ShouldBeTrue();

    [Fact]
    public void The_list_is_loaded_and_test_passwords_are_not_on_it()
    {
        CommonPasswordValidator.Count.ShouldBeGreaterThan(1000);
        CommonPasswordValidator.IsCommon(RecipeWardenFactory.Password).ShouldBeFalse();
    }

    [Fact]
    public async Task Admins_cannot_give_an_account_a_common_password_or_its_own_email()
    {
        using var admin = await factory.SignInAsync("pw-admin@example.com", AppRoles.Admin);
        var common = await admin.PostFormAsync("/admin/users?handler=Create", ("NewUser.Email", "pw-new@example.com"), ("NewUser.Password", "Password1234"),
            ("NewUser.Role", AppRoles.Editor));
        common.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await common.TextAsync()).ShouldContain("too common");

        var email = await admin.PostFormAsync("/admin/users?handler=Create", ("NewUser.Email", "pw-same@example.com"), ("NewUser.Password", "pw-same@example.com"),
            ("NewUser.Role", AppRoles.Editor));
        (await email.TextAsync()).ShouldContain("cannot be your email");
    }

    [Fact]
    public async Task Changing_your_password_needs_the_current_one_and_a_good_new_one()
    {
        using var user = await factory.SignInAsync("pw-change@example.com", AppRoles.Editor);
        (await user.PostFormAsync("/account/password", [new("current", "wrong password!!"), new("password", "a brand new long phrase"), new("confirm", "a brand new long phrase")],
            tokenPage: "/account/password")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await user.PostFormAsync("/account/password", [new("current", RecipeWardenFactory.Password), new("password", "password1234"), new("confirm", "password1234")],
            tokenPage: "/account/password")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await user.PostFormAsync("/account/password", [new("current", RecipeWardenFactory.Password), new("password", "a brand new long phrase"), new("confirm", "a brand new long phrase")],
            tokenPage: "/account/password")).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        using var again = factory.CreateHttpsClient();
        (await RecipeWardenFactory.PostLoginAsync(again, "pw-change@example.com", "a brand new long phrase")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    [Trait("Req", "SEC-03")]
    public async Task Remember_me_gives_a_cookie_for_30_days_and_otherwise_a_session_cookie()
    {
        await factory.EnsureUserAsync("pw-remember@example.com", AppRoles.Editor);
        using var plain = factory.CreateHttpsClient();
        var session = await RecipeWardenFactory.PostLoginAsync(plain, "pw-remember@example.com", RecipeWardenFactory.Password);
        var sessionCookie = session.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-rw-auth=", StringComparison.Ordinal));
        sessionCookie.ShouldNotContain("expires=", Case.Insensitive);

        using var remembered = factory.CreateHttpsClient();
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(remembered, "/account/login");
        var response = await remembered.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "pw-remember@example.com",
            ["Input.Password"] = RecipeWardenFactory.Password,
            ["Input.RememberMe"] = "true",
            ["__RequestVerificationToken"] = token,
        }), Ct);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-rw-auth=", StringComparison.Ordinal));
        var expires = DateTimeOffset.Parse(Regex.Match(cookie, "expires=([^;]+)", RegexOptions.IgnoreCase).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        (expires - DateTimeOffset.UtcNow).ShouldBe(TimeSpan.FromDays(30), TimeSpan.FromMinutes(5));
    }
}

/// <summary>SEC-63: its own host with a low limit, so no other test's sign-ins share the budget.</summary>
[Trait("Req", "SEC-63")]
public sealed class SEC_63_SignInRateLimitTests : IClassFixture<SEC_63_SignInRateLimitTests.Factory>
{
    private readonly RecipeWardenFactory factory;

    public SEC_63_SignInRateLimitTests(Factory f) => factory = f.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            Host.Settings["RateLimits:SignInsPerMinute"] = "4";
        }

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    [Fact]
    [Trait("Req", "SEC-63")]
    public async Task Sign_in_attempts_are_limited_per_IP()
    {
        using var client = factory.CreateHttpsClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(client, "/account/login");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/account/login")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Input.Email"] = "nobody@example.com",
                    ["Input.Password"] = "wrong password!!",
                    ["__RequestVerificationToken"] = token,
                }),
            };
            request.Headers.Add(RecipeWardenFactory.RemoteIpHeader, "127.0.0.9");
            codes.Add((await client.SendAsync(request, Ct)).StatusCode);
        }

        codes.Take(4).ShouldAllBe(c => c == HttpStatusCode.OK);
        codes.Skip(4).ShouldAllBe(c => c == HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/account/login", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "showing the form is not limited");
    }
}
