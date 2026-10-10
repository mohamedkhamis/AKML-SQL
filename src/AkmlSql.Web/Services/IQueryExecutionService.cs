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
    internal int CountBatchRuns(string sql)
    {
        var split = _parser.SplitBatches(sql);
        if (split.SeparatorError != null || split.Batches.Count == 0) return 1;
        long runs = split.Batches.Sum(b => (long)Math.Min(Math.Max(1, b.RepeatCount), 10_000));
        return (int)Math.Min(int.MaxValue, Math.Max(1, runs));
    }

    /// <summary>
    /// Spec 041 (R22) — how long to wait for the reply: the per-command timeout for every command the script
    /// runs, plus slack. A heuristic — SqlClient's command timeout resets on every network read — which is
    /// why running out of it says the statement "may still be running" rather than that it failed.
    /// </summary>
    internal TimeSpan ReplyDeadline(string sql, int timeoutSeconds)
    {
        double seconds = (double)Math.Max(1, timeoutSeconds) * CountBatchRuns(sql);
        return TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.FromDays(1).TotalSeconds)) + DeadlineSlack;
    }

    public Task<ExecuteQueryResult> ExecuteAsync(string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct) =>
        SendScriptAsync(MessageTypes.ExecuteQuery, sql, maxRows, timeoutSeconds, queryId, ct);

    public Task<ExecuteQueryResult> ParseAsync(string sql, int timeoutSeconds, string queryId, CancellationToken ct)
    {
        if (Array.IndexOf(_bridge.EngineCapabilities ?? Array.Empty<string>(), CapabilityExecuteV2) < 0)
        {
            // An older engine has no Parse: an unknown request type gets no reply at all.
            return Task.FromResult(new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Error, ErrorMessage = ParseUnavailable });
        }
        return SendScriptAsync(MessageTypes.ExecuteParse, sql, 1, timeoutSeconds, queryId, ct);
    }

    private async Task<ExecuteQueryResult> SendScriptAsync(int messageType, string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open || !_sqlConn.IsConnected)
        {
            return new ExecuteQueryResult
            {
                QueryId = queryId,
                Status = ExecuteStatus.NoConnection,
                ErrorMessage = "Connect to a SQL Server first (the engine must be paired and a database connected).",
            };
        }

        using var deadline = new CancellationTokenSource(ReplyDeadline(sql ?? string.Empty, timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            var req = new ExecuteQueryRequest
            {
                SessionId = _sqlConn.SessionId,
                Sql = sql ?? string.Empty,
                MaxRows = maxRows,
                CommandTimeoutSeconds = timeoutSeconds,
                QueryId = queryId,
                IncludeProvenance = true,
            };
            var result = await _bridge.SendAsync<ExecuteQueryRequest, ExecuteQueryResult>(
                messageType, req, linked.Token).ConfigureAwait(false);

            // Spec 041 (R27): a script's USE moves the session; the toolbar's database selector follows.
            if (result != null) _sqlConn.ReportCurrentDatabase(result.CurrentDatabase);

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

    public async Task CancelAsync(string queryId, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open || string.IsNullOrEmpty(queryId)) return;
        try
        {
            await _bridge.SendNotificationAsync(
                MessageTypes.ExecuteCancel,
                new ExecuteCancelRequest { SessionId = _sqlConn.SessionId, QueryId = queryId },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Trace, "execute", $"ExecuteCancel send failed: {ex.Message}");
        }
    }

    public async Task<ApplyChangesResult> ApplyAsync(ApplyChangesRequest request, CancellationToken ct)
    {
        if (_bridge.State != BridgeState.Open || !_sqlConn.IsConnected)
        {
            return new ApplyChangesResult { Status = ExecuteStatus.NoConnection, ErrorMessage = "Not connected." };
        }

        try
        {
            request.SessionId = _sqlConn.SessionId; // always the canonical session.
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
