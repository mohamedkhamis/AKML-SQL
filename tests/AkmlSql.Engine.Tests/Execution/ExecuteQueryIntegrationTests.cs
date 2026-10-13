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
    public async Task Select_OmittingPrimaryKey_FiltersHiddenKeyInfoColumn()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");

        var (transport, _) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);

        // A base table whose PRIMARY KEY is NOT in the SELECT list. Under CommandBehavior.KeyInfo
        // SQL Server appends the PK as a HIDDEN column so each row is identifiable — it must be
        // filtered out, never shown beside the one column the user actually selected (spec 030 loop).
        await ExecuteAsync(transport, sid,
            "CREATE TABLE #hk (id int PRIMARY KEY, val varchar(10)); INSERT INTO #hk VALUES (1,'a'),(2,'b');", 1);

        var sel = await ExecuteAsync(transport, sid, "SELECT val FROM #hk ORDER BY val;", 2);
        Assert.Equal(ExecuteStatus.Ok, sel.Status);
        var rs = sel.ResultSets[0];
        Assert.Equal(new[] { "val" }, rs.ColumnNames);          // hidden 'id' filtered out
        Assert.All(rs.Rows, r => Assert.Single(r));              // each row carries only the visible column
        // The key is absent from the result set → rows can't be targeted → read-only.
        Assert.False(rs.IsEditable);

        // Sanity: when the key IS selected it appears (not hidden) and the set is editable.
        var sel2 = await ExecuteAsync(transport, sid, "SELECT id, val FROM #hk ORDER BY id;", 3);
        var rs2 = sel2.ResultSets[0];
        Assert.Equal(new[] { "id", "val" }, rs2.ColumnNames);
        Assert.True(rs2.IsEditable);
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
    public async Task ApplyChanges_Update_Succeeds_UnderSetNocountOn()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");

        var (transport, _) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);

        var tableName = "akml_nocount_" + Guid.NewGuid().ToString("N");
        try
        {
            var setup = await ExecuteAsync(transport, sid,
                $"CREATE TABLE dbo.[{tableName}] (Id int PRIMARY KEY, Name nvarchar(50)); " +
                $"INSERT INTO dbo.[{tableName}] VALUES (1, 'Alice'), (2, 'Bob');", 1);
            Assert.Equal(ExecuteStatus.Ok, setup.Status);

            // Turn NOCOUNT ON on the PERSISTENT session — exactly what a prior user batch can leave behind.
            // Under NOCOUNT, ExecuteNonQuery returns -1, so a keyed-write guard that reads its return value
            // would wrongly reject the UPDATE. The write must instead read @@ROWCOUNT (unaffected by NOCOUNT).
            var nocount = await ExecuteAsync(transport, sid, "SET NOCOUNT ON;", 2);
            Assert.Equal(ExecuteStatus.Ok, nocount.Status);

            var apply = new ApplyChangesRequest
            {
                SessionId = sid,
                BaseSchema = "dbo",
                BaseTable = tableName,
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
            var applyResp = await transport.SendAsync(new RpcMessage
            {
                MessageType = MessageTypes.ApplyChanges, RequestId = 3, Payload = MessagePackSerializer.Serialize(apply),
            }, CancellationToken.None);
            var applyResult = MessagePackSerializer.Deserialize<ApplyChangesResult>(applyResp!.Payload!);

            Assert.Equal(ExecuteStatus.Ok, applyResult.Status);
            Assert.True(applyResult.Results[0].Ok, applyResult.Results[0].Error);
            Assert.Equal(1, applyResult.Results[0].RowsAffected);

            var reread = await ExecuteAsync(transport, sid, $"SELECT Name FROM dbo.[{tableName}] WHERE Id = 2;", 4);
            Assert.Equal("Bobby", reread.ResultSets[0].Rows[0][0]);
        }
        finally
        {
            await ExecuteAsync(transport, sid, $"IF OBJECT_ID('dbo.[{tableName}]') IS NOT NULL DROP TABLE dbo.[{tableName}];", 99);
        }
    }

    /// <summary>
    /// Spec 041 (T045, research N2/R10): SqlClient reports NumericPrecision/NumericScale as 255 where they
    /// do not apply. The engine used to forward that into provenance, the web copied it into every Apply
    /// cell, and the parameter got Scale = 255 — so editing a date failed ("Scale value '255'…") and a
    /// money column overflowed. Provenance now carries null for the sentinel, and both edits apply.
    /// </summary>
    [SkippableFact]
    public async Task ApplyChanges_OnDateAndMoneyColumns_Succeeds_With_The255SentinelNulled()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");

        var (transport, _) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);

        var tableName = "akml_sentinel_" + Guid.NewGuid().ToString("N");
        try
        {
            var setup = await ExecuteAsync(transport, sid,
                $"CREATE TABLE dbo.[{tableName}] (Id int PRIMARY KEY, D date NULL, M money NULL, Amount decimal(10,2) NULL); " +
                $"INSERT INTO dbo.[{tableName}] VALUES (1, '2025-01-01', 1.5, 2.25);", 1);
            Assert.Equal(ExecuteStatus.Ok, setup.Status);

            var read = await ExecuteAsync(transport, sid, $"SELECT Id, D, M, Amount FROM dbo.[{tableName}];", 2);
            var rs = read.ResultSets[0];
            Assert.True(rs.IsEditable);

            var id = rs.Provenance.Single(p => p.BaseColumnName == "Id");
            var d = rs.Provenance.Single(p => p.BaseColumnName == "D");
            var m = rs.Provenance.Single(p => p.BaseColumnName == "M");
            var amount = rs.Provenance.Single(p => p.BaseColumnName == "Amount");
            Assert.NotEqual(255, id.Scale);
            Assert.Null(id.Scale);          // int: scale does not apply
            Assert.Null(m.Scale);           // money: scale does not apply
            Assert.NotEqual(255, d.Scale);
            Assert.NotEqual(255, d.Precision);
            Assert.Equal(10, amount.Precision);
            Assert.Equal(2, amount.Scale);

            CrudCellDto CellFrom(ColumnProvenanceDto p, string value) => new()
            {
                BaseColumnName = p.BaseColumnName!,
                ProviderType = p.ProviderType,
                Size = p.ColumnSize,
                Precision = p.Precision,
                Scale = p.Scale,
                Value = value,
            };

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
                        SetCells = new[] { CellFrom(d, "2025-10-01T00:00:00.0000000"), CellFrom(m, "12.5"), CellFrom(amount, "7.75") },
                        KeyCells = new[] { CellFrom(id, "1") },
                    },
                },
            };
            var applyResp = await transport.SendAsync(new RpcMessage
            {
                MessageType = MessageTypes.ApplyChanges, RequestId = 3, Payload = MessagePackSerializer.Serialize(apply),
            }, CancellationToken.None);
            var applyResult = MessagePackSerializer.Deserialize<ApplyChangesResult>(applyResp!.Payload!);

            Assert.True(applyResult.Status == ExecuteStatus.Ok, applyResult.ErrorMessage ?? applyResult.Results.FirstOrDefault()?.Error);
            Assert.True(applyResult.Results[0].Ok, applyResult.Results[0].Error);

            var reread = await ExecuteAsync(transport, sid,
                $"SELECT CONVERT(char(10), D, 23), CAST(M AS decimal(19,4)), Amount FROM dbo.[{tableName}] WHERE Id = 1;", 4);
            Assert.Equal("2025-10-01", reread.ResultSets[0].Rows[0][0]);
            Assert.Equal("12.5000", reread.ResultSets[0].Rows[0][1]);
            Assert.Equal("7.75", reread.ResultSets[0].Rows[0][2]);
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

    // ── Spec 041 (T080, research R16–R21, R71): scripts run as SSMS runs them ─────────────────────────

    private static async Task<ExecuteQueryResult> RunAsync(
        InProcessTransport transport, string sessionId, string sql, int reqId,
        int maxRows = 1000, int timeoutSeconds = 30, bool parse = false)
    {
        var req = new ExecuteQueryRequest
        {
            SessionId = sessionId,
            Sql = sql,
            MaxRows = maxRows,
            CommandTimeoutSeconds = timeoutSeconds,
            QueryId = Guid.NewGuid().ToString("N"),
            IncludeProvenance = true,
        };
        var msg = new RpcMessage
        {
            MessageType = parse ? MessageTypes.ExecuteParse : MessageTypes.ExecuteQuery,
            RequestId = reqId,
            Payload = MessagePackSerializer.Serialize(req),
        };
        var resp = await transport.SendAsync(msg, CancellationToken.None);
        Assert.NotNull(resp);
        Assert.Equal(parse ? MessageTypes.ExecuteParseResult : MessageTypes.ExecuteQueryResult, resp!.MessageType);
        Assert.Equal(reqId, resp.RequestId);
        return MessagePackSerializer.Deserialize<ExecuteQueryResult>(resp.Payload!);
    }

    private static async Task<(InProcessTransport Transport, string Session)> ConnectedAsync()
    {
        var (transport, _) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);
        return (transport, sid);
    }

    [SkippableFact]
    public async Task Row_counts_arrive_per_statement_in_order_and_a_SELECTs_count_names_its_set()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid,
            "CREATE TABLE #c (n int);\nINSERT #c VALUES (1), (2);\nSELECT n FROM #c;\nUPDATE #c SET n = n + 10;\nSELECT n FROM #c WHERE n > 11;", 1);

        Assert.Equal(ExecuteStatus.Ok, r.Status);
        var counts = r.MessageDetails.Where(m => m.Kind == ExecuteMessageKind.RowCount).ToArray();
        Assert.Equal(new long[] { 2, 2, 2, 1 }, counts.Select(m => m.RowCount));
        Assert.Equal(new[] { -1, 0, -1, 1 }, counts.Select(m => m.ResultSetIndex));
        Assert.Equal(4, r.TotalRowsAffected);
        Assert.Equal(new[] { "(2 rows affected)", "(2 rows affected)", "(2 rows affected)", "(1 row affected)" }, r.Messages);
        Assert.True(r.CompletedAtUnixMs > 0);
    }

    [SkippableFact]
    public async Task A_failing_statement_leaves_the_sets_around_it_and_reports_its_line()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid, "SELECT 1 AS a;\nSELECT 1/0 AS b;\nSELECT 2 AS c;", 1);

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Equal(3, r.ResultSets.Length);
        Assert.Single(r.ResultSets[0].Rows);
        Assert.Empty(r.ResultSets[1].Rows);
        Assert.Single(r.ResultSets[2].Rows);
        var error = r.MessageDetails.Single(m => m.Kind == ExecuteMessageKind.Error);
        Assert.Equal(8134, error.Number);
        Assert.Equal(2, error.Line);
        Assert.Equal(16, error.Severity);
        Assert.Equal(error.Text, r.ErrorMessage);
        Assert.Contains("Msg 8134, Level 16, State 1, Line 2\nDivide by zero error encountered.", r.Messages);
    }

    [SkippableFact]
    public async Task PRINT_is_an_info_message_with_number_0()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid, "PRINT 'hello';", 1);

        var m = r.MessageDetails.Single();
        Assert.Equal(ExecuteMessageKind.Info, m.Kind);
        Assert.Equal(0, m.Number);
        Assert.Equal("hello", m.Text);
        Assert.Equal(ExecuteStatus.Ok, r.Status);
    }

    [SkippableFact]
    public async Task An_error_inside_a_procedure_names_it_and_a_CREATE_PROCEDURE_batch_runs()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var created = await RunAsync(t, sid, "CREATE PROCEDURE #akml_p AS\nSELECT 1 AS n;\nRAISERROR('boom', 16, 1);\nGO\nEXEC #akml_p;", 1);

        Assert.Equal(2, created.Batches.Length);
        Assert.Single(created.ResultSets);
        var error = created.MessageDetails.Single(m => m.Kind == ExecuteMessageKind.Error);
        Assert.False(string.IsNullOrEmpty(error.Procedure));
        Assert.StartsWith("#akml_p", error.Procedure);
        Assert.Equal(1, error.BatchIndex);
    }

    [SkippableFact]
    public async Task NOCOUNT_gives_no_count_lines_and_a_truncated_set_says_more_exist()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var quiet = await RunAsync(t, sid, "SET NOCOUNT ON; SELECT 1 AS n; SET NOCOUNT OFF;", 1);
        Assert.DoesNotContain(quiet.MessageDetails, m => m.Kind == ExecuteMessageKind.RowCount);

        var capped = await RunAsync(t, sid, "SELECT TOP (10) v.n FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) v(n);", 2, maxRows: 3);
        Assert.True(capped.ResultSets[0].Truncated);
        Assert.Equal("(3 rows returned; more exist)", capped.Messages.Single());
        Assert.Equal(10, capped.MessageDetails.Single().RowCount);
    }

    [SkippableFact]
    public async Task A_failing_middle_batch_does_not_stop_the_script()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid, "SELECT 1 AS a;\nGO\nSELECT * FROM dbo.akml_no_such_table_041;\nGO\nSELECT 3 AS c;", 1);

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Equal(new[] { "a", "c" }, r.ResultSets.Select(s => s.ColumnNames[0]));
        Assert.Equal(new[] { 0, 2 }, r.ResultSets.Select(s => s.BatchIndex));
        var error = r.MessageDetails.Single(m => m.Kind == ExecuteMessageKind.Error);
        Assert.Equal(208, error.Number);
        Assert.Equal(1, error.BatchIndex);
        Assert.Equal(3, r.Batches[1].StartLine);
        Assert.Equal(new[] { ExecuteStatus.Ok, ExecuteStatus.Error, ExecuteStatus.Ok }, r.Batches.Select(b => b.Status));
    }

    [SkippableFact]
    public async Task GO_n_repeats_the_batch_with_SSMS_loop_lines()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid, "SELECT 1 AS n;\nGO 2", 1);

        Assert.Equal(2, r.ResultSets.Length);
        Assert.Equal(2, r.Batches.Single().RepeatCount);
        Assert.Equal("Beginning execution loop", r.Messages.First());
        Assert.Equal("Batch execution completed 2 times.", r.Messages.Last());
    }

    [SkippableFact]
    public async Task A_GO_line_with_a_statement_refuses_the_script_and_runs_nothing()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid, "CREATE TABLE #never (n int);\nGO SELECT 2", 1);
        var check = await RunAsync(t, sid, "SELECT OBJECT_ID('tempdb..#never') AS id;", 2);

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Empty(r.Batches);
        Assert.Contains("Incorrect syntax was encountered while parsing GO.", r.ErrorMessage);
        Assert.Null(check.ResultSets[0].Rows[0][0]);
    }

    [SkippableFact]
    public async Task A_timeout_stops_the_script_and_skips_the_rest()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid, "SELECT 1 AS first;\nGO\nWAITFOR DELAY '00:00:05';\nGO\nSELECT 2 AS never;", 1, timeoutSeconds: 1);

        Assert.Equal(ExecuteStatus.TimedOut, r.Status);
        Assert.Equal(new[] { ExecuteStatus.Ok, ExecuteStatus.TimedOut, ExecuteStatus.Skipped }, r.Batches.Select(b => b.Status));
        Assert.Equal("first", r.ResultSets.Single().ColumnNames[0]);
    }

    [SkippableFact]
    public async Task A_severity_20_error_closes_the_connection_and_the_rest_is_skipped()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();
        var role = await RunAsync(t, sid, "SELECT IS_SRVROLEMEMBER('sysadmin') AS sa;", 1);
        Skip.IfNot(role.ResultSets[0].Rows[0][0] == "1", "RAISERROR severity 20 needs sysadmin.");

        var r = await RunAsync(t, sid, "RAISERROR('fatal', 20, 1) WITH LOG;\nGO\nSELECT 1 AS never;", 2);

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Equal(ExecuteStatus.Skipped, r.Batches[1].Status);
        Assert.Contains("The remaining 1 batch(es) did not run because the connection was closed.", r.Messages);
        var next = await RunAsync(t, sid, "SELECT 1 AS n;", 3);
        Assert.True(next.ConnectionWasReset);
    }

    [SkippableFact]
    public async Task Column_types_are_the_declared_types()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid,
            "SELECT CAST(SYSDATETIME() AS datetime2(3)) AS a, CAST(1.5 AS decimal(10,2)) AS b, CAST(N'x' AS nvarchar(max)) AS c, CAST('y' AS varchar(20)) AS d, 1 AS e;", 1);

        Assert.Equal(new[] { "datetime2(3)", "decimal(10,2)", "nvarchar(max)", "varchar(20)", "int" }, r.ResultSets[0].ColumnSqlTypes);
    }

    [SkippableFact]
    public async Task Messages_beyond_ten_thousand_are_counted_not_sent()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid, "DECLARE @i int = 0; WHILE @i < 12000 BEGIN PRINT 'p'; SET @i += 1; END", 1, timeoutSeconds: 120);

        Assert.Equal(2_000, r.MessagesOmitted);
        Assert.Equal(ExecuteMessageKind.System, r.MessageDetails.Last().Kind);
    }

    [SkippableFact]
    public async Task ApplyChanges_after_an_execute_still_reports_its_failure()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();
        var tableName = "akml_041_flag_" + Guid.NewGuid().ToString("N");
        try
        {
            await RunAsync(t, sid, $"CREATE TABLE dbo.[{tableName}] (Id int PRIMARY KEY, Qty int NOT NULL); INSERT dbo.[{tableName}] VALUES (1, 1);", 1);
            await RunAsync(t, sid, "SELECT 1/0 AS warm;", 2);   // an execute that turned errors into info messages

            var apply = new ApplyChangesRequest
            {
                SessionId = sid,
                BaseSchema = "dbo",
                BaseTable = tableName,
                Edits = new[]
                {
                    new CrudEditDto
                    {
                        Op = CrudOp.Update,
                        SetCells = new[] { new CrudCellDto { BaseColumnName = "Qty", ProviderType = (int)System.Data.SqlDbType.Int, Value = null } },
                        KeyCells = new[] { new CrudCellDto { BaseColumnName = "Id", ProviderType = (int)System.Data.SqlDbType.Int, Value = "1" } },
                    },
                },
            };
            var resp = await t.SendAsync(new RpcMessage { MessageType = MessageTypes.ApplyChanges, RequestId = 3, Payload = MessagePackSerializer.Serialize(apply) }, CancellationToken.None);
            var result = MessagePackSerializer.Deserialize<ApplyChangesResult>(resp!.Payload!);

            Assert.Equal(ExecuteStatus.Error, result.Status);
            Assert.False(result.Results[0].Ok);
        }
        finally
        {
            await RunAsync(t, sid, $"IF OBJECT_ID('dbo.[{tableName}]') IS NOT NULL DROP TABLE dbo.[{tableName}];", 99);
        }
    }

    [SkippableFact]
    public async Task Parse_checks_every_batch_runs_nothing_and_execute_still_works_after_it()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();
        var tableName = "akml_041_parse_" + Guid.NewGuid().ToString("N");

        var ok = await RunAsync(t, sid, $"CREATE TABLE dbo.[{tableName}] (n int);\nGO\nSELECT 1 AS n;", 1, parse: true);
        Assert.Equal(ExecuteStatus.Ok, ok.Status);
        Assert.Empty(ok.ResultSets);

        var exists = await RunAsync(t, sid, $"SELECT OBJECT_ID('dbo.[{tableName}]') AS id;", 2);
        Assert.Null(exists.ResultSets[0].Rows[0][0]);

        var bad = await RunAsync(t, sid, "SELECT 1;\nGO\nSELECT 1;\nSELECT 2;\nSELEC 3;", 3, parse: true);
        Assert.Equal(ExecuteStatus.Error, bad.Status);
        var error = bad.MessageDetails.First(m => m.Kind == ExecuteMessageKind.Error);
        Assert.Equal(1, error.BatchIndex);
        Assert.Equal(3, error.Line);
        Assert.Equal(3, bad.Batches[1].StartLine);

        var after = await RunAsync(t, sid, "SELECT 42 AS n;", 4);
        Assert.Equal("42", after.ResultSets[0].Rows[0][0]);
    }

    // ── Spec 041 (T180, research R26, R27) — the database selector and USE ───────────────────────────────

    private static async Task<ChangeDatabaseResponse> SwitchAsync(InProcessTransport transport, string sessionId, string database, int reqId)
    {
        var resp = await transport.SendAsync(new RpcMessage
        {
            MessageType = MessageTypes.ChangeDatabase,
            RequestId = reqId,
            Payload = MessagePackSerializer.Serialize(new ChangeDatabaseRequest { SessionId = sessionId, DatabaseName = database }),
        }, CancellationToken.None);
        Assert.NotNull(resp);
        Assert.Equal(MessageTypes.ChangeDatabaseResult, resp!.MessageType);
        return MessagePackSerializer.Deserialize<ChangeDatabaseResponse>(resp.Payload!);
    }

    [SkippableFact]
    public async Task Switching_database_keeps_the_connection_so_a_temp_table_survives()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (transport, comp) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);
        Assert.Equal(ExecuteStatus.Ok, (await RunAsync(transport, sid, "CREATE TABLE #t (n int); INSERT #t VALUES (7);", 1)).Status);

        var switched = await SwitchAsync(transport, sid, "master", 2);

        Assert.True(switched.Ok, switched.ErrorMessage);
        Assert.Equal("master", switched.DatabaseName);
        Assert.False(switched.ConnectionWasReset);
        var after = await RunAsync(transport, sid, "SELECT n FROM #t; SELECT DB_NAME();", 3);
        Assert.Equal(ExecuteStatus.Ok, after.Status);
        Assert.Equal("7", after.ResultSets[0].Rows[0][0]);
        Assert.Equal("master", after.ResultSets[1].Rows[0][0]);
        Assert.Equal("master", after.CurrentDatabase);
        Assert.Equal("master", comp.Context.Sessions.GetSession(sid)!.DatabaseName);   // re-keyed
    }

    [SkippableFact]
    public async Task Switching_to_a_database_that_does_not_exist_reports_the_server_error()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t2, comp2) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(t2, sid);

        var switched = await SwitchAsync(t2, sid, "akml_no_such_db_041", 2);

        Assert.False(switched.Ok);
        Assert.False(string.IsNullOrEmpty(switched.ErrorMessage));
        Assert.Equal("tempdb", comp2.Context.Sessions.GetSession(sid)!.DatabaseName);
    }

    [SkippableFact]
    public async Task A_scripts_USE_is_reported_as_the_current_database_and_moves_the_session()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (transport, comp) = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid);

        var result = await RunAsync(transport, sid, "USE master; SELECT DB_NAME();", 1);

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Equal("master", result.CurrentDatabase);
        Assert.Equal("master", comp.Context.Sessions.GetSession(sid)!.DatabaseName);
    }

    [SkippableFact]
    public async Task The_current_database_is_on_an_error_reply_too()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (transport, sid) = await ConnectedAsync();

        var result = await RunAsync(transport, sid, "USE master; RAISERROR('boom', 16, 1);", 1);

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Equal("master", result.CurrentDatabase);
    }

    [SkippableFact]
    public async Task An_Apply_after_a_later_USE_writes_to_the_sets_own_database()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (transport, sid) = await ConnectedAsync();
        var table = "akml_basecat_" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Equal(ExecuteStatus.Ok, (await RunAsync(transport, sid,
                $"CREATE TABLE dbo.[{table}] (Id int PRIMARY KEY, Name nvarchar(20)); INSERT dbo.[{table}] VALUES (1, 'a');", 1)).Status);
            Assert.Equal("master", (await RunAsync(transport, sid, "USE master;", 2)).CurrentDatabase);

            // The web fills BaseCatalog from the set, or from the database current when the set was read (tempdb).
            var apply = new ApplyChangesRequest
            {
                SessionId = sid, BaseCatalog = "tempdb", BaseSchema = "dbo", BaseTable = table,
                Edits = new[]
                {
                    new CrudEditDto
                    {
                        Op = CrudOp.Update,
                        SetCells = new[] { new CrudCellDto { BaseColumnName = "Name", ProviderType = (int)System.Data.SqlDbType.NVarChar, Value = "b" } },
                        KeyCells = new[] { new CrudCellDto { BaseColumnName = "Id", ProviderType = (int)System.Data.SqlDbType.Int, Value = "1" } },
                    },
                },
            };
            var resp = await transport.SendAsync(new RpcMessage { MessageType = MessageTypes.ApplyChanges, RequestId = 3, Payload = MessagePackSerializer.Serialize(apply) }, CancellationToken.None);
            var applied = MessagePackSerializer.Deserialize<ApplyChangesResult>(resp!.Payload!);
            Assert.True(applied.Results[0].Ok, applied.Results[0].Error);

            var reread = await RunAsync(transport, sid, $"SELECT Name FROM tempdb.dbo.[{table}] WHERE Id = 1;", 4);
            Assert.Equal("b", reread.ResultSets[0].Rows[0][0]);
        }
        finally
        {
            await RunAsync(transport, sid, $"IF OBJECT_ID('tempdb.dbo.[{table}]') IS NOT NULL DROP TABLE tempdb.dbo.[{table}];", 99);
        }
    }

    // ── PR #255 review fixes ─────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task CREATE_OR_ALTER_VIEW_with_a_SELECT_runs_under_provenance()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();
        var view = "akml_255_v_" + Guid.NewGuid().ToString("N");
        try
        {
            // KeyInfo would put SET NO_BROWSETABLE ON in front of the CREATE: Msg 111.
            var r = await RunAsync(t, sid, $"CREATE OR ALTER VIEW dbo.[{view}] AS SELECT 1 AS n;", 1);
            Assert.Equal(ExecuteStatus.Ok, r.Status);
            Assert.Null(r.ErrorMessage);

            var again = await RunAsync(t, sid, $"CREATE OR ALTER VIEW dbo.[{view}] AS SELECT 2 AS n;", 2);
            Assert.Equal(ExecuteStatus.Ok, again.Status);
        }
        finally
        {
            await RunAsync(t, sid, $"IF OBJECT_ID('dbo.[{view}]') IS NOT NULL DROP VIEW dbo.[{view}];", 99);
        }
    }

    [SkippableFact]
    public async Task A_set_read_before_a_later_USE_carries_the_database_it_came_from_and_Apply_writes_there()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();
        var tableName = "akml_255_use_" + Guid.NewGuid().ToString("N");
        try
        {
            await RunAsync(t, sid, $"CREATE TABLE dbo.[{tableName}] (Id int PRIMARY KEY, Qty int NOT NULL); INSERT dbo.[{tableName}] VALUES (1, 1);", 1);

            var r = await RunAsync(t, sid, $"USE tempdb;\nSELECT Id, Qty FROM dbo.[{tableName}];\nUSE master;", 2);

            Assert.Equal(ExecuteStatus.Ok, r.Status);
            Assert.Equal("master", r.CurrentDatabase);
            var set = r.ResultSets.Single();
            Assert.True(set.IsEditable);
            Assert.Equal("tempdb", set.BaseCatalog);

            var apply = new ApplyChangesRequest
            {
                SessionId = sid,
                BaseCatalog = set.BaseCatalog,
                BaseSchema = set.BaseSchema ?? "dbo",
                BaseTable = set.BaseTable ?? tableName,
                Edits = new[]
                {
                    new CrudEditDto
                    {
                        Op = CrudOp.Update,
                        SetCells = new[] { new CrudCellDto { BaseColumnName = "Qty", ProviderType = (int)System.Data.SqlDbType.Int, Value = "5" } },
                        KeyCells = new[] { new CrudCellDto { BaseColumnName = "Id", ProviderType = (int)System.Data.SqlDbType.Int, Value = "1" } },
                    },
                },
            };
            var resp = await t.SendAsync(new RpcMessage { MessageType = MessageTypes.ApplyChanges, RequestId = 3, Payload = MessagePackSerializer.Serialize(apply) }, CancellationToken.None);
            var result = MessagePackSerializer.Deserialize<ApplyChangesResult>(resp!.Payload!);
            Assert.Equal(ExecuteStatus.Ok, result.Status);

            var check = await RunAsync(t, sid, $"SELECT Qty FROM tempdb.dbo.[{tableName}] WHERE Id = 1;", 4);
            Assert.Equal("5", check.ResultSets[0].Rows[0][0]);
        }
        finally
        {
            await RunAsync(t, sid, $"IF OBJECT_ID('tempdb.dbo.[{tableName}]') IS NOT NULL DROP TABLE tempdb.dbo.[{tableName}];", 99);
        }
    }

    [SkippableFact]
    public async Task An_error_after_ten_thousand_row_counts_still_fails_the_script()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var (t, sid) = await ConnectedAsync();

        var r = await RunAsync(t, sid, "DECLARE @i int = 0; WHILE @i < 10050 BEGIN PRINT 'p'; SET @i += 1; END;\nGO\nSELECT 1/0 AS boom;", 1, timeoutSeconds: 120);

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Contains("Divide by zero", r.ErrorMessage);
        Assert.Contains(r.MessageDetails, m => m.Kind == ExecuteMessageKind.Error && m.Number == 8134);
    }
}
