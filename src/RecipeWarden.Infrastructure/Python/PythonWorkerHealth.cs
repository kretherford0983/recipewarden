using RecipeWarden.Core.Python;

namespace RecipeWarden.Infrastructure.Python;

/// <summary>FR-OPS-06: the worker's state for the health page.</summary>
public enum PythonStatus
{
    /// <summary>No self-test has run in this process yet.</summary>
    Unknown,
    Ok,
    Unavailable,
    TurnedOff,
}

public sealed record PythonHealthSnapshot(PythonStatus Status, DateTimeOffset? CheckedAt, string? Engine, string? Detail,
    SelftestChecks? Checks, DateTimeOffset? LastCallAt, string? LastCommand, WorkerFailure? LastFailure, string? LastDetail);

/// <summary>FR-OPS-06: the last self-test and the last call, kept in memory (a restart runs the self-test again).</summary>
public sealed class PythonWorkerHealth(TimeProvider clock)
{
    private readonly Lock gate = new();
    private PythonHealthSnapshot state = new(PythonStatus.Unknown, null, null, null, null, null, null, null, null);

    public PythonHealthSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public void RecordSelftest(WorkerCall<SelftestResult> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        lock (gate)
        {
            state = state with
            {
                Status = call.Ok ? PythonStatus.Ok : PythonStatus.Unavailable,
                CheckedAt = clock.GetUtcNow(),
                Engine = call.Engine?.Describe(),
                Detail = call.Ok ? null : call.Detail,
                Checks = call.Result?.Checks,
            };
        }
    }

    public void RecordCall(string command, WorkerFailure failure, string? detail)
    {
        lock (gate)
        {
            state = state with { LastCallAt = clock.GetUtcNow(), LastCommand = command, LastFailure = failure, LastDetail = detail };
        }
    }
}
