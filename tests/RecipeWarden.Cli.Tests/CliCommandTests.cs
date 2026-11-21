using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using RecipeWarden.Cli.Admin;
using RecipeWarden.Cli.Health;
using RecipeWarden.Cli.Hosting;

namespace RecipeWarden.Cli.Tests;

public sealed class CliCommandTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rw-cli-").FullName;

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Directory"] = dir })
        .Build();

    [Fact]
    [Trait("Req", "FR-INST-04")]
    [Trait("Req", "SEC-05")]
    public async Task Admin_create_creates_an_admin_and_refuses_a_second_with_the_same_email()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var first = await AdminCreateCommand.RunAsync(Config(), "admin@example.com", "correct horse battery", output, error, TestContext.Current.CancellationToken);
        var second = await AdminCreateCommand.RunAsync(Config(), "admin@example.com", "correct horse battery", output, error, TestContext.Current.CancellationToken);

        first.ShouldBe(0);
        output.ToString().ShouldContain("Admin account admin@example.com created.");
        second.ShouldBe(1);
        error.ToString().ShouldContain("already exists");
    }

    [Fact]
    [Trait("Req", "SEC-01")]
    public async Task Admin_create_refuses_a_short_password()
    {
        var error = new StringWriter();
        var exit = await AdminCreateCommand.RunAsync(Config(), "admin@example.com", "too-short", new StringWriter(), error, TestContext.Current.CancellationToken);

        exit.ShouldBe(1);
        error.ToString().ShouldContain("12");
    }

    [Theory]
    [Trait("Req", "FR-INST-04")]
    [Trait("Req", "AC-100")]
    [InlineData("first.last@#example.com")]
    [InlineData("not-an-email")]
    [InlineData("x;id@example.com")]
    public async Task Admin_create_rejects_an_invalid_email_before_asking_for_a_password(string email)
    {
        var asked = false;
        var error = new StringWriter();

        var exit = await AdminCreateCommand.RunAsync(
            Config(), email, () => { asked = true; return "correct horse battery"; }, new StringWriter(), error, TestContext.Current.CancellationToken);

        exit.ShouldBe(1);
        asked.ShouldBeFalse();
        error.ToString().ShouldContain($"'{email}' is not a valid email address");
        File.Exists(Path.Combine(dir, "db", "recipewarden.db")).ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "FR-INST-04")]
    [Trait("Req", "AC-100")]
    public async Task Admin_create_rejects_an_existing_email_before_asking_for_a_password()
    {
        await AdminCreateCommand.RunAsync(Config(), "admin@example.com", "correct horse battery", new StringWriter(), new StringWriter(), TestContext.Current.CancellationToken);
        var asked = false;
        var error = new StringWriter();

        var exit = await AdminCreateCommand.RunAsync(
            Config(), "ADMIN@example.com", () => { asked = true; return "another long password"; }, new StringWriter(), error, TestContext.Current.CancellationToken);

        exit.ShouldBe(1);
        asked.ShouldBeFalse();
        error.ToString().ShouldContain("already exists");
    }

    [Fact]
    [Trait("Req", "FR-INST-04")]
    [Trait("Req", "AC-100")]
    public async Task Admin_create_asks_for_the_password_once_the_email_is_valid_and_new()
    {
        var asked = 0;
        var output = new StringWriter();

        var exit = await AdminCreateCommand.RunAsync(
            Config(), "admin@example.com", () => { asked++; return "correct horse battery"; }, output, new StringWriter(), TestContext.Current.CancellationToken);

        exit.ShouldBe(0);
        asked.ShouldBe(1);
        output.ToString().ShouldContain("Admin account admin@example.com created.");
    }

    [Fact]
    [Trait("Req", "FR-OPS-01")]
    public async Task Health_reports_ok_for_an_initialized_database()
    {
        await AdminCreateCommand.RunAsync(Config(), "admin@example.com", "correct horse battery", new StringWriter(), new StringWriter(), TestContext.Current.CancellationToken);
        var output = new StringWriter();

        var exit = await HealthCommand.RunAsync(Config(), output, TestContext.Current.CancellationToken);

        exit.ShouldBe(0);
        output.ToString().ShouldContain("quick_check: ok");
        output.ToString().ShouldContain("journal_mode: wal");
        output.ToString().ShouldContain("Status: OK");
    }

    [Fact]
    [Trait("Req", "FR-OPS-01")]
    public async Task Health_reports_critical_without_a_database()
    {
        var output = new StringWriter();
        var exit = await HealthCommand.RunAsync(Config(), output, TestContext.Current.CancellationToken);

        exit.ShouldBe(1);
        output.ToString().ShouldContain("Status: Critical");
    }

    [Fact]
    public void Health_uri_comes_from_the_kestrel_url_and_never_targets_0_0_0_0()
    {
        CliConfig.HealthUri(new ConfigurationBuilder().Build()).ToString().ShouldBe("http://127.0.0.1:5080/health");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kestrel:Endpoints:Http:Url"] = "http://0.0.0.0:6000" })
            .Build();
        CliConfig.HealthUri(config).ToString().ShouldBe("http://127.0.0.1:6000/health");
    }

    [Fact]
    public void Config_path_prefers_the_explicit_option()
    {
        CliConfig.ResolvePath("/tmp/x.json").ShouldBe("/tmp/x.json");
        CliConfig.ResolvePath(null).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Config_file_is_loaded_from_disk()
    {
        var path = Path.Combine(dir, "appsettings.Production.json");
        File.WriteAllText(path, """{ "Backup": { "AllowedFolders": [ "/mnt/a", "/mnt/b" ] } }""");

        var config = CliConfig.Load(path);

        config.GetSection("Backup:AllowedFolders").GetChildren().Select(c => c.Value).ShouldBe(["/mnt/a", "/mnt/b"]);
    }

    [Fact]
    [Trait("Req", "FR-INST-02")]
    public void Network_json_beside_the_config_overrides_the_listening_address()
    {
        var path = Path.Combine(dir, "appsettings.Production.json");
        File.WriteAllText(path, """{ "Kestrel": { "Endpoints": { "Http": { "Url": "http://127.0.0.1:5080" } } } }""");
        File.WriteAllText(Path.Combine(dir, "network.json"), """{ "Kestrel": { "Endpoints": { "Http": { "Url": "http://192.168.1.15:6080" } } } }""");

        CliConfig.HealthUri(CliConfig.Load(path)).ToString().ShouldBe("http://192.168.1.15:6080/health");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
