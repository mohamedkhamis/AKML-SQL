using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 030 — Phase 5. Browser-side execute facade over the engine bridge. Uses the ONE canonical
/// <see cref="ISqlConnectionService.SessionId"/> so the engine runs the SQL on the SAME persistent
/// per-session connection that completion/schema use (so #temp/SET/USE state persists). Each execute
/// gets an app-level QueryId GUID so a Cancel can correlate (the bridge's per-frame RequestId is
/// internal and not exposed to callers).
/// </summary>
public interface IQueryExecutionService
{
    /// <summary>Run <paramref name="sql"/>. <paramref name="queryId"/> is echoed back and is the
    /// handle for <see cref="CancelAsync"/>. Always returns a status-bearing result (never null).</summary>
    Task<ExecuteQueryResult> ExecuteAsync(string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct);

    /// <summary>Fire-and-forget cancel for a (possibly queued) execute. No-op offline.</summary>
    Task CancelAsync(string queryId, CancellationToken ct);

    /// <summary>Commit grid edits (parameterized UPDATE/INSERT/DELETE, one transaction). Never null.</summary>
    Task<ApplyChangesResult> ApplyAsync(ApplyChangesRequest request, CancellationToken ct);

    /// <summary>Spec 041 (R21) — check the script's syntax on the server without running it. Sent only to an
    /// engine that advertises <c>execute.v2</c>; never null.</summary>
    Task<ExecuteQueryResult> ParseAsync(string sql, int timeoutSeconds, string queryId, CancellationToken ct) =>
        Task.FromResult(new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Error, ErrorMessage = QueryExecutionService.ParseUnavailable });

    // ── Spec 043 (research R1): on a given query tab's session ───────────────────────────────────────────────
    // A run can outlive a tab switch: the page captures the tab's session before its first await and passes it here, so the
    // request, the cancel, the Apply and the database / transaction state the reply reports all belong to that tab. Default
    // members: a fake that does not implement them runs on the active session, as before tabs.

    Task<ExecuteQueryResult> ExecuteAsync(SqlSessionState session, string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct) =>
        ExecuteAsync(sql, maxRows, timeoutSeconds, queryId, ct);

    Task<ExecuteQueryResult> ParseAsync(SqlSessionState session, string sql, int timeoutSeconds, string queryId, CancellationToken ct) =>
        ParseAsync(sql, timeoutSeconds, queryId, ct);

    Task CancelAsync(SqlSessionState session, string queryId, CancellationToken ct) => CancelAsync(queryId, ct);

    Task<ApplyChangesResult> ApplyAsync(SqlSessionState session, ApplyChangesRequest request, CancellationToken ct) => ApplyAsync(request, ct);

    /// <summary>
    /// Spec 043 (review of US1, gap G1): whether a tab's session holds an open (or an error-aborted) transaction, asked when the tab
    /// closes. A PostgreSQL session knows from every reply; a SQL Server one is asked of the server then, as SSMS asks when a query
    /// window closes — asking after every run would reset <c>@@ROWCOUNT</c> and <c>@@ERROR</c> for the user's next batch. Null when it
    /// cannot tell (no connection, the engine away, no answer).
    /// </summary>
    Task<AkmlSql.Core.TransactionState?> ProbeTransactionStateAsync(SqlSessionState session, CancellationToken ct) =>
        Task.FromResult<AkmlSql.Core.TransactionState?>(
            session.IsConnected && session.Kind == AkmlSql.Core.DatabaseKind.PostgreSql ? session.TransactionState : null);
}

internal sealed class QueryExecutionService : IQueryExecutionService
{
    /// <summary>The capability an engine advertises when it understands Parse and the structured reply.</summary>
    public const string CapabilityExecuteV2 = "execute.v2";

    public const string NoReplyMessage = "No result was received within the timeout; the statement may still be running on the engine.";
    public const string DisconnectedMessage = "The engine connection dropped; the statement may still have run.";
    public const string ParseUnavailable = "Parse needs a newer engine: this one can only run a script, not check it.";

