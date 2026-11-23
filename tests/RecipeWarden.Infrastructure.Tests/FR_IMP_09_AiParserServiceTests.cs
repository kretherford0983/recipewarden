using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.AiParser;
using RecipeWarden.Infrastructure.Ai;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>FR-IMP-09/10, FR-NUT-06, AC-17, AC-18, SEC-44 to 47: the AI parser against a pretend internet.</summary>
[Trait("Req", "FR-IMP-09")]
public sealed class FR_IMP_09_AiParserServiceTests
{
    private const string ClaudeUrl = "http://api.anthropic.test/v1/messages";
    private const string Secret = "fake-test-key-one";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string BlogProse = """
        My grandmother made this every winter. You will want two pounds of beef chuck, three carrots and an onion,
        plus a spoon of flour. First you brown the beef in a heavy pot, then add everything else with water and let it
        simmer for two hours until tender. Serve with bread.
        """;

    private static readonly string StewMarkdown = """
        ---
        format: recipe-md/1
        title: "Grandma's Winter Stew"
        servings: 4
        ---

        ## Ingredients

        - 2 lb beef chuck
        - 3 carrots
        - 1 onion
        - 1 Tbsp flour

        ## Steps

        1. Brown the beef in a heavy pot.
        2. Add everything else with water and simmer 2 hours.
        """;

    private sealed class Scope : IAsyncDisposable
    {
        private readonly TestDatabase tdb;

        private Scope(TestDatabase tdb, FakeWeb web)
        {
            this.tdb = tdb;
            Web = web;
        }

        public FakeWeb Web { get; }

        public static async Task<Scope> CreateAsync(TimeProvider? clock = null)
        {
            var web = new FakeWeb();
            var tdb = await TestDatabase.CreateAsync(clock: clock, configure: s =>
            {
                s.AddSingleton<IDnsResolver>(web);
                s.AddSingleton<ISocketConnector>(web);
            }, settings: new Dictionary<string, string?> { ["Ai:ClaudeEndpoint"] = ClaudeUrl });
            return new Scope(tdb, web);
        }

        public async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
        {
            await using var scope = tdb.Provider.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }

        public Task<string?> ConfigureAsync(AiSettings settings, string? key = Secret) =>
            WithAsync(sp => sp.GetRequiredService<AiParserService>().SaveSettingsAsync(settings, key, false, Ct));

