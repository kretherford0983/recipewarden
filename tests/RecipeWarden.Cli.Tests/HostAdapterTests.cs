using System.Net;
using System.Net.Sockets;
using RecipeWarden.Cli.Admin;
using RecipeWarden.Cli.ApplyConfig;

namespace RecipeWarden.Cli.Tests;

[Trait("Req", "FR-BKP-09")]
public sealed class HostAdapterTests
{
    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    [Fact]
    public async Task Health_probe_succeeds_when_health_says_ok()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var serve = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            var body = "ok"u8.ToArray();
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        }, TestContext.Current.CancellationToken);

        var probe = new HttpHealthProbe(new Uri($"http://127.0.0.1:{port}/health"), TimeProvider.System);
        (await probe.WaitHealthyAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).ShouldBeTrue();
        await serve;
    }

    [Fact]
    public async Task Health_probe_gives_up_after_the_timeout()
    {
        var probe = new HttpHealthProbe(new Uri($"http://127.0.0.1:{FreePort()}/health"), TimeProvider.System);
        (await probe.WaitHealthyAsync(TimeSpan.FromMilliseconds(1500), TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Systemctl_reports_exit_codes_and_missing_binaries()
    {
        (await new Systemctl("true").DaemonReloadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await new Systemctl("false").RestartAsync("recipewarden.service", TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await new Systemctl("/nonexistent/systemctl").DaemonReloadAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "FR-INST-04")]
    public void Password_can_be_piped_on_stdin()
    {
        var original = Console.In;
        try
        {
            Console.SetIn(new StringReader("correct horse battery\nignored\n"));
            AdminCreateCommand.ReadPassword(fromStdin: true).ShouldBe("correct horse battery");
            Console.SetIn(new StringReader(string.Empty));
            AdminCreateCommand.ReadPassword(fromStdin: true).ShouldBe(string.Empty);
        }
        finally
        {
            Console.SetIn(original);
        }
    }
}