    /// <summary>Added to the reply deadline beyond the script's own timeouts (connect, queueing, transfer).</summary>
    internal static readonly TimeSpan DefaultDeadlineSlack = TimeSpan.FromSeconds(15);

    private readonly IEngineBridge _bridge;
    private readonly ISqlConnectionService _sqlConn;
    private readonly IDiagnosticsRingBuffer _diagnostics;
    private readonly TsqlParserService _parser;

    public QueryExecutionService(IEngineBridge bridge, ISqlConnectionService sqlConn, IDiagnosticsRingBuffer diagnostics, TsqlParserService? parser = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _sqlConn = sqlConn ?? throw new ArgumentNullException(nameof(sqlConn));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _parser = parser ?? new TsqlParserService();
    }

    /// <summary>Tests shorten the slack so a never-answering engine is noticed quickly.</summary>
    internal TimeSpan DeadlineSlack { get; set; } = DefaultDeadlineSlack;

    /// <summary>
    /// How many commands the engine will run for <paramref name="sql"/> — its batches, each as many times as
    /// its <c>GO n</c> says — split by the same parser the engine uses. At least 1.
    /// </summary>
    internal int CountBatchRuns(string sql) => CountBatchRuns(sql, _sqlConn.Kind);

    internal int CountBatchRuns(string sql, AkmlSql.Core.DatabaseKind kind)
    {
        // Spec 042: a PostgreSQL script has no GO. The engine runs each statement under its own timeout, and the browser
        // has no PostgreSQL splitter, so it counts semicolons: never fewer than the statements, which only lengthens the
        // wait (capped at the script ceiling), and a wait that is too short would report a run that is still going as lost.
        if (kind == AkmlSql.Core.DatabaseKind.PostgreSql)
            return Math.Max(1, sql.Count(c => c == ';') + 1);

        var split = _parser.SplitBatches(sql);
        if (split.SeparatorError != null || split.Batches.Count == 0) return 1;
        long runs = split.Batches.Sum(b => (long)Math.Min(Math.Max(1, b.RepeatCount), 10_000));
        return (int)Math.Min(int.MaxValue, Math.Max(1, runs));
    }

    /// <summary>
    /// Spec 041 (R22) — how long to wait for the reply: the script's time limit, which the engine enforces
    /// on the whole run (start to last row, <see cref="ExecuteLimits.ScriptTimeLimit"/>) and then replies,
    /// plus slack for connecting, queueing and the transfer. Running out of it means the engine did not
    /// answer (an older engine without the limit, or a lost reply), so it says the statement "may still be
    /// running" rather than that it failed.
    /// </summary>
    internal TimeSpan ReplyDeadline(string sql, int timeoutSeconds) => ReplyDeadline(sql, timeoutSeconds, _sqlConn.Kind);

    internal TimeSpan ReplyDeadline(string sql, int timeoutSeconds, AkmlSql.Core.DatabaseKind kind) =>
        ExecuteLimits.ScriptTimeLimit(timeoutSeconds, CountBatchRuns(sql, kind)) + DeadlineSlack;

    public Task<ExecuteQueryResult> ExecuteAsync(string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct) =>
        SendScriptAsync(null, MessageTypes.ExecuteQuery, sql, maxRows, timeoutSeconds, queryId, ct);

    public Task<ExecuteQueryResult> ExecuteAsync(SqlSessionState session, string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct) =>
        SendScriptAsync(session ?? throw new ArgumentNullException(nameof(session)), MessageTypes.ExecuteQuery, sql, maxRows, timeoutSeconds, queryId, ct);

    public Task<ExecuteQueryResult> ParseAsync(string sql, int timeoutSeconds, string queryId, CancellationToken ct) =>
        ParseOnAsync(null, sql, timeoutSeconds, queryId, ct);

