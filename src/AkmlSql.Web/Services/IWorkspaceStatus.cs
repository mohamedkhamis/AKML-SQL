using System;
using System.Linq;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>Where the last (or current) execution stands.</summary>
public enum ExecutionPhase
{
    Idle,
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// Spec 041 (research R41, X8) — the one place the editor page reports what is happening, read by the
/// results pane's running indicator and the status bar's segments (FR-027, FR-083): the execution phase
/// and outcome, rows and time, the caret, and the document's name and modified state. Replaces the
/// editor toolbar's free-text status line for execution.
/// </summary>
public interface IWorkspaceStatus
{
    ExecutionPhase Phase { get; }

    /// <summary>When the current or last execution began; null before the first.</summary>
    DateTimeOffset? StartedAt { get; }

    /// <summary>While running: time since <see cref="StartedAt"/>, counting up. Afterwards: the
    /// engine's elapsed time (or the time to failure).</summary>
    long ElapsedMs { get; }

    /// <summary>Rows returned across every result set, or rows affected when there was no set.</summary>
    long RowCount { get; }

    /// <summary>The status-bar outcome text: "Executing query…", "Query executed successfully." or
    /// "Query completed with errors."; null before the first execution.</summary>
    string? Outcome { get; }

    string? ErrorMessage { get; }

    int CaretLine { get; }

    int CaretColumn { get; }

    string DocumentName { get; }

    bool IsModified { get; }

    event Action? Changed;

    void Begin();

    void Complete(ExecuteQueryResult result);

    /// <summary>The execution ended without a result (the engine connection dropped, no reply).</summary>
    void Fail(string message);

    void SetCaret(int line, int column);

    void SetDocument(string name, bool modified);
}

/// <summary>The app's <see cref="IWorkspaceStatus"/> (one per browser tab).</summary>
public sealed class WorkspaceStatus : IWorkspaceStatus
{
    public const string RunningText = "Executing query…";
    public const string SucceededText = "Query executed successfully.";
    public const string FailedText = "Query completed with errors.";

    private readonly Func<DateTimeOffset> _clock;
    private long _elapsedMs;

    public WorkspaceStatus() : this(() => DateTimeOffset.UtcNow) { }

    /// <summary>Tests pass a clock so elapsed time is deterministic.</summary>
    public WorkspaceStatus(Func<DateTimeOffset> clock) => _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public event Action? Changed;

    public ExecutionPhase Phase { get; private set; } = ExecutionPhase.Idle;

    public DateTimeOffset? StartedAt { get; private set; }

    public long ElapsedMs => Phase == ExecutionPhase.Running && StartedAt.HasValue
        ? (long)Math.Max(0, (_clock() - StartedAt.Value).TotalMilliseconds)
        : _elapsedMs;

    public long RowCount { get; private set; }

    public string? Outcome => Phase switch
    {
        ExecutionPhase.Running => RunningText,
        ExecutionPhase.Succeeded => SucceededText,
        ExecutionPhase.Failed => FailedText,
        _ => null,
    };

    public string? ErrorMessage { get; private set; }

    public int CaretLine { get; private set; } = 1;

    public int CaretColumn { get; private set; } = 1;

    public string DocumentName { get; private set; } = string.Empty;

    public bool IsModified { get; private set; }

    public void Begin()
    {
        Phase = ExecutionPhase.Running;
        StartedAt = _clock();
        _elapsedMs = 0;
        RowCount = 0;
        ErrorMessage = null;
        Changed?.Invoke();
    }

    public void Complete(ExecuteQueryResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        var sets = result.ResultSets ?? Array.Empty<ExecuteResultSet>();
        RowCount = sets.Length > 0
            ? sets.Sum(s => (long)(s.Rows?.Length ?? 0))
            : Math.Max(0, result.TotalRowsAffected);
        _elapsedMs = result.ElapsedMs;
        if (result.Status == ExecuteStatus.Ok)
        {
            Phase = ExecutionPhase.Succeeded;
            ErrorMessage = null;
        }
        else
        {
            Phase = ExecutionPhase.Failed;
            ErrorMessage = result.ErrorMessage;
        }
        Changed?.Invoke();
    }

    public void Fail(string message)
    {
        _elapsedMs = ElapsedMs;
        Phase = ExecutionPhase.Failed;
        ErrorMessage = message;
        Changed?.Invoke();
    }

    public void SetCaret(int line, int column)
    {
        if (line == CaretLine && column == CaretColumn) return;
        CaretLine = line;
        CaretColumn = column;
        Changed?.Invoke();
    }

    public void SetDocument(string name, bool modified)
    {
        name ??= string.Empty;
        if (name == DocumentName && modified == IsModified) return;
        DocumentName = name;
        IsModified = modified;
        Changed?.Invoke();
    }
}
