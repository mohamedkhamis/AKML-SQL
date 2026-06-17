using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Transports;
using MessagePack;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Spec 030 — Phase 5. End-to-end execution test over the in-process transport. SKIPPED (not failed)
/// when no local SQL Server is reachable, so CI hosts without a DB stay green. Proves:
///   • a SELECT returns SAFE-encoded rows + provenance,
///   • a #temp table created in one execute survives into the next (persistent-connection guarantee),
///   • ApplyChanges UPDATE persists and re-reads (write path + transaction).
/// </summary>
// Run the live-DB integration tests in their own non-parallel collection so they don't contend with
// the rest of the engine suite (or each other) for the testhost / the local SQL Server — a short
// connect under heavy parallel CPU load was a self-inflicted flake source.
[CollectionDefinition("ExecuteQueryIntegration", DisableParallelization = true)]
public sealed class ExecuteQueryIntegrationCollection { }

[Collection("ExecuteQueryIntegration")]
public sealed class ExecuteQueryIntegrationTests
{
    // Loopback. 15s connect timeout matches the web edition (SqlConnectionService) and tolerates a
    // busy host; the per-command execution still uses the 30s default.
    private const string ConnString =
        "Server=(local);Database=tempdb;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15";

    private static bool TryReachLocalSql()
    {
        try
        {
            using var conn = new SqlConnection(ConnString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.ExecuteScalar();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (InProcessTransport transport, EngineComposition comp) BuildEngine()
    {
        var comp = EngineComposition.Build();
        var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => comp.Router.RouteAsync(msg, comp.Context, ct);
        transport.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (transport, comp);
    }

    private static async Task ConnectAsync(InProcessTransport transport, string sessionId)
    {
        var info = new ConnectionInfo
        {
            SessionId = sessionId,
            ConnectionString = ConnString,
            DatabaseName = "tempdb",
            ServerVersion = 16,
            EngineEdition = 3,
        };
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.ConnectionChanged,
            RequestId = 0,
            Payload = MessagePackSerializer.Serialize(info),
        };
        await transport.SendAsync(msg, CancellationToken.None);
    }

    private static async Task<ExecuteQueryResult> ExecuteAsync(
        InProcessTransport transport, string sessionId, string sql, int reqId)
    {
        var req = new ExecuteQueryRequest
        {
            SessionId = sessionId,
            Sql = sql,
            MaxRows = 1000,
            CommandTimeoutSeconds = 30,
            QueryId = Guid.NewGuid().ToString("N"),
            IncludeProvenance = true,
        };
        var msg = new RpcMessage
        {
            MessageType = MessageTypes.ExecuteQuery,
            RequestId = reqId,
            Payload = MessagePackSerializer.Serialize(req),
        };
        var resp = await transport.SendAsync(msg, CancellationToken.None);
        Assert.NotNull(resp);
        Assert.Equal(MessageTypes.ExecuteQueryResult, resp!.MessageType);
        Assert.Equal(reqId, resp.RequestId);
        return MessagePackSerializer.Deserialize<ExecuteQueryResult>(resp.Payload!);
    }

    [SkippableFact]
    public async Task SelectLiteral_ReturnsSafeEncodedRow()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");

        var (transport, _) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);

        var result = await ExecuteAsync(transport, sid,
            "SELECT CAST(42 AS int) AS N, CAST('hello' AS nvarchar(20)) AS S, CAST(NULL AS int) AS Z", 1);

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Single(result.ResultSets);
        var rs = result.ResultSets[0];
        Assert.Equal(new[] { "N", "S", "Z" }, rs.ColumnNames);
        Assert.Single(rs.Rows);
        Assert.Equal("42", rs.Rows[0][0]);
        Assert.Equal("hello", rs.Rows[0][1]);
        Assert.Null(rs.Rows[0][2]); // SQL NULL == null array element.
        // A literal SELECT has no base table → not editable.
        Assert.False(rs.IsEditable);
    }

    [SkippableFact]
    public async Task TempTable_PersistsAcrossExecutes()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");