        public void ClaudeReplies(string text, int inTokens = 120, int outTokens = 80) =>
            Web.Serve(ClaudeUrl, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                content = new[] { new { type = "text", text } },
                usage = new { input_tokens = inTokens, output_tokens = outTokens },
            }));

        public IEnumerable<FakeWeb.Request> AiRequests => Web.Requests.Where(r => r.Host.Contains("anthropic", StringComparison.Ordinal) || r.Host.StartsWith("192.168.", StringComparison.Ordinal));

        public async ValueTask DisposeAsync()
        {
            await tdb.DisposeAsync();
            await Web.DisposeAsync();
        }
    }

    private static AiSettings Claude(long? cap = null) => new(true, AiProvider.Claude, null, "claude-haiku-5-5", cap, false);

    [Fact]
    [Trait("Req", "AC-17")]
    public async Task With_the_parser_off_no_request_is_made_to_any_AI_host_during_any_import()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(StewMarkdown);
        await s.ConfigureAsync(Claude() with { Enabled = false });

        var start = await s.WithAsync(sp => sp.GetRequiredService<ImportService>().StartPasteAsync(BlogProse, null, false, "u", Ct));

        start.Error.ShouldNotBeNull("the rules find no recipe in prose");
        s.AiRequests.ShouldBeEmpty();
        s.Web.Lookups.ShouldBeEmpty();
        (await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AiCallLogs.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "AC-17")]
    [Trait("Req", "SEC-46")]
    public async Task With_Claude_on_blog_prose_becomes_a_marked_draft_for_review_and_nothing_is_saved()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(StewMarkdown);
        (await s.ConfigureAsync(Claude())).ShouldBeNull();
        var recipesBefore = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().Recipes.CountAsync(Ct));

        var start = await s.WithAsync(sp => sp.GetRequiredService<ImportService>().StartPasteAsync(BlogProse, null, false, "u", Ct));

        start.Error.ShouldBeNull();
        var review = (await s.WithAsync(sp => sp.GetRequiredService<ImportService>().GetReviewAsync(start.JobIds[0], Ct))).ShouldNotBeNull();
        review.ParsedByAi.ShouldBeTrue();
        review.Recipe.Title.ShouldBe("Grandma's Winter Stew");
        review.Recipe.Ingredients.ShouldNotBeNull().ShouldContain("2 lb beef chuck");
        review.Recipe.Steps.ShouldNotBeNull().ShouldContain("Brown the beef");
        review.Warnings.Select(w => w.Message).ShouldContain(AiParserRules.AiParsedNotice);
        (await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().Recipes.CountAsync(Ct))).ShouldBe(recipesBefore, "nothing is saved until the user confirms");

        var request = s.AiRequests.ShouldHaveSingleItem();
        request.Headers["x-api-key"].ShouldBe(Secret);
        request.Body.ShouldContain("two pounds of beef chuck");
        request.Body.ShouldNotContain("\"u\"", customMessage: "no user or account data (SEC-46)");
        request.Body.ShouldNotContain("tools");

        var log = await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AiCallLogs.AsNoTracking().SingleAsync(Ct));
        log.Result.ShouldBe(AiCallResult.Ok);
        log.InputTokens.ShouldBe(120);
        log.OutputTokens.ShouldBe(80);
        log.Purpose.ShouldBe(AiPurpose.Import);
    }

    [Fact]
    [Trait("Req", "AC-18")]
    [Trait("Req", "SEC-44")]
    public async Task A_LAN_OpenAI_endpoint_works_at_its_exact_host_and_port_while_other_private_addresses_stay_refused()
    {
        await using var s = await Scope.CreateAsync();
        s.Web.Serve("http://192.168.1.50:11434/v1/chat/completions", 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            choices = new[] { new { message = new { role = "assistant", content = StewMarkdown } } },
            usage = new { prompt_tokens = 50, completion_tokens = 40 },
        }));
        (await s.ConfigureAsync(new AiSettings(true, AiProvider.OpenAiCompatible, "http://192.168.1.50:11434", "llama3.1", null, true), key: null)).ShouldBeNull();

        var start = await s.WithAsync(sp => sp.GetRequiredService<ImportService>().StartPasteAsync(BlogProse, null, false, "u", Ct));
        start.Error.ShouldBeNull();
        s.AiRequests.ShouldHaveSingleItem().PathAndQuery.ShouldBe("/v1/chat/completions");

        // The permit is that host and port only: the same host on another port, another LAN host, and imports stay refused.
        var fetcher = await s.WithAsync(sp => Task.FromResult(sp.GetRequiredService<SafeFetcher>()));
        var permit = new Core.Security.PermittedEndpoint("http", "192.168.1.50", 11434);
        (await fetcher.SendAsync(Post("http://192.168.1.50:8080/x", permit), Ct)).Failure.ShouldBe(FetchFailure.Refused);
        (await fetcher.SendAsync(Post("http://192.168.1.51:11434/x", permit), Ct)).Failure.ShouldNotBe(FetchFailure.None);
        (await fetcher.SendAsync(Post("http://192.168.1.51/x", permit), Ct)).Failure.ShouldBe(FetchFailure.Blocked);
        (await fetcher.FetchAsync("http://192.168.1.50:11434/", FetchPurpose.Page, Ct)).Failure.ShouldNotBe(FetchFailure.None);
        s.Web.Connections.Count.ShouldBe(1, "only the permitted call connected");
    }

    private static OutboundRequest Post(string url, Core.Security.PermittedEndpoint permit) =>
        new(new Uri(url), HttpMethod.Post, [1], "application/json", new Dictionary<string, string>(), permit, TimeSpan.FromSeconds(5), 1024);

    [Fact]
    [Trait("Req", "AC-18")]
    [Trait("Req", "SEC-45")]
    public async Task The_key_is_encrypted_at_rest_and_never_logged_or_audited()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(StewMarkdown);
        await s.ConfigureAsync(Claude());
        await s.WithAsync(sp => sp.GetRequiredService<ImportService>().StartPasteAsync(BlogProse, null, false, "u", Ct));

        var db = await s.WithAsync(async sp =>
        {
            var d = sp.GetRequiredService<AppDbContext>();
            var settings = await d.AppSettings.AsNoTracking().ToListAsync(Ct);
            var audit = await d.AuditEntries.AsNoTracking().ToListAsync(Ct);
            var logs = await d.AiCallLogs.AsNoTracking().ToListAsync(Ct);
            return (settings, audit, logs);
        });
        var stored = db.settings.Single(x => x.Key == AiParserService.ApiKey).Value;
        stored.ShouldNotContain(Secret);
        stored.Length.ShouldBeGreaterThan(40);
        db.audit.ShouldAllBe(a => a.DetailsJson == null || !a.DetailsJson.Contains(Secret, StringComparison.Ordinal));
        db.logs.ShouldAllBe(l => l.Error == null || !l.Error.Contains(Secret, StringComparison.Ordinal));
        (await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().HasKeyAsync(Ct))).ShouldBeTrue();
    }

    [Fact]
    [Trait("Req", "AC-18")]
    [Trait("Req", "FR-IMP-10")]
    public async Task At_the_monthly_cap_AI_pauses_with_a_message_and_the_rules_based_parser_is_used()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(StewMarkdown, 600, 500);
        await s.ConfigureAsync(Claude(cap: 1000));
        // A recipe the rules can read, but with steps missing, so AI would normally be tried.
        const string text = "Quick Stew\n\nIngredients\n2 lb beef\n3 carrots\n\nSteps\n";

        var first = await s.WithAsync(sp => sp.GetRequiredService<ImportService>().StartPasteAsync(BlogProse, null, false, "u", Ct));
        first.Error.ShouldBeNull();
        var second = await s.WithAsync(sp => sp.GetRequiredService<ImportService>().StartPasteAsync(text, null, false, "u", Ct));
        second.Error.ShouldBeNull();

        var review = (await s.WithAsync(sp => sp.GetRequiredService<ImportService>().GetReviewAsync(second.JobIds[0], Ct)))!;
        review.ParsedByAi.ShouldBeFalse();
        review.Recipe.Title.ShouldBe("Quick Stew");
        review.Warnings.Select(w => w.Message).ShouldContain(AiParserRules.CapReached);
        s.AiRequests.Count().ShouldBe(1);
        (await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AiCallLogs.AnyAsync(l => l.Result == AiCallResult.Capped, Ct))).ShouldBeTrue();
        (await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().UsageThisMonthAsync(Ct))).Total.ShouldBe(1100);
    }

    [Fact]
    [Trait("Req", "AC-18")]
    [Trait("Req", "SEC-47")]
    public async Task HTML_or_script_in_a_reply_is_kept_as_inert_text()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(StewMarkdown.Replace("Grandma's Winter Stew", "<script>alert(1)</script> Stew", StringComparison.Ordinal)
            .Replace("1. Brown the beef in a heavy pot.", "1. <img src=x onerror=alert(2)> Brown the beef.", StringComparison.Ordinal));
        await s.ConfigureAsync(Claude());

        var start = await s.WithAsync(sp => sp.GetRequiredService<ImportService>().StartPasteAsync(BlogProse, null, false, "u", Ct));
        var review = (await s.WithAsync(sp => sp.GetRequiredService<ImportService>().GetReviewAsync(start.JobIds[0], Ct)))!;

        // The recipe-md reader keeps text only (BR-13): the markup is gone, and pages Razor-encode whatever remains (AC-76).
        review.Recipe.Title.ShouldBe("Stew");
        review.Recipe.Steps.ShouldNotBeNull().ShouldContain("Brown the beef.");
        review.Recipe.Steps.ShouldNotContain("<img");
        review.Recipe.Steps.ShouldNotContain("onerror");
    }

    [Fact]
    public async Task A_reply_without_valid_recipe_md_keeps_the_rules_result_and_is_logged_invalid()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies("Sure! Here is your recipe: it is a stew.");
        await s.ConfigureAsync(Claude());

        var start = await s.WithAsync(sp => sp.GetRequiredService<ImportService>().StartPasteAsync(BlogProse, null, false, "u", Ct));

        start.Error.ShouldNotBeNull("no rules result to fall back to");
        (await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AiCallLogs.SingleAsync(Ct))).Result.ShouldBe(AiCallResult.Invalid);
    }

    [Fact]
    public async Task Reparse_with_AI_replaces_the_draft_and_keeps_the_source()
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(StewMarkdown);
        await s.ConfigureAsync(Claude() with { Enabled = false });
        var start = await s.WithAsync(sp => sp.GetRequiredService<ImportService>()
            .StartPasteAsync("Quick Stew\n\nIngredients\n2 lb beef\n\nSteps\nCook.", "https://blog.example.com/stew", false, "u", Ct));
        var job = start.JobIds[0];

        (await s.WithAsync(sp => sp.GetRequiredService<ImportService>().ReparseWithAiAsync(job, Ct))).ShouldBe(AiParserRules.Disabled);
        await s.ConfigureAsync(Claude());
        (await s.WithAsync(sp => sp.GetRequiredService<ImportService>().ReparseWithAiAsync(job, Ct))).ShouldBeNull();

        var review = (await s.WithAsync(sp => sp.GetRequiredService<ImportService>().GetReviewAsync(job, Ct)))!;
        review.ParsedByAi.ShouldBeTrue();
        review.Recipe.Title.ShouldBe("Grandma's Winter Stew");
        review.Recipe.SourceUrl.ShouldBe("https://blog.example.com/stew");
        (await s.WithAsync(sp => sp.GetRequiredService<AppDbContext>().AiCallLogs.SingleAsync(Ct))).Purpose.ShouldBe(AiPurpose.Reparse);
    }

    [Theory]
    [InlineData("2", 1, null)]
    [InlineData("none", null, "no match")]
    [InlineData("99", null, "no match")]
    [Trait("Req", "FR-NUT-06")]
    public async Task Mapping_suggestions_pick_only_among_local_candidates(string reply, int? index, string? error)
    {
        await using var s = await Scope.CreateAsync();
        s.ClaudeReplies(reply, 10, 1);
        await s.ConfigureAsync(Claude());

        var result = await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().SuggestMappingAsync("gochujang", ["Ketchup", "Hot chile paste"], Ct));

        result.Index.ShouldBe(index);
        if (error is not null)
        {
            result.Error.ShouldNotBeNull().ShouldContain(error);
        }

        var body = s.AiRequests.Single().Body;
        body.ShouldContain("gochujang");
        body.ShouldContain("Hot chile paste");
    }

    [Fact]
    [Trait("Req", "FR-IMP-10")]
    public async Task Test_connection_reports_failures_and_works_while_the_parser_is_off()
    {
        await using var s = await Scope.CreateAsync();
        await s.ConfigureAsync(Claude() with { Enabled = false });
        (await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().TestAsync(Ct))).ShouldNotBeNull("nothing answers yet");

        s.ClaudeReplies("ok", 5, 1);
        (await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().TestAsync(Ct))).ShouldBeNull();
        s.Web.Serve(ClaudeUrl, 401, "application/json", Encoding.UTF8.GetBytes("""{"error":{"message":"invalid x-api-key"}}"""));
        (await s.WithAsync(sp => sp.GetRequiredService<AiParserService>().TestAsync(Ct))).ShouldNotBeNull().ShouldContain("invalid x-api-key");
    }
}