    /// <summary>The probe a SQL Server session answers when its tab closes: XACT_STATE() is 1 for an open transaction, -1 for one
    /// an error left uncommittable, 0 for none.</summary>
    internal const string TransactionProbeSql = "SELECT XACT_STATE() AS xact_state;";

    public async Task<AkmlSql.Core.TransactionState?> ProbeTransactionStateAsync(SqlSessionState session, CancellationToken ct)
    {
        if (session == null || !session.IsConnected) return null;
        if (session.Kind == AkmlSql.Core.DatabaseKind.PostgreSql) return session.TransactionState;
        if (_bridge.State != BridgeState.Open) return null;
        var result = await SendScriptAsync(session, MessageTypes.ExecuteQuery, TransactionProbeSql, 1, 5, Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
        if (result.Status != ExecuteStatus.Ok || result.ResultSets.Length == 0 || result.ResultSets[0].Rows.Length == 0) return null;
        var row = result.ResultSets[0].Rows[0];
        return (row.Length > 0 ? row[0]?.Trim() : null) switch
        {
            "1" => AkmlSql.Core.TransactionState.Open,
            "-1" => AkmlSql.Core.TransactionState.Aborted,
            "0" => AkmlSql.Core.TransactionState.None,
            _ => null,
        };
    }

    public Task<ExecuteQueryResult> ParseAsync(SqlSessionState session, string sql, int timeoutSeconds, string queryId, CancellationToken ct) =>
        ParseOnAsync(session ?? throw new ArgumentNullException(nameof(session)), sql, timeoutSeconds, queryId, ct);

    private Task<ExecuteQueryResult> ParseOnAsync(SqlSessionState? session, string sql, int timeoutSeconds, string queryId, CancellationToken ct)
    {
        if (Array.IndexOf(_bridge.EngineCapabilities ?? Array.Empty<string>(), CapabilityExecuteV2) < 0)
        {
            // An older engine has no Parse: an unknown request type gets no reply at all.
            return Task.FromResult(new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Error, ErrorMessage = ParseUnavailable });
        }
        return SendScriptAsync(session, MessageTypes.ExecuteParse, sql, 1, timeoutSeconds, queryId, ct);
    }

