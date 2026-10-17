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
/// Review findings WEB-01 and WEB-06 end to end over the in-process transport, against a uniquely named table in the local
/// server's tempdb (dropped afterwards). SKIPPED when no local SQL Server is reachable.
/// <list type="bullet">
///   <item>Rows read through one connection are refused on the next one, even with the same table and keys.</item>
///   <item>Disconnect rolls back the session's open transaction, so another connection is not blocked, and the session then
///   holds no connection.</item>
/// </list>
/// </summary>
[Collection("ExecuteQueryIntegration")]
public sealed class SessionTargetIntegrationTests
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

    private static InProcessTransport BuildEngine()
    {
        var comp = EngineComposition.Build();
        var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => comp.Router.RouteAsync(msg, comp.Context, ct);
        transport.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return transport;
    }

    private static Task ConnectAsync(InProcessTransport transport, string sessionId, string connectionString) =>
        transport.SendAsync(new RpcMessage
        {
            MessageType = MessageTypes.ConnectionChanged,
            Payload = MessagePackSerializer.Serialize(new ConnectionInfo
            {
                SessionId = sessionId, ConnectionString = connectionString, DatabaseName = "tempdb", ServerVersion = 16, EngineEdition = 3,
            }),
        }, CancellationToken.None);

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(InProcessTransport transport, int type, TRequest request, int id)
    {
        var reply = await transport.SendAsync(new RpcMessage { MessageType = type, RequestId = id, Payload = MessagePackSerializer.Serialize(request) },
            CancellationToken.None);
        Assert.NotNull(reply);
        return MessagePackSerializer.Deserialize<TResponse>(reply!.Payload!);
    }

    private static Task<ExecuteQueryResult> ExecuteAsync(InProcessTransport transport, string sessionId, string sql, int id) =>
        SendAsync<ExecuteQueryRequest, ExecuteQueryResult>(transport, MessageTypes.ExecuteQuery, new ExecuteQueryRequest
        {
            SessionId = sessionId, Sql = sql, MaxRows = 1000, CommandTimeoutSeconds = 30, QueryId = Guid.NewGuid().ToString("N"), IncludeProvenance = true,
        }, id);

    private static ApplyChangesRequest Rename(string sessionId, string table, string? stamp) => new()
    {
        SessionId = sessionId,
        BaseSchema = "dbo",
        BaseTable = table,
        ConnectionStamp = stamp,
        Edits = new[]
        {
            new CrudEditDto
            {
                Op = CrudOp.Update,
                SetCells = new[] { new CrudCellDto { BaseColumnName = "Name", ProviderType = (int)System.Data.SqlDbType.NVarChar, Value = "Changed" } },
                KeyCells = new[] { new CrudCellDto { BaseColumnName = "Id", ProviderType = (int)System.Data.SqlDbType.Int, Value = "1" } },
            },
        },
    };

    [SkippableFact]
    public async Task Rows_read_through_one_connection_are_not_written_through_the_next()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var transport = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        var table = "akml_stamp_" + Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid, ConnString);
        try
        {
            await ExecuteAsync(transport, sid, $"CREATE TABLE dbo.[{table}] (Id int PRIMARY KEY, Name nvarchar(50)); INSERT INTO dbo.[{table}] VALUES (1, 'Original');", 1);
            var read = await ExecuteAsync(transport, sid, $"SELECT Id, Name FROM dbo.[{table}];", 2);
            Assert.False(string.IsNullOrEmpty(read.ConnectionStamp));

            // The session moves to another connection (here the same server under another login string) before Apply.
            await ConnectAsync(transport, sid, ConnString + ";Application Name=AKML other target");
            var refused = await SendAsync<ApplyChangesRequest, ApplyChangesResult>(transport, MessageTypes.ApplyChanges, Rename(sid, table, read.ConnectionStamp), 3);
            Assert.Equal(ExecuteStatus.Error, refused.Status);
            Assert.Equal(AkmlSql.Engine.Execution.ConnectionStamp.StaleMessage, refused.ErrorMessage);
            Assert.Equal("Original", (await ExecuteAsync(transport, sid, $"SELECT Name FROM dbo.[{table}] WHERE Id = 1;", 4)).ResultSets[0].Rows[0][0]);

            // Read again on the current connection: its stamp is accepted.
            var reread = await ExecuteAsync(transport, sid, $"SELECT Id, Name FROM dbo.[{table}];", 5);
            var applied = await SendAsync<ApplyChangesRequest, ApplyChangesResult>(transport, MessageTypes.ApplyChanges, Rename(sid, table, reread.ConnectionStamp), 6);
            Assert.Equal(ExecuteStatus.Ok, applied.Status);
        }
        finally
        {
            await ExecuteAsync(transport, sid, $"IF OBJECT_ID('dbo.[{table}]') IS NOT NULL DROP TABLE dbo.[{table}];", 99);
        }
    }

    [SkippableFact]
    public async Task Disconnect_rolls_back_the_open_transaction_and_releases_its_locks()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local)/tempdb.");
        var transport = BuildEngine();
        var sid = Guid.NewGuid().ToString("N");
        var table = "akml_disconnect_" + Guid.NewGuid().ToString("N");
        await ConnectAsync(transport, sid, ConnString);
        try
        {
            await ExecuteAsync(transport, sid, $"CREATE TABLE dbo.[{table}] (Id int PRIMARY KEY, Name nvarchar(50)); INSERT INTO dbo.[{table}] VALUES (1, 'Original');", 1);
            var open = await ExecuteAsync(transport, sid, $"BEGIN TRANSACTION; UPDATE dbo.[{table}] SET Name = 'Uncommitted' WHERE Id = 1;", 2);
            Assert.Equal(ExecuteStatus.Ok, open.Status);

            var disconnected = await SendAsync<SessionDisconnectRequest, SessionDisconnectResponse>(transport, MessageTypes.SessionDisconnect,
                new SessionDisconnectRequest { SessionId = sid }, 3);
            Assert.True(disconnected.Ok, disconnected.ErrorMessage);
            Assert.True(disconnected.RolledBack);

            // Another connection reads the row without waiting for a lock, and sees the value from before the transaction.
            await using (var other = new SqlConnection(ConnString))
            {
                await other.OpenAsync();
                await using var cmd = other.CreateCommand();
                cmd.CommandText = $"SET LOCK_TIMEOUT 2000; SELECT Name FROM dbo.[{table}] WHERE Id = 1;";
                Assert.Equal("Original", (string?)await cmd.ExecuteScalarAsync());
            }

            // The session holds no connection until the next ConnectionChanged.
            var after = await ExecuteAsync(transport, sid, "SELECT 1;", 4);
            Assert.Equal(ExecuteStatus.NoConnection, after.Status);

            // Reconnecting starts without the old transaction.
            await ConnectAsync(transport, sid, ConnString);
            var trancount = await ExecuteAsync(transport, sid, "SELECT @@TRANCOUNT;", 5);
            Assert.Equal("0", trancount.ResultSets[0].Rows[0][0]);
        }
        finally
        {
            await ConnectAsync(transport, sid, ConnString);
            await ExecuteAsync(transport, sid, $"IF OBJECT_ID('dbo.[{table}]') IS NOT NULL DROP TABLE dbo.[{table}];", 99);
        }
    }
}
