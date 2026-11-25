using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-IMP-09/10, FR-NUT-06, AC-17, AC-18, SEC-45: /admin/ai, review marks and re-parse, mapping suggestions.</summary>
[Trait("Req", "FR-IMP-09")]
[Trait("Req", "FR-IMP-10")]
public sealed class FR_IMP_09_AiPageTests : IClassFixture<FR_IMP_09_AiPageTests.Factory>
{
    private const string ClaudeUrl = "http://api.anthropic.test/v1/messages";
    private const string Secret = "fake-test-key-two";
    private readonly RecipeWardenFactory factory;

    public FR_IMP_09_AiPageTests(Factory f) => factory = f.Host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            Host.Settings["Ai:ClaudeEndpoint"] = ClaudeUrl;
        }

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    private void Replies(string text) => factory.Web.Serve(ClaudeUrl, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
    {
        content = new[] { new { type = "text", text } },
        usage = new { input_tokens = 100, output_tokens = 50 },
    }));

    private static (string, string)[] Settings(bool enabled = true, string key = "") =>
        [("enabled", enabled ? "true" : "false"), ("provider", "Claude"), ("model", "claude-haiku-5-5"), ("apiKey", key), ("cap", "")];

    [Fact]
    [Trait("Req", "AC-18")]
    [Trait("Req", "SEC-45")]
    public async Task The_key_is_write_only_and_never_shown_or_audited()
    {
        using var admin = await factory.SignInAsync("ai-admin@example.com", AppRoles.Admin);
        (await admin.GetStringAsync("/admin/ai", Ct)).ShouldContain("(not set)");
        (await admin.PostFormAsync("/admin/ai", Settings(key: Secret))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var page = await admin.GetStringAsync("/admin/ai", Ct);
        page.ShouldContain("(set — leave empty to keep it)");
        page.ShouldNotContain(Secret);
        (await admin.GetStringAsync("/admin/audit", Ct)).ShouldNotContain(Secret);
        (await admin.PostFormAsync("/admin/ai", Settings())).StatusCode.ShouldBe(HttpStatusCode.Redirect, "an empty key keeps the saved one");
        (await admin.GetStringAsync("/admin/ai", Ct)).ShouldContain("(set");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "AiSettings.Update", Ct)).ShouldBeTrue();
        (await db.AuditEntries.AsNoTracking().AnyAsync(a => a.DetailsJson != null && a.DetailsJson.Contains(Secret), Ct)).ShouldBeFalse();
        (await db.AppSettings.AsNoTracking().AnyAsync(s => s.Value.Contains(Secret), Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Bad_settings_are_refused_and_test_connection_reports_the_result()
    {
        using var admin = await factory.SignInAsync("ai-admin2@example.com", AppRoles.Admin);
        var lan = await admin.PostFormAsync("/admin/ai", ("enabled", "true"), ("provider", "OpenAiCompatible"), ("endpoint", "http://192.168.1.50:11434"),
            ("model", "llama3.1"), ("cap", ""));
        lan.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await lan.TextAsync()).ShouldContain("home network");

        (await admin.PostFormAsync("/admin/ai", Settings(key: Secret))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Replies("ok");
        var ok = await admin.PostFormAsync("/admin/ai?handler=Test");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.TextAsync());
        (await ok.TextAsync()).ShouldContain("Connection works.");
        (await admin.GetStringAsync("/admin", Ct)).ShouldContain("tokens this month");
    }

    [Fact]
    [Trait("Req", "AC-17")]
    public async Task An_AI_parsed_import_is_marked_on_review_and_can_be_parsed_again()
    {
        using var admin = await factory.SignInAsync("ai-admin3@example.com", AppRoles.Admin);
        (await admin.PostFormAsync("/admin/ai", Settings(key: Secret))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        Replies("---\nformat: recipe-md/1\ntitle: \"AI Stew\"\n---\n\n## Ingredients\n\n- 2 lb beef\n\n## Steps\n\n1. Simmer.\n");

        var start = await admin.PostFormAsync("/manage/import?handler=Paste", ("PasteText", "Grandma's stew: two pounds of beef, simmered a long time. Lovely in winter."));
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect, await start.TextAsync());
        var review = start.Headers.Location!.ToString();
        var html = await admin.GetStringAsync(review, Ct);
        html.ShouldContain("filled by the AI parser");
        html.ShouldContain("Ingredients");
        html.ShouldContain("Re-parse with AI");
        html.ShouldContain("AI Stew");

        var reparse = await admin.PostFormAsync(review + "?handler=Reparse");
        reparse.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    [Trait("Req", "FR-NUT-06")]
    public async Task Nutrition_mapping_offers_an_AI_suggestion_that_the_editor_confirms()
    {
        using var admin = await factory.SignInAsync("ai-admin4@example.com", AppRoles.Admin);
        (await admin.PostFormAsync("/admin/ai", Settings(key: Secret))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await admin.GetStringAsync("/manage/nutrition?map=gochujang", Ct)).ShouldContain("Suggest a match with AI");

        Replies("1");
        var suggested = await admin.PostFormAsync("/manage/nutrition?handler=Suggest", ("name", "chili paste"));
        suggested.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await suggested.TextAsync();
        html.ShouldContain("Suggested by AI", customMessage: System.Text.RegularExpressions.Regex.Match(html, "notice[^>]*>[^<]*").Value);
        html.ShouldContain("Use this suggestion");
    }
}
