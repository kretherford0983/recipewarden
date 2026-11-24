using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Python;

namespace RecipeWarden.Infrastructure.Python;

/// <summary>A worker call's answer (<see cref="Result"/> and <see cref="Engine"/>), or why there is none.</summary>
public sealed record WorkerCall<T>(T? Result, WorkerEngine? Engine, WorkerFailure Failure, string? Detail)
    where T : class
{
    public bool Ok => Failure == WorkerFailure.None && Result is not null && Engine is not null;
}

/// <summary>
/// ADR 0051, SEC-91 to SEC-93: runs <c>python3 -I -B -m rw_worker &lt;command&gt;</c> once per request. No shell, a
/// fixed argument list, an allow-listed environment, JSON on stdin and stdout, size and time limits, at most two at a
/// time; on any breach the process tree is killed. Never throws for a worker problem: the caller falls back (BR-119).
/// </summary>
public sealed partial class PythonWorker(PythonOptions options, PythonWorkerHealth health, ILogger<PythonWorker> logger) : IDisposable
{
    private readonly SemaphoreSlim slots = new(PythonOptions.MaxConcurrent, PythonOptions.MaxConcurrent);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));

    public Task<WorkerCall<ParseResult>> ParseAsync(IReadOnlyList<string> lines, bool fdc, CancellationToken cancellationToken) =>
        RunAsync("parse-ingredients", new ParseRequest(lines, fdc), WorkerJson.Default.ParseRequest, WorkerJson.Default.WorkerEnvelopeParseResult,
            Timeout, cancellationToken);

    public Task<WorkerCall<SuggestResult>> SuggestAsync(IReadOnlyList<string> names, CancellationToken cancellationToken) =>
        RunAsync("suggest-foods", new SuggestRequest(names), WorkerJson.Default.SuggestRequest, WorkerJson.Default.WorkerEnvelopeSuggestResult,
            Timeout, cancellationToken);

    public Task<WorkerCall<ScrapeResult>> ScrapeAsync(string html, Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        return RunAsync("scrape-html", new ScrapeRequest(html, url.AbsoluteUri), WorkerJson.Default.ScrapeRequest,
            WorkerJson.Default.WorkerEnvelopeScrapeResult, Timeout, cancellationToken);
    }

    /// <summary>FR-OPS-06, AC-106: the worker's own offline check; the result is kept for the health page.</summary>
    public async Task<WorkerCall<SelftestResult>> SelftestAsync(CancellationToken cancellationToken)
    {
        var call = await RunAsync("selftest", new EmptyRequest(), WorkerJson.Default.EmptyRequest, WorkerJson.Default.WorkerEnvelopeSelftestResult,
            TimeSpan.FromSeconds(Math.Clamp(options.SelftestTimeoutSeconds, 1, 300)), cancellationToken).ConfigureAwait(false);
        if (call.Ok && !call.Result!.Passed)
        {
            call = call with { Failure = WorkerFailure.Unavailable, Detail = "self-test failed: " + Failed(call.Result.Checks) };
        }

        health.RecordSelftest(call);
        return call;
    }

    private static string Failed(SelftestChecks c) =>
        string.Join(", ", new[] { ("network guard", c.NetworkBlocked), ("parse", c.Parse), ("USDA match", c.Fdc), ("scrape", c.Scrape) }
            .Where(x => !x.Item2).Select(x => x.Item1));

    internal async Task<WorkerCall<T>> RunAsync<TRequest, T>(string command, TRequest request, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<WorkerEnvelope<T>> responseType, TimeSpan timeout, CancellationToken cancellationToken)
        where T : class
    {
        var started = Stopwatch.GetTimestamp();
        var call = await RunCoreAsync(command, request, requestType, responseType, timeout, cancellationToken).ConfigureAwait(false);
        health.RecordCall(command, call.Failure, call.Detail);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogCall(logger, command, call.Failure, elapsed);
        return call;
    }

    private async Task<WorkerCall<T>> RunCoreAsync<TRequest, T>(string command, TRequest request, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<WorkerEnvelope<T>> responseType, TimeSpan timeout, CancellationToken cancellationToken)
        where T : class
    {
        if (options.Resolve() is not { } runtime)
        {
            return Fail<T>(WorkerFailure.Unavailable, "no Python runtime was found");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(request, requestType);
        if (payload.Length > PythonOptions.MaxRequestBytes)
        {
            return Fail<T>(WorkerFailure.TooLarge, "request over " + PythonOptions.MaxRequestBytes + " bytes");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await slots.WaitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail<T>(WorkerFailure.Timeout, "no free worker within the time limit");
        }

        byte[] output;
        try
        {
            var (bytes, failure, detail) = await ExecuteAsync(runtime, command, payload, limit.Token, cancellationToken).ConfigureAwait(false);
            if (failure != WorkerFailure.None)
            {
                return Fail<T>(failure, detail);
            }

            output = bytes!;
        }
        finally
        {
            slots.Release();
        }

        WorkerEnvelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(output, responseType);
        }
        catch (JsonException ex)
        {
            return Fail<T>(WorkerFailure.Malformed, "not contract JSON: " + ex.Message);
        }

        if (WorkerContract.CheckEnvelope(envelope) is { } problem)
        {
            return Fail<T>(WorkerFailure.Malformed, problem);
        }

        return envelope!.Ok
            ? new WorkerCall<T>(envelope.Result, envelope.Engine, WorkerFailure.None, null)
            : new WorkerCall<T>(null, envelope.Engine, WorkerFailure.WorkerError, envelope.Error!.Code + ": " + envelope.Error.Message);
    }

    private async Task<(byte[]? Output, WorkerFailure Failure, string? Detail)> ExecuteAsync(
        (string Program, IReadOnlyList<string> Arguments, string NltkData) runtime, string command, byte[] payload,
        CancellationToken limit, CancellationToken caller)
    {
        // SEC-91: no shell, a fixed argument list and only these variables (isolated mode ignores PYTHON* anyway).
        var start = new ProcessStartInfo(runtime.Program)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var argument in runtime.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add(command);
        start.Environment.Clear();
        start.Environment["LANG"] = "C.UTF-8";
        start.Environment["NLTK_DATA"] = runtime.NltkData;
        start.Environment["PYTHONHASHSEED"] = "0";

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return (null, WorkerFailure.Crashed, "the worker did not start");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return (null, WorkerFailure.Unavailable, "the worker could not start: " + ex.Message);
        }

        try
        {
            var stdout = ReadCappedAsync(process.StandardOutput.BaseStream, PythonOptions.MaxResponseBytes, limit);
            var stderr = ReadStderrAsync(process.StandardError, limit);
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(payload, limit).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The worker stopped reading (it failed early); its answer or exit code says why.
            }

            var (bytes, tooLarge) = await stdout.ConfigureAwait(false);
            if (tooLarge)
            {
                return (null, WorkerFailure.TooLarge, "answer over " + PythonOptions.MaxResponseBytes + " bytes");
            }

            await process.WaitForExitAsync(limit).ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            if (errors.Length > 0)
            {
                LogStderr(logger, command, errors);
            }

            return bytes.Length == 0
                ? (null, WorkerFailure.Crashed, "no answer (exit code " + process.ExitCode + ")")
                : (bytes, WorkerFailure.None, null);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            return (null, WorkerFailure.Timeout, "no answer within the time limit");
        }
        finally
        {
            Kill(process);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }

    private static async Task<(byte[] Bytes, bool TooLarge)> ReadCappedAsync(Stream stream, int cap, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > cap)
            {
                return ([], true);
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }

    private static async Task<string> ReadStderrAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var text = new System.Text.StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (text.Length < PythonOptions.MaxStderrChars)
            {
                text.Append(buffer, 0, Math.Min(read, PythonOptions.MaxStderrChars - text.Length));
            }
        }

        return text.ToString();
    }

    private static WorkerCall<T> Fail<T>(WorkerFailure failure, string? detail)
        where T : class => new(null, null, failure, detail);

    public void Dispose() => slots.Dispose();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Python worker {Command}: {Failure} in {Milliseconds:0} ms")]
    private static partial void LogCall(ILogger logger, string command, WorkerFailure failure, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Python worker {Command} wrote to stderr: {Errors}")]
    private static partial void LogStderr(ILogger logger, string command, string errors);
}
