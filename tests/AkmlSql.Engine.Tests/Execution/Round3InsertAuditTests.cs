using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

[Collection("ExecuteQueryIntegration")]
public sealed class Round3InsertAuditTests
{
    private static async Task<InProcessTransport> Connect(string sid, string database)
    {
        var comp = EngineComposition.Build();
        var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => comp.Router.RouteAsync(msg, comp.Context, ct);
        await transport.StartAsync(CancellationToken.None);
        await transport.SendAsync(new RpcMessage { MessageType = MessageTypes.ConnectionChanged,
            Payload = MessagePackSerializer.Serialize(new ConnectionInfo { SessionId = sid, DatabaseName = database,
                ConnectionString = $"Server=(local);Database={database};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15" }) }, CancellationToken.None);
        return transport;
    }

    private static async Task<TResponse> Send<TRequest, TResponse>(InProcessTransport transport, int type, TRequest request)
    {
        var reply = await transport.SendAsync(new RpcMessage { MessageType = type, RequestId = 1,
            Payload = MessagePackSerializer.Serialize(request) }, CancellationToken.None);
        return MessagePackSerializer.Deserialize<TResponse>(reply!.Payload!);
    }

    private static async Task Sql(InProcessTransport transport, string sid, string sql)
    {
        var result = await Send<ExecuteQueryRequest, ExecuteQueryResult>(transport, MessageTypes.ExecuteQuery,
            new ExecuteQueryRequest { SessionId = sid, Sql = sql, MaxRows = 100, CommandTimeoutSeconds = 30,
                QueryId = Guid.NewGuid().ToString("N") });
        Assert.True(result.Status == ExecuteStatus.Ok, result.ErrorMessage);
    }

    [Fact]
    public async Task Cross_database_triggered_insert_succeeds()
        => await Probe("master", checkStoredRow: false);

    [Fact]
    public async Task Triggered_insert_returns_defaults_for_the_grid()
        => await Probe("tempdb", checkStoredRow: true);

    private static async Task Probe(string database, bool checkStoredRow)
    {
        var sid = Guid.NewGuid().ToString("N");
        var transport = await Connect(sid, database);
        var name = "akml_round3_" + Guid.NewGuid().ToString("N");
        try
        {
            await Sql(transport, sid, $"CREATE TABLE tempdb.dbo.[{name}] (Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL, Tag nvarchar(50) NOT NULL DEFAULT N'tagged');");
            await Sql(transport, sid, $"EXEC tempdb.sys.sp_executesql N'CREATE TRIGGER dbo.[trg_{name}] ON dbo.[{name}] AFTER INSERT AS SET NOCOUNT ON;';");
            var result = await Send<ApplyChangesRequest, ApplyChangesResult>(transport, MessageTypes.ApplyChanges,
                new ApplyChangesRequest {
                    SessionId = sid, BaseCatalog = "tempdb", BaseSchema = "dbo", BaseTable = name,
                    ReturnColumns = new[] { "Id", "Name", "Tag" },
                    Edits = new[] { new CrudEditDto { Op = CrudOp.Insert, SetCells = new[] {
                        new CrudCellDto { BaseColumnName = "Name", ProviderType = (int)System.Data.SqlDbType.NVarChar,
                            Size = 50, Value = "first" } } } }
                });
            Assert.True(result.Status == ExecuteStatus.Ok, result.ErrorMessage);
            if (checkStoredRow)
                Assert.Equal(new string?[] { "1", "first", "tagged" }, Assert.Single(result.Results).Row);
        }
        finally
        {
            await Sql(transport, sid, $"DROP TABLE IF EXISTS tempdb.dbo.[{name}];");
        }
    }
}
