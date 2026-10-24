using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Schema;
using MessagePack;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Spec 043 (review of US1, second round). Since the engine answers a browser's requests side by side (research R3), an
/// ExecuteCancel notification is read while that browser's ExecuteQuery still runs, so it stops a statement that is running —
/// not only one still queued, as the single serial socket allowed before. Closing a running tab and the toolbar's Cancel
/// rely on this. SKIPPED when no local SQL Server answers.
/// </summary>
[Collection("ExecuteQueryIntegration")]
public sealed class RunningStatementCancelIntegrationTests
{
    private const string ConnString =
        "Server=(local);Database=master;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15";

    private static bool TryReachLocalSql()
    {
        try
        {
            using var conn = new SqlConnection(ConnString);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static RpcMessage Execute(string sessionId, string sql, string queryId) => new()
    {
        MessageType = MessageTypes.ExecuteQuery,
        RequestId = 1,
        Payload = MessagePackSerializer.Serialize(new ExecuteQueryRequest
        {
            SessionId = sessionId, Sql = sql, MaxRows = 100, CommandTimeoutSeconds = 60, QueryId = queryId,
        }),
    };

    private static RpcMessage Cancel(string sessionId, string queryId) => new()
    {
        MessageType = MessageTypes.ExecuteCancel,
        RequestId = 0,
        Payload = MessagePackSerializer.Serialize(new ExecuteCancelRequest { SessionId = sessionId, QueryId = queryId }),
    };

    [SkippableFact]
    public async Task A_cancel_sent_while_a_statement_runs_stops_it_and_the_batches_after_it_do_not_run()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server.");
        var registry = new SessionConnectionRegistry();
        var handler = new ExecuteQueryHandler(registry, new SchemaCacheManager(), null, (_, _) => { });
        var cancel = new ExecuteCancelHandler(registry);
        var queryId = Guid.NewGuid().ToString("N");

        var watch = Stopwatch.StartNew();
        var running = handler.HandleAsync(Execute("s-cancel", "WAITFOR DELAY '00:00:20'; SELECT 1 AS never;\nGO\nSELECT 2 AS after_it;", queryId),
            _ => (ConnString, "master"), CancellationToken.None);
        await Task.Delay(1_500);   // the WAITFOR is running
        Assert.False(running.IsCompleted);
        cancel.Handle(Cancel("s-cancel", queryId));
        var reply = await running;
        watch.Stop();

        var result = MessagePackSerializer.Deserialize<ExecuteQueryResult>(reply!.Payload);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"the cancel took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(ExecuteStatus.Cancelled, result.Status);
        Assert.Empty(result.ResultSets);   // neither SELECT ran
        await registry.CloseAsync("s-cancel", TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    [SkippableFact]
    public async Task A_cancel_sent_while_the_execute_waits_behind_another_run_stops_it_before_it_runs()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server.");
        var registry = new SessionConnectionRegistry();
        var handler = new ExecuteQueryHandler(registry, new SchemaCacheManager(), null, (_, _) => { });
        var cancel = new ExecuteCancelHandler(registry);
        var first = handler.HandleAsync(Execute("s-queued", "WAITFOR DELAY '00:00:03'; SELECT 1 AS first_run;", Guid.NewGuid().ToString("N")),
            _ => (ConnString, "master"), CancellationToken.None);
        await Task.Delay(700);   // the first run holds the session's connection
        var queuedId = Guid.NewGuid().ToString("N");
        var queued = handler.HandleAsync(Execute("s-queued", "SELECT 2 AS never_ran;", queuedId), _ => (ConnString, "master"), CancellationToken.None);
        await Task.Delay(200);
        Assert.False(queued.IsCompleted);   // waiting for the session

        cancel.Handle(Cancel("s-queued", queuedId));
        var queuedReply = MessagePackSerializer.Deserialize<ExecuteQueryResult>((await queued)!.Payload);
        Assert.False(first.IsCompleted);   // stopped while still queued, before the first run ended

        Assert.Equal(ExecuteStatus.Cancelled, queuedReply.Status);
        Assert.Empty(queuedReply.ResultSets);
        var firstReply = MessagePackSerializer.Deserialize<ExecuteQueryResult>((await first)!.Payload);
        Assert.Equal(ExecuteStatus.Ok, firstReply.Status);   // the other run is untouched
        await registry.CloseAsync("s-queued", TimeSpan.FromSeconds(5), CancellationToken.None);
    }
}