    /// <summary>
    /// <paramref name="session"/> null: the active session, read through the connection service (a caller from before
    /// tabs). Otherwise everything — the id sent, the kind that decides the deadline, the reports of the reply — is that
    /// session's, read once here, before the first await.
    /// </summary>
    private async Task<ExecuteQueryResult> SendScriptAsync(SqlSessionState? session, int messageType, string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct)
    {
        bool connected = session?.IsConnected ?? _sqlConn.IsConnected;
        string sessionId = session?.SessionId ?? _sqlConn.SessionId;
        var kind = session?.Kind ?? _sqlConn.Kind;
        if (_bridge.State != BridgeState.Open || !connected)
        {
            return new ExecuteQueryResult
            {
                QueryId = queryId,
                Status = ExecuteStatus.NoConnection,
                ErrorMessage = "Connect to a database first (the engine must be paired and a database connected).",
            };
        }

        using var deadline = new CancellationTokenSource(ReplyDeadline(sql ?? string.Empty, timeoutSeconds, kind));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            var req = new ExecuteQueryRequest
            {
                SessionId = sessionId,
                Sql = sql ?? string.Empty,
                MaxRows = maxRows,
                CommandTimeoutSeconds = timeoutSeconds,
                QueryId = queryId,
                IncludeProvenance = true,
            };
            var result = await _bridge.SendAsync<ExecuteQueryRequest, ExecuteQueryResult>(
                messageType, req, linked.Token).ConfigureAwait(false);

            // Spec 041 (R27): a script's USE moves the session; the toolbar's database selector follows.
            if (result != null)
            {
                if (session != null) _sqlConn.ReportCurrentDatabase(session, result.CurrentDatabase);
                else _sqlConn.ReportCurrentDatabase(result.CurrentDatabase);
            }

            // Spec 042 (FR-016): a PostgreSQL run ends in a transaction state the status bar shows. A reply from the engine is
            // the only thing that changes it; a dropped bridge says nothing about the server's transaction, so it is left alone.
            // SQL Server replies always say None, so only a PostgreSQL session reports.
            if (result != null && kind == AkmlSql.Core.DatabaseKind.PostgreSql)
            {
                if (session != null) _sqlConn.ReportTransactionState(session, (AkmlSql.Core.TransactionState)result.TransactionState);
                else _sqlConn.ReportTransactionState((AkmlSql.Core.TransactionState)result.TransactionState);
            }

            // The bridge returns default! for an empty payload; defend so callers always get a result.
            return result ?? new ExecuteQueryResult
            {
                QueryId = queryId,
                Status = ExecuteStatus.Error,
                ErrorMessage = "The engine returned an empty execute response.",
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Cancelled, ErrorMessage = "Cancelled." };
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "execute", "ExecuteQuery: no reply before the deadline.");
            return new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.NoReply, ErrorMessage = NoReplyMessage };
        }
        catch (OperationCanceledException)
        {
            return new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Cancelled, ErrorMessage = "Cancelled." };
        }
        catch (InvalidOperationException ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "execute", $"ExecuteQuery: the bridge dropped: {ex.Message}");
            return new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Disconnected, ErrorMessage = DisconnectedMessage };
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "execute", $"ExecuteQuery failed: {ex.Message}");
            return new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Error, ErrorMessage = "Could not reach the engine: " + ex.Message };
        }
    }

    public Task CancelAsync(string queryId, CancellationToken ct) => CancelOnAsync(_sqlConn.SessionId, queryId, ct);

    public Task CancelAsync(SqlSessionState session, string queryId, CancellationToken ct) =>
        CancelOnAsync((session ?? throw new ArgumentNullException(nameof(session))).SessionId, queryId, ct);

    private async Task CancelOnAsync(string sessionId, string queryId, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open || string.IsNullOrEmpty(queryId)) return;
        try
        {
            await _bridge.SendNotificationAsync(
                MessageTypes.ExecuteCancel,
                new ExecuteCancelRequest { SessionId = sessionId, QueryId = queryId },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Trace, "execute", $"ExecuteCancel send failed: {ex.Message}");
        }
    }

    public Task<ApplyChangesResult> ApplyAsync(ApplyChangesRequest request, CancellationToken ct) =>
        ApplyOnAsync(_sqlConn.IsConnected, _sqlConn.SessionId, request, ct);

    /// <summary>Spec 043: grid edits are written on the connection of the tab whose results they are.</summary>
    public Task<ApplyChangesResult> ApplyAsync(SqlSessionState session, ApplyChangesRequest request, CancellationToken ct) =>
        ApplyOnAsync((session ?? throw new ArgumentNullException(nameof(session))).IsConnected, session.SessionId, request, ct);

    private async Task<ApplyChangesResult> ApplyOnAsync(bool connected, string sessionId, ApplyChangesRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open || !connected)
        {
            return new ApplyChangesResult { Status = ExecuteStatus.NoConnection, ErrorMessage = "Not connected." };
        }

        try
        {
            request.SessionId = sessionId; // always the tab's own session.
            var result = await _bridge.SendAsync<ApplyChangesRequest, ApplyChangesResult>(
                MessageTypes.ApplyChanges, request, ct).ConfigureAwait(false);
            return result ?? new ApplyChangesResult { Status = ExecuteStatus.Error, ErrorMessage = "Empty apply response." };
        }
        catch (OperationCanceledException)
        {
            return new ApplyChangesResult { Status = ExecuteStatus.Cancelled, ErrorMessage = "Cancelled." };
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "execute", $"ApplyChanges failed: {ex.Message}");
            return new ApplyChangesResult { Status = ExecuteStatus.Error, ErrorMessage = "Could not reach the engine: " + ex.Message };
        }
    }
}
