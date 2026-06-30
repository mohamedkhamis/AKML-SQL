using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

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
}

internal sealed class QueryExecutionService : IQueryExecutionService
{
    private readonly IEngineBridge _bridge;
    private readonly ISqlConnectionService _sqlConn;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    public QueryExecutionService(IEngineBridge bridge, ISqlConnectionService sqlConn, IDiagnosticsRingBuffer diagnostics)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _sqlConn = sqlConn ?? throw new ArgumentNullException(nameof(sqlConn));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<ExecuteQueryResult> ExecuteAsync(string sql, int maxRows, int timeoutSeconds, string queryId, CancellationToken ct)
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
                MessageTypes.ExecuteQuery, req, ct).ConfigureAwait(false);

            // The bridge returns default! for an empty payload; defend so callers always get a result.
            return result ?? new ExecuteQueryResult
            {
                QueryId = queryId,
                Status = ExecuteStatus.Error,
                ErrorMessage = "The engine returned an empty execute response.",
            };
        }
        catch (OperationCanceledException)
        {
            return new ExecuteQueryResult { QueryId = queryId, Status = ExecuteStatus.Cancelled, ErrorMessage = "Cancelled." };
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
