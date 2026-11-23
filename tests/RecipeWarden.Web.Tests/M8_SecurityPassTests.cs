using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// M8 security pass (ADR 0043): rules that had no automated test until now. SEC-31 against the running app; SEC-20,
/// SEC-22, SEC-24, SEC-70 and SEC-80 to SEC-84 against the repository, so a later change cannot quietly drop them.
/// </summary>
public sealed partial class M8_SecurityPassTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RecipeWarden.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found");
    }

    private static IEnumerable<string> Sources(string folder, params string[] patterns) =>
        patterns.SelectMany(p => Directory.EnumerateFiles(Path.Combine(Root, folder), p, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    [Fact]
    [Trait("Req", "SEC-31")]
    public async Task Get_requests_never_write_to_the_database()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecipeService>().CreateAsync(new RecipeEdit
            {
                Title = "Read Only Risotto", IngredientsText = "1 cup rice", StepsText = "Stir.", Tags = ["Dinner"], WantsPublish = true,
            }, "seed", Ct);
        }

        using var admin = await factory.SignInAsync("sec31-admin@example.com", AppRoles.Admin);
        using var anonymous = factory.CreateHttpsClient();
        var id = await factory.RecipeIdAsync("read-only-risotto");
        var data = factory.Services.GetRequiredService<DataOptions>();
        await using var watcher = new SqliteConnection(data.ConnectionString);
        await watcher.OpenAsync(Ct);

        async Task<long> VersionAsync()
        {
            await using var command = watcher.CreateCommand();
            command.CommandText = "PRAGMA data_version";
            return (long)(await command.ExecuteScalarAsync(Ct))!;
        }

        var before = await VersionAsync();
        var paths = new[]
        {
            "/", "/recipes", "/recipes/read-only-risotto", "/recipes/read-only-risotto/cook", "/recipes/read-only-risotto/print",
            "/recipes/read-only-risotto/export.md", "/search?q=risotto", "/tags/dinner", "/manage", "/manage/recipes/new", $"/manage/recipes/{id}/edit",
            "/manage/import", "/manage/kitchen", "/manage/nutrition", "/manage/tags", "/manage/collections", "/manage/trash", "/shopping",
            "/admin", "/admin/users", "/admin/backups", "/admin/health", "/admin/settings", "/admin/audit", "/admin/ai", "/admin/nutrition-data",
            "/account/2fa", "/account/password", "/health", "/health/ready",
        };
        var failures = new List<string>();
        foreach (var path in paths)
        {
            foreach (var client in new[] { admin, anonymous })
            {
                using var get = await client.GetAsync(path, Ct);
                using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path), Ct);
                var after = await VersionAsync();
                if (after != before)
                {
                    failures.Add($"{path} ({(client == admin ? "admin" : "anonymous")}) wrote to the database");
                    before = after;
                }
            }
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Fact]
    [Trait("Req", "SEC-20")]
    [Trait("Req", "SEC-22")]
    public void No_raw_sql_or_raw_html_anywhere_in_the_source()
    {
        var banned = new[] { "FromSqlRaw", "ExecuteSqlRaw", "SqlQueryRaw", "Html.Raw(", "new HtmlString(" };
        // The one HtmlString: Markdig output (raw HTML off) after the sanitizer allow-list, as SEC-22 prescribes.
        var allowed = new[] { "src/RecipeWarden.Web/Content/MarkdownRenderer.cs: return new HtmlString(Sanitizer.Sanitize(html));" };
        var hits = Sources("src", "*.cs", "*.cshtml")
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !x.line.TrimStart().StartsWith("///", StringComparison.Ordinal)
                        && banned.Any(b => x.line.Contains(b, StringComparison.Ordinal)))
            .Where(x => !allowed.Contains($"{Path.GetRelativePath(Root, x.f).Replace('\\', '/')}: {x.line.Trim()}"))
            .Select(x => $"{Path.GetRelativePath(Root, x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        hits.ShouldBeEmpty(string.Join(Environment.NewLine, hits));
        Read("BannedSymbols.txt").ShouldContain("FromSqlRaw");
        Read("BannedSymbols.txt").ShouldContain("IHtmlHelper.Raw");
    }

    [Fact]
    [Trait("Req", "SEC-24")]
    public void The_web_app_never_starts_a_process()
    {
        var hits = new[] { "src/RecipeWarden.Web", "src/RecipeWarden.Infrastructure", "src/RecipeWarden.Core", "src/RecipeWarden.Importers" }
            .SelectMany(d => Sources(d, "*.cs"))
            .Where(f => File.ReadAllText(f) is var t && (t.Contains("Process.Start", StringComparison.Ordinal) || t.Contains("ProcessStartInfo", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(Root, f))
            .ToList();
        hits.ShouldBeEmpty(string.Join(", ", hits));

        // The CLI starts only fixed programs, with arguments passed as a list (never through a shell).
        var cli = Sources("src/RecipeWarden.Cli", "*.cs").Select(File.ReadAllText).ToList();
        cli.ShouldAllBe(t => !t.Contains("/bin/sh", StringComparison.Ordinal) && !t.Contains("\"bash\"", StringComparison.Ordinal)
                             && !t.Contains("UseShellExecute = true", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "SEC-70")]
    public void The_service_unit_keeps_its_systemd_hardening()
    {
        var unit = Read("deploy/recipewarden.service");
        foreach (var line in new[] { "User=recipewarden", "NoNewPrivileges=true", "ProtectSystem=strict", "ProtectHome=true", "PrivateTmp=true",
                     "ReadWritePaths=/var/lib/recipewarden /var/log/recipewarden" })
        {
            unit.ShouldContain(line);
        }
    }

    [Fact]
    [Trait("Req", "SEC-80")]
    [Trait("Req", "SEC-81")]
    [Trait("Req", "SEC-82")]
    [Trait("Req", "SEC-83")]
    [Trait("Req", "SEC-84")]
    public void Build_and_ci_gates_stay_in_place()
    {
        var dependabot = Read(".github/dependabot.yml");
        dependabot.ShouldContain("package-ecosystem: nuget");
        dependabot.ShouldContain("package-ecosystem: github-actions");

        var props = Read("Directory.Build.props");
        props.ShouldContain("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>");
        props.ShouldContain("<AnalysisMode>Recommended</AnalysisMode>");
        props.ShouldContain("<AnalysisModeSecurity>All</AnalysisModeSecurity>");

        var ci = Read(".github/workflows/ci.yml");
        ci.ShouldContain("build/check-vulnerable.sh");
        ci.ShouldContain("gitleaks/gitleaks-action@");
        File.Exists(Path.Combine(Root, ".githooks", "pre-commit")).ShouldBeTrue();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, ".github", "workflows"), "*.yml"))
        {
            var text = File.ReadAllText(file);
            Regex.IsMatch(text, @"^permissions:\s*\n\s+contents: read", RegexOptions.Multiline).ShouldBeTrue($"{file}: default permissions");
            var unpinned = UsesLine().Matches(text).Select(m => m.Value.Trim()).Where(u => !PinnedUse().IsMatch(u)).ToList();
            unpinned.ShouldBeEmpty($"{Path.GetFileName(file)}: {string.Join(", ", unpinned)}");
        }
    }

    [Fact]
    [Trait("Req", "SEC-62")]
    public async Task Security_headers_are_on_pages_errors_and_static_files()
    {
        using var client = factory.CreateHttpsClient();
        foreach (var path in new[] { "/", "/account/login", "/no-such-page", "/css/site.css" })
        {
            using var response = await client.GetAsync(path, Ct);
            response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).ShouldBeTrue(path);
            nosniff!.ShouldContain("nosniff");
            response.Headers.Contains("Referrer-Policy").ShouldBeTrue(path);
            response.Headers.Contains("Permissions-Policy").ShouldBeTrue(path);
            if (response.StatusCode != HttpStatusCode.NotFound || path != "/css/site.css")
            {
                response.Headers.Contains("Content-Security-Policy").ShouldBeTrue(path);
            }
        }
    }

    [GeneratedRegex(@"^\s*-?\s*uses:\s*\S+", RegexOptions.Multiline)]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"uses:\s*(\./|[\w.-]+/[\w./-]+@[0-9a-f]{40}\b)")]
    private static partial Regex PinnedUse();
}
