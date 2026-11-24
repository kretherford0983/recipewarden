using System.Net;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-OPS-06, AC-103, D5: the worker's state on the health page, the self-test button and the Admin switch.</summary>
[Trait("Req", "FR-OPS-06")]
public sealed class FR_OPS_06_PythonHealthPageTests : IClassFixture<FR_OPS_06_PythonHealthPageTests.Factory>
{
    private readonly RecipeWardenFactory factory;

    public FR_OPS_06_PythonHealthPageTests(Factory f) => factory = f.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            PythonWebTestKit.UseFakeWorker(Host, """
                if command == "selftest":
                    answer({"passed": True, "checks": {"network_blocked": True, "parse_ms": 12, "parse": True, "fdc": True, "scrape_ms": 3, "scrape": True}})
                else:
                    answer({"suggestions": [{"name": n, "fdc": []} for n in req["names"]]})
                """);
        }

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    [Fact]
    [Trait("Req", "AC-103")]
    public async Task An_admin_sees_the_state_runs_the_self_test_and_turns_python_off_and_on()
    {
        using var admin = await factory.SignInAsync("ops06-admin@example.com", AppRoles.Admin);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/health", Ct));
        page.ShouldContain("<h2 id=\"python\">Python worker</h2>");
        page.ShouldContain("Not checked yet");

        var ran = await admin.PostFormAsync("/admin/health?handler=PythonSelftest", [], tokenPage: "/admin/health");
        ran.Headers.Location!.OriginalString.ShouldStartWith("/admin/health?done=Python%20worker%20self-test%20passed.");
        page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/health", Ct));
        page.ShouldContain("<span class=\"badge ok\">OK</span>");
        page.ShouldContain("ingredient-parser-nlp 2.10.0, recipe-scrapers 15.14.0");
        page.ShouldContain("parse 12 ms, scrape 3 ms");

        await admin.PostFormAsync("/admin/health?handler=PythonSwitch", [new("enabled", "false")], tokenPage: "/admin/health");
        WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/health", Ct)).ShouldContain("<span class=\"badge\">Turned off</span>");
        var nutrition = await admin.GetStringAsync("/manage/nutrition", Ct);
        nutrition.ShouldNotContain("handler=SuggestMatches"); // AC-103: the report works as in v1

        await admin.PostFormAsync("/admin/health?handler=PythonSwitch", [new("enabled", "true")], tokenPage: "/admin/health");
        WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/health", Ct)).ShouldContain("<span class=\"badge ok\">OK</span>");
    }

    [Fact]
    [Trait("Req", "SEC-10")]
    public async Task Only_admins_can_switch_python_or_run_its_self_test()
    {
        using var editor = await factory.SignInAsync("ops06-editor@example.com");
        (await editor.PostFormAsync("/admin/health?handler=PythonSwitch", [new("enabled", "false")])).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Redirect);
        (await editor.PostFormAsync("/admin/health?handler=PythonSelftest", [])).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Redirect);
        using var admin = await factory.SignInAsync("ops06-admin2@example.com", AppRoles.Admin);
        WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/health", Ct)).ShouldNotContain("<span class=\"badge\">Turned off</span>");
    }
}
