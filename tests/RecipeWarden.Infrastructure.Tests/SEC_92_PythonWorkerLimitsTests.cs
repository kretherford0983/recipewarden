using RecipeWarden.Core.Python;
using RecipeWarden.Infrastructure.Python;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// SEC-91 to SEC-93, BR-119, AC-105: the worker runner's limits, against fake workers that misbehave on purpose.
/// Every failure gives a fallback result and leaves no process behind.
/// </summary>
[Trait("Req", "SEC-92")]
public sealed class SEC_92_PythonWorkerLimitsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int ReadPid(FakeWorker fake, string name) => int.Parse(File.ReadAllText(Path.Combine(fake.Work, name)), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    [Trait("Req", "AC-105")]
    public async Task A_late_worker_and_its_children_are_killed_and_the_call_times_out()
    {
        var fake = PythonTestKit.Fake("""
            child = subprocess.Popen(["/bin/sleep", "60"])
            open(os.path.join(work, "pid"), "w").write(str(os.getpid()))
            open(os.path.join(work, "child"), "w").write(str(child.pid))
            time.sleep(60)
            """);
        var (worker, _) = PythonTestKit.Worker(fake.Options(timeoutSeconds: 2));

        var call = await worker.ParseAsync(["1 cup milk"], fdc: false, Ct);

        call.Failure.ShouldBe(WorkerFailure.Timeout);
        PythonTestKit.ProcessExists(ReadPid(fake, "pid")).ShouldBeFalse();
        PythonTestKit.ProcessExists(ReadPid(fake, "child")).ShouldBeFalse();
    }

    [Theory]
    [Trait("Req", "AC-105")]
    [Trait("Req", "SEC-93")]
    [InlineData("sys.stdout.write('x' * (3 * 1024 * 1024))", WorkerFailure.TooLarge)]
    [InlineData("sys.stdout.write('{not json')", WorkerFailure.Malformed)]
    [InlineData("answer({'lines': [], 'surprise': 1})", WorkerFailure.Malformed)]
    [InlineData("sys.stdout.write(json.dumps({'v': 2, 'ok': True, 'engine': ENGINE, 'result': {'lines': []}}))", WorkerFailure.Malformed)]
    [InlineData("sys.stdout.write(json.dumps({'v': 1, 'ok': False, 'engine': ENGINE, 'error': {'code': 'internal', 'message': 'boom'}}))", WorkerFailure.WorkerError)]
    [InlineData("sys.exit(3)", WorkerFailure.Crashed)]
    [InlineData("os.kill(os.getpid(), 9)", WorkerFailure.Crashed)]
    public async Task Oversized_malformed_failed_or_silent_answers_fall_back(string body, WorkerFailure expected)
    {
        var (worker, health) = PythonTestKit.Worker(PythonTestKit.Fake(body).Options());
        var call = await worker.ParseAsync(["1 cup milk"], fdc: false, Ct);
        call.Failure.ShouldBe(expected, call.Detail);
        call.Result.ShouldBeNull();
        health.Snapshot.LastFailure.ShouldBe(expected);
    }

    [Fact]
    [Trait("Req", "SEC-91")]
    public async Task The_worker_gets_a_fixed_argument_list_and_only_the_allowed_environment()
    {
        var fake = PythonTestKit.Fake("""
            json.dump({"argv": sys.argv[1:], "env": dict(os.environ), "cwd": os.getcwd(), "request": json.loads(request)},
                      open(os.path.join(work, "seen.json"), "w"))
            answer({"suggestions": [{"name": "x", "fdc": []}]})
            """);
        Environment.SetEnvironmentVariable("RW_SECRET_FOR_SEC_91", "leak");
        var (worker, _) = PythonTestKit.Worker(fake.Options());

        (await worker.SuggestAsync(["x"], Ct)).Ok.ShouldBeTrue();

        using var seen = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fake.Work, "seen.json"), Ct));
        seen.RootElement.GetProperty("argv").EnumerateArray().Select(a => a.GetString()).ShouldBe(["suggest-foods"]);
        var env = seen.RootElement.GetProperty("env").EnumerateObject().Select(p => p.Name).Where(n => n != "LC_CTYPE").Order().ToList();
        env.ShouldBe(["LANG", "NLTK_DATA", "PYTHONHASHSEED"]);
        seen.RootElement.GetProperty("request").GetProperty("names")[0].GetString().ShouldBe("x");
    }

    [Fact]
    public async Task At_most_two_workers_run_at_once()
    {
        var fake = PythonTestKit.Fake("""
            mine = os.path.join(work, "run-" + str(os.getpid()))
            os.mkdir(mine)
            running = len([d for d in os.listdir(work) if d.startswith("run-")])
            with open(os.path.join(work, "counts"), "a") as f:
                f.write(str(running) + "\n")
            time.sleep(0.4)
            os.rmdir(mine)
            answer({"lines": [None]})
            """);
        var (worker, _) = PythonTestKit.Worker(fake.Options());

        var calls = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => worker.ParseAsync([""], fdc: false, Ct)));

        calls.ShouldAllBe(c => c.Ok);
        var counts = (await File.ReadAllLinesAsync(Path.Combine(fake.Work, "counts"), Ct)).Select(int.Parse).ToList();
        counts.Count.ShouldBe(5);
        counts.Max().ShouldBeLessThanOrEqualTo(PythonOptions.MaxConcurrent);
    }

    [Fact]
    public async Task An_oversized_request_is_never_sent()
    {
        var fake = PythonTestKit.Fake("open(os.path.join(work, 'ran'), 'w').write('1')\nanswer({'lines': []})");
        var (worker, _) = PythonTestKit.Worker(fake.Options());
        var html = new string('a', PythonOptions.MaxRequestBytes);

        (await worker.ScrapeAsync(html, new Uri("https://example.com/"), Ct)).Failure.ShouldBe(WorkerFailure.TooLarge);
        File.Exists(Path.Combine(fake.Work, "ran")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_runtime_is_unavailable()
    {
        var (worker, _) = PythonTestKit.Worker(new PythonOptions { Directory = "/nonexistent/rw-python" });
        (await worker.ParseAsync(["x"], false, Ct)).Failure.ShouldBe(WorkerFailure.Unavailable);
        var (missing, _) = PythonTestKit.Worker(new PythonOptions { Executable = "/nonexistent/rw-python/worker" });
        (await missing.ParseAsync(["x"], false, Ct)).Failure.ShouldBe(WorkerFailure.Unavailable);
    }

    [Fact]
    [Trait("Req", "AC-105")]
    [Trait("Req", "BR-119")]
    public async Task An_adapter_falls_back_with_one_warning_and_contract_breaks_count_as_failures()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var fake = PythonTestKit.Fake("answer({'suggestions': [{'name': 'someone else', 'fdc': []}]})");
        var (worker, health) = PythonTestKit.Worker(fake.Options());
        var log = new ListLogger<PythonFeatures>();
        var source = new PythonFoodSuggestionSource(PythonTestKit.Features(fake.Options(), health, new SettingsStore(s.Db), log), worker);

        var outcome = await source.SuggestAsync(["brown sugar"], Ct);

        outcome.Ok.ShouldBeFalse();
        outcome.Failure.ShouldBe(WorkerFailure.Malformed);
        log.Warnings.Count.ShouldBe(1);
        log.Warnings[0].ShouldContain("food suggestions used the .NET behaviour");
    }

    [Fact]
    [Trait("Req", "FR-OPS-06")]
    [Trait("Req", "AC-103")]
    public async Task Turned_off_by_an_admin_or_the_server_owner_nothing_runs_and_nothing_is_logged()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var fake = PythonTestKit.Fake("open(os.path.join(work, 'ran'), 'w').write('1')\nanswer({'lines': [None]})");
        var (worker, health) = PythonTestKit.Worker(fake.Options());
        var log = new ListLogger<PythonFeatures>();
        var features = PythonTestKit.Features(fake.Options(), health, new SettingsStore(s.Db), log);
        await features.SetAdminEnabledAsync(false, Ct);

        var outcome = await new PythonIngredientSecondOpinion(features, worker).ReadAsync([""], Ct);

        outcome.Failure.ShouldBe(WorkerFailure.TurnedOff);
        (await features.StatusAsync(Ct)).Status.ShouldBe(PythonStatus.TurnedOff);
        File.Exists(Path.Combine(fake.Work, "ran")).ShouldBeFalse();
        log.Warnings.ShouldBeEmpty();

        await features.SetAdminEnabledAsync(true, Ct);
        (await new PythonIngredientSecondOpinion(features, worker).ReadAsync([""], Ct)).Ok.ShouldBeTrue();
        var serverOff = fake.Options();
        serverOff.Enabled = false;
        (await PythonTestKit.Features(serverOff, health, new SettingsStore(s.Db)).GateAsync(Ct)).ShouldBe(WorkerFailure.TurnedOff);
    }

    [Fact]
    [Trait("Req", "FR-OPS-06")]
    public async Task A_failed_self_test_marks_the_worker_unavailable_and_later_calls_skip_it()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var fake = PythonTestKit.Fake("""
            if command == "selftest":
                answer({"passed": False, "checks": {"network_blocked": True, "parse_ms": 1, "parse": False, "fdc": False, "scrape_ms": 1, "scrape": True}})
            else:
                open(os.path.join(work, "ran"), "w").write("1")
            """);
        var (worker, health) = PythonTestKit.Worker(fake.Options());

        var selftest = await worker.SelftestAsync(Ct);

        selftest.Failure.ShouldBe(WorkerFailure.Unavailable);
        selftest.Detail.ShouldBe("self-test failed: parse, USDA match");
        health.Snapshot.Status.ShouldBe(PythonStatus.Unavailable);
        var features = PythonTestKit.Features(fake.Options(), health, new SettingsStore(s.Db));
        (await new PythonSiteRecipeScraper(features, worker).ScrapeAsync("<html></html>", new Uri("https://example.com/"), Ct))
            .Failure.ShouldBe(WorkerFailure.Unavailable);
        File.Exists(Path.Combine(fake.Work, "ran")).ShouldBeFalse();
    }
}
