using Microsoft.Extensions.Configuration;
using RecipeWarden.Cli.Python;

namespace RecipeWarden.Cli.Tests;

/// <summary>FR-OPS-06, AC-106: "recipewarden python-selftest" against the development worker, and with none.</summary>
[Trait("Req", "FR-OPS-06")]
public sealed class FR_OPS_06_PythonSelftestCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RecipeWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    [Trait("Req", "AC-106")]
    public async Task The_development_worker_passes_and_the_checks_are_printed()
    {
        var root = Root();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await PythonSelftestCommand.RunAsync(Config(("Python:Directory", Path.Combine(root, "python", ".venv")),
            ("Python:NltkData", Path.Combine(root, "python", ".nltk_data"))), output, error, Ct);

        code.ShouldBe(0, error.ToString());
        var text = output.ToString();
        text.ShouldContain("Engine: worker 1.0.0, Python 3.13");
        text.ShouldContain("Checks: network blocked yes, parse yes");
        text.ShouldContain("Self-test: passed in");
    }

    [Fact]
    public async Task No_runtime_or_a_failing_one_exits_1()
    {
        var error = new StringWriter();
        (await PythonSelftestCommand.RunAsync(Config(("Python:Directory", "/nonexistent/rw-python")), new StringWriter(), error, Ct)).ShouldBe(1);
        error.ToString().ShouldContain("no Python runtime was found");

        var root = Root();
        var output = new StringWriter();
        error = new StringWriter();
        var code = await PythonSelftestCommand.RunAsync(Config(("Python:Directory", Path.Combine(root, "python", ".venv")),
            ("Python:NltkData", Path.Combine(Path.GetTempPath(), "rw-no-nltk-" + Guid.NewGuid().ToString("N"))), ("Python:Enabled", "false")),
            output, error, Ct);
        code.ShouldBe(1);
        output.ToString().ShouldContain("Python:Enabled is false");
        error.ToString().ShouldContain("Self-test: FAILED");
    }
}
