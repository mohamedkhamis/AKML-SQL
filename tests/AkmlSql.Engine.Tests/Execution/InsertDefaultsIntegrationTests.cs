using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;
using MessagePack;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Review finding WEB-20 end to end against a uniquely named table in the local server's tempdb (dropped afterwards; SKIPPED
/// when no local SQL Server is reachable): a column the grid leaves out takes its default, an explicit NULL stays NULL, and a
/// row with nothing set takes every default.
/// </summary>
[Collection("ExecuteQueryIntegration")]
public sealed class InsertDefaultsIntegrationTests
{
    private const string ConnString =
        "Server=(local);Database=tempdb;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15";

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

    private static async Task<InProcessTransport> ConnectedEngineAsync(string sessionId)
    {
        var comp = EngineComposition.Build();
        var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => comp.Router.RouteAsync(msg, comp.Context, ct);
        await transport.StartAsync(CancellationToken.None);
        await transport.SendAsync(new RpcMessage
        {
            MessageType = MessageTypes.ConnectionChanged,
            Payload = MessagePackSerializer.Serialize(new ConnectionInfo { SessionId = sessionId, ConnectionString = ConnString, DatabaseName = "tempdb" }),
        }, CancellationToken.None);
        return transport;
    }

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(InProcessTransport transport, int type, TRequest request, int id)
    {
        var reply = await transport.SendAsync(new RpcMessage { MessageType = type, RequestId = id, Payload = MessagePackSerializer.Serialize(request) },
            CancellationToken.None);
        return MessagePackSerializer.Deserialize<TResponse>(reply!.Payload!);
    }

    private static Task<ExecuteQueryResult> ExecuteAsync(InProcessTransport transport, string sessionId, string sql, int id) =>
        SendAsync<ExecuteQueryRequest, ExecuteQueryResult>(transport, MessageTypes.ExecuteQuery, new ExecuteQueryRequest
        {
            SessionId = sessionId, Sql = sql, MaxRows = 100, CommandTimeoutSeconds = 30, QueryId = Guid.NewGuid().ToString("N"),
        }, id);

    private static CrudCellDto Text(string column, string? value) =>
        new() { BaseColumnName = column, ProviderType = (int)System.Data.SqlDbType.NVarChar, Size = 50, Value = value };

    [SkippableFact]
    public async Task Omitted_columns_take_their_defaults_and_an_explicit_NULL_stays_NULL()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var sid = Guid.NewGuid().ToString("N");
        var transport = await ConnectedEngineAsync(sid);
        var table = "akml_defaults_" + Guid.NewGuid().ToString("N");
        try
        {
            await ExecuteAsync(transport, sid,
                $"CREATE TABLE dbo.[{table}] (Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL, " +
                "Created datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), Note nvarchar(50) NULL DEFAULT N'from default', " +
                "Tag nvarchar(50) NOT NULL DEFAULT N'tagged');", 1);

            var apply = new ApplyChangesRequest
            {
                SessionId = sid,
                BaseSchema = "dbo",
                BaseTable = table,
                Edits = new[]
                {
                    // Created, Note and Tag left out: all three take their defaults.
                    new CrudEditDto { Op = CrudOp.Insert, SetCells = new[] { Text("Name", "omitted") } },
                    // Note set to NULL on purpose: it stays NULL; Tag still takes its default.
                    new CrudEditDto { Op = CrudOp.Insert, SetCells = new[] { Text("Name", "explicit null"), Text("Note", null) } },
                },
            };
            var result = await SendAsync<ApplyChangesRequest, ApplyChangesResult>(transport, MessageTypes.ApplyChanges, apply, 2);
            Assert.Equal(ExecuteStatus.Ok, result.Status);

            var rows = (await ExecuteAsync(transport, sid,
                $"SELECT Name, CASE WHEN Created IS NULL THEN 'null' ELSE 'set' END, Note, Tag FROM dbo.[{table}] ORDER BY Id;", 3)).ResultSets[0].Rows;
            Assert.Equal(new string?[] { "omitted", "set", "from default", "tagged" }, rows[0]);
            Assert.Equal(new string?[] { "explicit null", "set", null, "tagged" }, rows[1]);
        }
        finally
        {
            await ExecuteAsync(transport, sid, $"IF OBJECT_ID('dbo.[{table}]') IS NOT NULL DROP TABLE dbo.[{table}];", 99);
        }
    }

    [SkippableFact]
    public async Task A_row_with_nothing_set_takes_every_default()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var sid = Guid.NewGuid().ToString("N");
        var transport = await ConnectedEngineAsync(sid);
        var table = "akml_alldefaults_" + Guid.NewGuid().ToString("N");
        try
        {
            await ExecuteAsync(transport, sid,
                $"CREATE TABLE dbo.[{table}] (Id int IDENTITY PRIMARY KEY, Created datetime2 NOT NULL DEFAULT SYSUTCDATETIME());", 1);

            var result = await SendAsync<ApplyChangesRequest, ApplyChangesResult>(transport, MessageTypes.ApplyChanges, new ApplyChangesRequest
            {
                SessionId = sid, BaseSchema = "dbo", BaseTable = table,
                Edits = new[] { new CrudEditDto { Op = CrudOp.Insert } },
            }, 2);

            Assert.Equal(ExecuteStatus.Ok, result.Status);
            Assert.Equal("1", Assert.Single(result.Results).NewIdentity);
            Assert.Equal("1", (await ExecuteAsync(transport, sid, $"SELECT COUNT(*) FROM dbo.[{table}];", 3)).ResultSets[0].Rows[0][0]);
        }
        finally
        {
            await ExecuteAsync(transport, sid, $"IF OBJECT_ID('dbo.[{table}]') IS NOT NULL DROP TABLE dbo.[{table}];", 99);
        }
    }
}
