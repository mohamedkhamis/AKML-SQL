using System;
using System.Collections.Generic;
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
/// Spec 043 (review of US1) — with a browser's requests answered side by side, a session can be disconnected and connected again
/// while one of its runs is still finishing (a reload's reattach gives up waiting for the old run; or the browser stopped waiting and
/// the user connected again). That run's script may have changed database (USE); only a run on the session's CURRENT connection may
/// move the session there. SKIPPED when no local SQL Server answers.
/// </summary>
[Collection("ExecuteQueryIntegration")]
public sealed class StaleRunDatabaseMoveIntegrationTests
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

    private static RpcMessage Execute(string sessionId, string sql) => new()
    {
        MessageType = MessageTypes.ExecuteQuery,
        RequestId = 1,
        Payload = MessagePackSerializer.Serialize(new ExecuteQueryRequest
        {
            SessionId = sessionId, Sql = sql, MaxRows = 100, CommandTimeoutSeconds = 30, QueryId = Guid.NewGuid().ToString("N"),
        }),
    };

    [SkippableFact]
    public async Task A_run_on_the_current_connection_moves_the_session_to_the_database_its_script_ended_in()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server.");
        var registry = new SessionConnectionRegistry();
        var moved = new List<(string Session, string Database)>();
        var handler = new ExecuteQueryHandler(registry, new SchemaCacheManager(), null, (s, db) => moved.Add((s, db)));

        await handler.HandleAsync(Execute("s-live", "USE tempdb; SELECT 1 AS n;"), _ => (ConnString, "master"), CancellationToken.None);

        Assert.Equal(new[] { ("s-live", "tempdb") }, moved);
        await registry.CloseAsync("s-live", TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    [SkippableFact]
    public async Task A_run_that_finishes_after_its_session_was_connected_again_does_not_move_the_new_connection()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server.");
        var registry = new SessionConnectionRegistry();
        var moved = new List<(string Session, string Database)>();
        var handler = new ExecuteQueryHandler(registry, new SchemaCacheManager(), null, (s, db) => moved.Add((s, db)));

        var running = handler.HandleAsync(Execute("s-stale", "USE tempdb; WAITFOR DELAY '00:00:02'; SELECT 1 AS n;"),
            _ => (ConnString, "master"), CancellationToken.None);
        await Task.Delay(700);   // the USE ran; the WAITFOR is running

        // The reattach: the old connection is closed without waiting long for the run, and the session is connected again.
        // A close that cannot finish within its wait says so (TimeoutException); either way the session is closed for new work.
        try { await registry.CloseAsync("s-stale", TimeSpan.FromMilliseconds(50), CancellationToken.None); }
        catch (TimeoutException) { /* the run is still finishing: the next test covers that case on purpose */ }
        var fresh = registry.GetOrCreate("s-stale", ConnString);
        await running;

        Assert.Empty(moved);   // the old run's USE must not move the new connection to tempdb
        Assert.Same(fresh, registry.TryGet("s-stale"));
        await registry.CloseAsync("s-stale", TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    [SkippableFact]
    public async Task A_run_that_outlives_the_close_wait_does_not_move_the_connection_made_after_it()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server.");
        var registry = new SessionConnectionRegistry();
        var moved = new List<(string Session, string Database)>();
        var handler = new ExecuteQueryHandler(registry, new SchemaCacheManager(), null, (s, db) => moved.Add((s, db)));

        var running = handler.HandleAsync(Execute("s-outlives", "USE tempdb; WAITFOR DELAY '00:00:02'; SELECT 1 AS n;"),
            _ => (ConnString, "master"), CancellationToken.None);
        await Task.Delay(700);   // the USE ran; the WAITFOR is running

        // No wait at all: the close gives up at once while the run still holds the connection, as a reattach that does not
        // wait for a long statement does. The session is connected again before the run ends.
        await Assert.ThrowsAsync<TimeoutException>(() => registry.CloseAsync("s-outlives", TimeSpan.Zero, CancellationToken.None));
        var fresh = registry.GetOrCreate("s-outlives", ConnString);
        await running;   // the close also asked the run to stop; however it ends, it ends after the new connection exists

        Assert.Empty(moved);
        Assert.Same(fresh, registry.TryGet("s-outlives"));
        await registry.CloseAsync("s-outlives", TimeSpan.FromSeconds(5), CancellationToken.None);
    }
}
