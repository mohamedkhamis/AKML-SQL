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

    /// <summary>The user cancelled the run (<see cref="ExecuteStatus.Cancelled"/>): not a failure of the script.</summary>
    Cancelled,
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

    /// <summary>Spec 043: the status clock's now; a query tab's running clock counts from its own start against it.</summary>
    DateTimeOffset Now => DateTimeOffset.UtcNow;

    /// <summary>Rows returned across every result set, or rows affected when there was no set.</summary>
    long RowCount { get; }

    /// <summary>The status-bar outcome text: "Executing query…", "Query executed successfully.",
    /// "Query completed with errors." or "Query was cancelled by user."; null before the first execution.</summary>
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

    /// <summary>
    /// The last result the editor page showed and what produced it. The page is created again on every visit (Editor →
    /// History → Editor), so it keeps the result here and shows it again instead of an empty results pane; null before the first.
    /// </summary>
    WorkspaceResult? LastResult { get; }

    void KeepResult(WorkspaceResult result);

    /// <summary>
    /// Forget the last execution's outcome (phase, rows, time) - the connection changed, so "196 rows · 28 ms" and "Query executed
    /// successfully." describe another server. The kept result stays: the page shows it as from an earlier connection.
    /// </summary>
    void ClearOutcome();

    /// <summary>Spec 043 (T036): the run as shown now — phase, start, time, rows, error — for a query tab to keep.</summary>
    RunSnapshot Snapshot();

    /// <summary>Spec 043 (T036): show a query tab's run: the active tab's, on activation and whenever its run changes.</summary>
    void Show(RunSnapshot run);

    /// <summary>
    /// What the last editor action said ("Formatted.", "Applied: Expand INSERT columns."); null when there is none. The status
    /// bar shows it for <see cref="WorkspaceStatus.NoticeLifetime"/> after <see cref="NoticeAt"/>. It used to sit at the end of the
    /// editor toolbar, where a long message pushed the toolbar onto a second row and the editor down with it.
    /// </summary>
    string? Notice => null;

    /// <summary>When <see cref="Notice"/> was set; null when there is none.</summary>
    DateTimeOffset? NoticeAt => null;

    /// <summary>Say what an editor action did; null or blank clears it.</summary>
    void SetNotice(string? text) { }
}

/// <summary>
/// Spec 043 (T036) — one query tab's run as the status bar shows it. Every tab keeps its own; the status bar shows the active
/// tab's, so a switch shows that tab's outcome and rows (FR-005) and a run finishing in another tab changes nothing on screen.
/// </summary>
/// <param name="ElapsedMs">The run's time once it ended (while running, the status counts up from <paramref name="StartedAt"/>).</param>
/// <param name="Outcome">Spec 047 (WEB-005): words that replace the phase's ("Parse failed: 1 syntax error."); null for a run.</param>
public sealed record RunSnapshot(ExecutionPhase Phase, DateTimeOffset? StartedAt, long ElapsedMs, long RowCount, string? ErrorMessage,
    string? Outcome = null)
{
    /// <summary>No run yet (or its outcome was cleared).</summary>
    public static RunSnapshot Idle { get; } = new(ExecutionPhase.Idle, null, 0, 0, null);

    /// <summary>A run that began at <paramref name="startedAt"/>.</summary>
    public static RunSnapshot Running(DateTimeOffset startedAt) => new(ExecutionPhase.Running, startedAt, 0, 0, null);

    /// <summary>A run's reply: rows returned across every set (rows affected when there was none), its time and its error.</summary>
    public static RunSnapshot Of(ExecuteQueryResult result, DateTimeOffset? startedAt = null)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        var sets = result.ResultSets ?? Array.Empty<ExecuteResultSet>();
        long rows = sets.Length > 0 ? sets.Sum(s => (long)(s.Rows?.Length ?? 0)) : Math.Max(0, result.TotalRowsAffected);
        // A cancel is the user's doing, not an error of the script (UI scan 2026-11-03, W2): it is reported as such.
        var phase = result.Status switch
        {
            ExecuteStatus.Ok => ExecutionPhase.Succeeded,
            ExecuteStatus.Cancelled => ExecutionPhase.Cancelled,
            _ => ExecutionPhase.Failed,
        };
        return new(phase, startedAt, result.ElapsedMs, rows, phase == ExecutionPhase.Succeeded ? null : result.ErrorMessage);
    }
}

/// <summary>A result as the editor page showed it: the reply, the connection generation it was read under, where the executed text
/// began in the document (errors map their lines from it) and the text that ran. The open results tab is kept by the layout store.</summary>
public sealed record WorkspaceResult(ExecuteQueryResult Result, int ConnectionGeneration, int StartLine, int StartColumn, string Sql);