        var (transport, _) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);

        // Execute 1: create + seed a #temp table.
        var create = await ExecuteAsync(transport, sid,
            "CREATE TABLE #t (Id int); INSERT INTO #t VALUES (1),(2),(3);", 1);
        Assert.Equal(ExecuteStatus.Ok, create.Status);

        // Execute 2: the SAME persistent connection still sees #t.
        var select = await ExecuteAsync(transport, sid, "SELECT COUNT(*) AS C FROM #t;", 2);
        Assert.Equal(ExecuteStatus.Ok, select.Status);
        Assert.Single(select.ResultSets);
        Assert.Equal("3", select.ResultSets[0].Rows[0][0]);
    }

    [SkippableFact]
    public async Task ApplyChanges_UpdatesRow_AndReReads()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");

        var (transport, _) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);

        // Use a REAL table in tempdb (uniquely named) so KeyInfo yields full base-table provenance —
        // #temp tables surface tempdb-internal base names and are intentionally treated read-only.
        var tableName = "akml_phase5_" + Guid.NewGuid().ToString("N");
        try
        {
            var setup = await ExecuteAsync(transport, sid,
                $"CREATE TABLE dbo.[{tableName}] (Id int PRIMARY KEY, Name nvarchar(50)); " +
                $"INSERT INTO dbo.[{tableName}] VALUES (1, 'Alice'), (2, 'Bob');", 1);
            Assert.Equal(ExecuteStatus.Ok, setup.Status);

            // Read it back with provenance.
            var read = await ExecuteAsync(transport, sid, $"SELECT Id, Name FROM dbo.[{tableName}] ORDER BY Id;", 2);
            Assert.Equal(ExecuteStatus.Ok, read.Status);
            var rs = read.ResultSets[0];
            Assert.True(rs.IsEditable, "single-PK real-table SELECT should be editable");
            Assert.Equal(2, rs.Rows.Length);
            Assert.Equal(tableName, rs.BaseTable);
            // The PK column is flagged as a key.
            Assert.Contains(rs.Provenance, p => p.BaseColumnName == "Id" && p.IsKey);

            // Apply an UPDATE: rename Id=2 to 'Bobby' (parameterized, transactional, same connection).
            var apply = new ApplyChangesRequest
            {
                SessionId = sid,
                BaseSchema = rs.BaseSchema ?? "dbo",
                BaseTable = rs.BaseTable ?? tableName,
                Edits = new[]
                {
                    new CrudEditDto
                    {
                        Op = CrudOp.Update,
                        SetCells = new[] { new CrudCellDto { BaseColumnName = "Name", ProviderType = (int)System.Data.SqlDbType.NVarChar, Value = "Bobby" } },
                        KeyCells = new[] { new CrudCellDto { BaseColumnName = "Id", ProviderType = (int)System.Data.SqlDbType.Int, Value = "2" } },
                    },
                },
            };
            var applyMsg = new RpcMessage
            {
                MessageType = MessageTypes.ApplyChanges,
                RequestId = 3,
                Payload = MessagePackSerializer.Serialize(apply),
            };
            var applyResp = await transport.SendAsync(applyMsg, CancellationToken.None);
            Assert.NotNull(applyResp);
            Assert.Equal(MessageTypes.ApplyChangesResult, applyResp!.MessageType);
            var applyResult = MessagePackSerializer.Deserialize<ApplyChangesResult>(applyResp.Payload!);
            Assert.Equal(ExecuteStatus.Ok, applyResult.Status);
            Assert.True(applyResult.Results[0].Ok);
            Assert.Equal(1, applyResult.Results[0].RowsAffected);

            // Re-read: the UPDATE persisted on the same connection.
            var reread = await ExecuteAsync(transport, sid, $"SELECT Name FROM dbo.[{tableName}] WHERE Id = 2;", 4);
            Assert.Equal("Bobby", reread.ResultSets[0].Rows[0][0]);
        }
        finally
        {
            await ExecuteAsync(transport, sid, $"IF OBJECT_ID('dbo.[{tableName}]') IS NOT NULL DROP TABLE dbo.[{tableName}];", 99);
        }
    }

    [SkippableFact]
    public async Task Execute_WithoutConnection_ReturnsNoConnectionEnvelope()
    {
        // No Skip — this path needs no DB; it asserts the never-null NoConnection envelope.
        var (transport, _) = BuildEngine();
        var result = await ExecuteAsync(transport, Guid.NewGuid().ToString("N"), "SELECT 1", 1);
        Assert.Equal(ExecuteStatus.NoConnection, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }
}
