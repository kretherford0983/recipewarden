using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Health;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Health;

namespace RecipeWarden.Web.Tests;

/// <summary>SEC-12, AC-72: /admin/users.</summary>
[Trait("Req", "SEC-12")]
[Trait("Req", "AC-72")]
[Trait("Req", "FR-AUTH-03")]
public sealed class SEC_12_UsersPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> IdOfAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email))!.Id;
    }

    private async Task<IList<string>> RolesOfAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await users.GetRolesAsync((await users.FindByEmailAsync(email))!);
    }

    [Fact]
    public async Task The_last_admin_cannot_be_demoted_and_nobody_changes_their_own_role()
    {
        using var admin = await factory.SignInAsync("only-admin@example.com", AppRoles.Admin);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            foreach (var other in await users.GetUsersInRoleAsync(AppRoles.Admin))
            {
                if (other.Email != "only-admin@example.com")
                {
                    await users.RemoveFromRoleAsync(other, AppRoles.Admin);
                }
            }
        }

        var self = await admin.PostFormAsync("/admin/users?handler=Role", ("id", await IdOfAsync("only-admin@example.com")), ("role", AppRoles.Editor));
        self.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await self.TextAsync()).ShouldContain("own role");

        (await admin.PostFormAsync("/admin/users?handler=Create", ("NewUser.Email", "second-admin@example.com"), ("NewUser.Password", RecipeWardenFactory.Password),
            ("NewUser.Role", AppRoles.Admin))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using var second = factory.CreateHttpsClient();
        (await RecipeWardenFactory.PostLoginAsync(second, "second-admin@example.com", RecipeWardenFactory.Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await second.PostFormAsync("/admin/users?handler=Role", ("id", await IdOfAsync("only-admin@example.com")), ("role", AppRoles.Editor)))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await RolesOfAsync("only-admin@example.com")).ShouldBe([AppRoles.Editor]);

        var last = await second.PostFormAsync("/admin/users?handler=Delete", ("id", await IdOfAsync("second-admin@example.com")));
        last.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admins_create_lock_and_delete_accounts_and_it_is_audited()
    {
        using var admin = await factory.SignInAsync("users-admin@example.com", AppRoles.Admin);
        (await admin.PostFormAsync("/admin/users?handler=Create", ("NewUser.Email", "new-cook@example.com"), ("NewUser.Password", "short"),
            ("NewUser.Role", AppRoles.Editor))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostFormAsync("/admin/users?handler=Create", ("NewUser.Email", "new-cook@example.com"), ("NewUser.Password", RecipeWardenFactory.Password),
            ("NewUser.Role", "Owner"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostFormAsync("/admin/users?handler=Create", ("NewUser.Email", "new-cook@example.com"), ("NewUser.Password", RecipeWardenFactory.Password),
            ("NewUser.Role", AppRoles.Editor))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await RolesOfAsync("new-cook@example.com")).ShouldBe([AppRoles.Editor]);
        (await admin.GetStringAsync("/admin/users", Ct)).ShouldContain("new-cook@example.com");

        var id = await IdOfAsync("new-cook@example.com");
        (await admin.PostFormAsync("/admin/users?handler=Lock", ("id", id))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using (var cook = factory.CreateHttpsClient())
        {
            (await RecipeWardenFactory.PostLoginAsync(cook, "new-cook@example.com", RecipeWardenFactory.Password)).StatusCode.ShouldBe(HttpStatusCode.OK, "locked");
        }

        (await admin.PostFormAsync("/admin/users?handler=Unlock", ("id", id))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await admin.PostFormAsync("/admin/users?handler=Password", ("id", id), ("password", "another long password"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using (var cook = factory.CreateHttpsClient())
        {
            (await RecipeWardenFactory.PostLoginAsync(cook, "new-cook@example.com", "another long password")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        (await admin.PostFormAsync("/admin/users?handler=Delete", ("id", id))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var actions = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEntries.AsNoTracking()
            .Where(a => a.UserName == "users-admin@example.com").Select(a => a.Action).ToListAsync(Ct);
        actions.ShouldContain("User.Create");
        actions.ShouldContain("UserRole.Create");
        actions.ShouldContain("User.Update");
        actions.ShouldContain("User.Delete");
    }

    [Fact]
    [Trait("Req", "AC-71")]
    public async Task Editors_cannot_manage_users()
    {
        using var editor = await factory.SignInAsync("users-editor@example.com", AppRoles.Editor);
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(editor, "/manage");
        var response = await editor.PostAsync("/admin/users?handler=Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["NewUser.Email"] = "sneaky@example.com",
            ["NewUser.Password"] = RecipeWardenFactory.Password,
            ["NewUser.Role"] = AppRoles.Admin,
            ["__RequestVerificationToken"] = token,
        }), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

/// <summary>FR-ADM-01, AC-74: audit entries from real requests, and /admin/audit.</summary>
[Trait("Req", "FR-ADM-01")]
[Trait("Req", "AC-74")]
public sealed class FR_ADM_01_AuditPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recipe_saved_through_the_editor_is_audited_with_user_time_and_ip_and_shown_on_the_page()
    {
        using var editor = await factory.SignInAsync("audit-editor@example.com", AppRoles.Editor);
        var slug = await editor.AddRecipeAsync("Audited Chili");
        var id = await factory.RecipeIdAsync(slug);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var entry = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEntries.AsNoTracking()
                .FirstAsync(a => a.Action == "Recipe.Create" && a.EntityId == id.ToString(System.Globalization.CultureInfo.InvariantCulture), Ct);
            entry.UserName.ShouldBe("audit-editor@example.com");
            entry.UserId.ShouldNotBeNull();
            entry.Ip.ShouldBe("127.0.0.1");
            (DateTimeOffset.UtcNow - entry.At).ShouldBeLessThan(TimeSpan.FromMinutes(5));
        }

        using var admin = await factory.SignInAsync("audit-admin@example.com", AppRoles.Admin);
        var html = await admin.GetStringAsync("/admin/audit?action=Recipe.&user=audit-editor", Ct);
        html.ShouldContain("Recipe.Create");
        html.ShouldContain("Audited Chili");
        (await admin.GetStringAsync("/admin/audit?action=Account.SignIn", Ct)).ShouldContain("audit-admin@example.com");
        (await admin.GetStringAsync("/admin/audit?action=nothing-like-this", Ct)).ShouldContain("No entries match");
    }

    [Fact]
    [Trait("Req", "SEC-72")]
    public async Task Failed_sign_ins_are_audited_without_the_password()
    {
        using var client = factory.CreateHttpsClient();
        await RecipeWardenFactory.PostLoginAsync(client, "nobody@example.com", "my secret attempt");

        await using var scope = factory.Services.CreateAsyncScope();
        var entry = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEntries.AsNoTracking()
            .OrderByDescending(a => a.Id).FirstAsync(a => a.Action == "Account.SignInFailed", Ct);
        entry.DetailsJson.ShouldNotBeNull().ShouldContain("nobody@example.com");
        entry.DetailsJson.ShouldNotContain("my secret attempt");
    }
}

/// <summary>FR-OPS-02 to 05, AC-98, AC-99: the health page, the banner and /health/ready with a simulated disk.</summary>
[Trait("Req", "FR-OPS-02")]
[Trait("Req", "FR-OPS-03")]
public sealed class FR_OPS_02_HealthPageTests : IClassFixture<FR_OPS_02_HealthPageTests.Factory>
{
    private readonly Factory factory;

    public FR_OPS_02_HealthPageTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class FakeVolumes : IVolumeProbe
    {
        public long Total { get; set; } = 100L * 1024 * 1024 * 1024;

        public long Free { get; set; } = 60L * 1024 * 1024 * 1024;

        public (long Total, long Free) Measure(string path) => (Total, Free);
    }

    public sealed class Factory : IDisposable
    {
        public Factory()
        {
            Host = new RecipeWardenFactory();
            Host.ExtraServices.Add(s => s.AddSingleton<IVolumeProbe>(Volumes));
        }

        public FakeVolumes Volumes { get; } = new();

        public RecipeWardenFactory Host { get; }

        public void Dispose() => Host.Dispose();
    }

    private async Task EvaluateAsync()
    {
        await using var scope = factory.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HealthService>().EvaluateAsync(store: true, Ct);
    }

    [Fact]
    [Trait("Req", "AC-99")]
    [Trait("Req", "FR-OPS-05")]
    public async Task Low_space_gives_warning_with_banner_then_critical_with_503()
    {
        using var admin = await factory.Host.SignInAsync("health-admin@example.com", AppRoles.Admin);
        using var editor = await factory.Host.SignInAsync("health-editor@example.com", AppRoles.Editor);
        using var anonymous = factory.Host.CreateHttpsClient();
        (await admin.PostFormAsync("/admin/backups?handler=BackupNow", ("folder", factory.Host.DataDirectory + "/backups"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        factory.Volumes.Free = factory.Volumes.Total * 15 / 100;
        await EvaluateAsync();
        var ready = await anonymous.GetAsync("/health/ready", Ct);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ready.TextAsync()).ShouldBe("warning");
        var page = await admin.GetStringAsync("/recipes", Ct);
        page.ShouldContain("admin-banner");
        page.ShouldContain("Disk: data");
        (await editor.GetStringAsync("/recipes", Ct)).ShouldNotContain("admin-banner", customMessage: "only Admins see the banner");

        factory.Volumes.Free = factory.Volumes.Total * 8 / 100;
        await EvaluateAsync();
        var critical = await anonymous.GetAsync("/health/ready", Ct);
        critical.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await critical.TextAsync()).ShouldBe("critical");

        factory.Volumes.Free = factory.Volumes.Total * 60 / 100;
        await EvaluateAsync();
        (await anonymous.GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Req", "AC-98")]
    [Trait("Req", "FR-OPS-04")]
    public async Task Health_page_shows_the_figures_and_runs_maintenance()
    {
        using var admin = await factory.Host.SignInAsync("health-admin2@example.com", AppRoles.Admin);
        var html = await admin.GetStringAsync("/admin/health", Ct);
        foreach (var text in new[] { "Database", "WAL", "Reclaimable free pages", "Last integrity check", "Space by category", "Images", "Last successful backup", "Move to a new host" })
        {
            html.ShouldContain(text);
        }

        var dbBytes = new FileInfo(Path.Combine(factory.Host.DataDirectory, "db", "recipewarden.db")).Length;
        html.ShouldContain(HealthRules.FormatBytes(dbBytes));

        foreach (var handler in new[] { "Integrity", "Compact", "Purge" })
        {
            var response = await admin.PostFormAsync("/admin/health?handler=" + handler);
            response.StatusCode.ShouldBe(HttpStatusCode.Redirect, handler);
        }

        (await admin.GetStringAsync("/admin/health", Ct)).ShouldContain("passed");
        (await admin.GetStringAsync("/admin", Ct)).ShouldContain("Maintenance.");
    }
}

/// <summary>/admin/settings and /admin/nutrition-data.</summary>
[Trait("Req", "FR-AUTH-03")]
public sealed class M7_SettingsPageTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (string, string)[] Form(string siteName = "Our Kitchen", string warnPct = "20", string critPct = "10", string? webhook = null) =>
    [
        ("Input.SiteName", siteName), ("Input.WarningPercent", warnPct), ("Input.WarningGb", "2"), ("Input.CriticalPercent", critPct),
        ("Input.CriticalMb", "500"), ("Input.WebhookUrl", webhook ?? string.Empty), ("Input.WebhookFormat", "Text"),
    ];

    [Fact]
    public async Task Site_name_is_saved_shown_in_the_header_and_audited()
    {
        using var admin = await factory.SignInAsync("settings-admin@example.com", AppRoles.Admin);
        (await admin.PostFormAsync("/admin/settings", Form())).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        using var anonymous = factory.CreateHttpsClient();
        var home = await anonymous.GetStringAsync("/", Ct);
        home.ShouldContain("<a class=\"brand\" href=\"/\">Our Kitchen</a>");
        home.ShouldContain("· Our Kitchen</title>");

        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEntries.AnyAsync(a => a.Action == "Settings.Update", Ct)).ShouldBeTrue();
        (await admin.PostFormAsync("/admin/settings", Form(siteName: ""))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    [Trait("Req", "BR-81")]
    public async Task Critical_above_warning_is_refused()
    {
        using var admin = await factory.SignInAsync("settings-admin2@example.com", AppRoles.Admin);
        var response = await admin.PostFormAsync("/admin/settings", Form(warnPct: "10", critPct: "20"));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.TextAsync()).ShouldContain("Critical thresholds");
    }

    [Fact]
    [Trait("Req", "FR-OPS-05")]
    [Trait("Req", "SEC-41")]
    public async Task A_LAN_webhook_is_refused_unless_allowed_and_a_test_send_is_reported()
    {
        using var admin = await factory.SignInAsync("settings-admin3@example.com", AppRoles.Admin);
        var lan = await admin.PostFormAsync("/admin/settings", Form(webhook: "http://192.168.1.50:8080/alerts"));
        lan.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await lan.TextAsync()).ShouldContain("home network");

        var test = await admin.PostFormAsync("/admin/settings?handler=TestWebhook", Form(webhook: "https://ntfy.example.com/rw"));
        test.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "the test host has no internet (FakeWeb answers nothing for this host)");
        (await test.TextAsync()).ShouldContain("not delivered");
    }

    [Fact]
    [Trait("Req", "FR-NUT-05")]
    public async Task Nutrition_data_page_lists_the_dataset_and_refuses_files_that_are_not_zips()
    {
        using var admin = await factory.SignInAsync("nutrition-admin@example.com", AppRoles.Admin);
        (await admin.GetStringAsync("/admin/nutrition-data", Ct)).ShouldContain("SR Legacy");

        var bad = await admin.PostFilesAsync("/admin/nutrition-data", [("file", "data.zip", "not a zip"u8.ToArray())]);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.TextAsync()).ShouldContain(ArchiveUploadRules.WrongDatasetType);
    }
}