/// <summary>The app's <see cref="IWorkspaceStatus"/> (one per browser tab).</summary>
public sealed class WorkspaceStatus : IWorkspaceStatus
{
    public const string RunningText = "Executing query…";
    public const string SucceededText = "Query executed successfully.";
    public const string FailedText = "Query completed with errors.";

    /// <summary>SSMS's words for a run the user cancelled; the Messages tab says the same (MessagesView).</summary>
    public const string CancelledText = "Query was cancelled by user.";

    /// <summary>Spec 047 (WEB-005): what Parse found, said in its own words (a run keeps <see cref="FailedText"/>).</summary>
    public const string ParseFailedOne = "Parse failed: 1 syntax error.";
    public const string ParseFailedMany = "Parse failed: {0} syntax errors.";
    public const string ParseSucceeded = "Parse succeeded.";

    private readonly Func<DateTimeOffset> _clock;
    private long _elapsedMs;

    public WorkspaceStatus() : this(() => DateTimeOffset.UtcNow) { }

    /// <summary>Tests pass a clock so elapsed time is deterministic.</summary>
    public WorkspaceStatus(Func<DateTimeOffset> clock) => _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public DateTimeOffset Now => _clock();

    public event Action? Changed;

    public ExecutionPhase Phase { get; private set; } = ExecutionPhase.Idle;

    public DateTimeOffset? StartedAt { get; private set; }

    public long ElapsedMs => Phase == ExecutionPhase.Running && StartedAt.HasValue
        ? (long)Math.Max(0, (_clock() - StartedAt.Value).TotalMilliseconds)
        : _elapsedMs;

    public long RowCount { get; private set; }

    // Spec 047 (WEB-005): the words of a finished Parse; null for every run.
    private string? _outcome;

    public string? Outcome => Phase == ExecutionPhase.Running ? RunningText : _outcome ?? Phase switch
    {
        ExecutionPhase.Succeeded => SucceededText,
        ExecutionPhase.Failed => FailedText,
        ExecutionPhase.Cancelled => CancelledText,
        _ => null,
    };

    public string? ErrorMessage { get; private set; }

    public int CaretLine { get; private set; } = 1;

    public int CaretColumn { get; private set; } = 1;

    public string DocumentName { get; private set; } = string.Empty;

    public bool IsModified { get; private set; }

    /// <summary>How long the status bar shows an action's <see cref="Notice"/>.</summary>
    public static readonly TimeSpan NoticeLifetime = TimeSpan.FromSeconds(12);

    public string? Notice { get; private set; }

    public DateTimeOffset? NoticeAt { get; private set; }

    public void SetNotice(string? text)
    {
        Notice = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        NoticeAt = Notice == null ? null : _clock();
        Changed?.Invoke();
    }

    public void Begin()
    {
        Phase = ExecutionPhase.Running;
        StartedAt = _clock();
        _elapsedMs = 0;
        RowCount = 0;
        ErrorMessage = null;
        _outcome = null;
        // A run's outcome takes over the status bar; "Formatted." from before it would describe an older moment.
        Notice = null;
        NoticeAt = null;
        Changed?.Invoke();
    }

    public void Complete(ExecuteQueryResult result)
    {
        var run = RunSnapshot.Of(result, StartedAt);
        RowCount = run.RowCount;
        _elapsedMs = run.ElapsedMs;
        Phase = run.Phase;
        ErrorMessage = run.ErrorMessage;
        _outcome = null;
        Changed?.Invoke();
    }

    public RunSnapshot Snapshot() => new(Phase, StartedAt, Phase == ExecutionPhase.Running ? 0 : _elapsedMs, RowCount, ErrorMessage, _outcome);

    public void Show(RunSnapshot run)
    {
        run ??= RunSnapshot.Idle;
        Phase = run.Phase;
        StartedAt = run.StartedAt;
        _elapsedMs = run.ElapsedMs;
        RowCount = run.RowCount;
        ErrorMessage = run.ErrorMessage;
        _outcome = run.Outcome;
        Changed?.Invoke();
    }

    public void Fail(string message)
    {
        _elapsedMs = ElapsedMs;
        Phase = ExecutionPhase.Failed;
        ErrorMessage = message;
        _outcome = null;
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

    public WorkspaceResult? LastResult { get; private set; }

    public void KeepResult(WorkspaceResult result) => LastResult = result ?? throw new ArgumentNullException(nameof(result));

    public void ClearOutcome()
    {
        if (Phase is ExecutionPhase.Idle or ExecutionPhase.Running) return;      // a run in flight reports its own outcome
        Phase = ExecutionPhase.Idle;
        StartedAt = null;
        _elapsedMs = 0;
        RowCount = 0;
        ErrorMessage = null;
        _outcome = null;
        Changed?.Invoke();
    }
}
