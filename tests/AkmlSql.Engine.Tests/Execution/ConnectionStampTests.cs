using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Execution;

/// <summary>
/// Review finding WEB-01: an edit read through one connection must never be written through another. The execute reply stamps
/// the connection; Apply refuses a stamp that is not the session's current one, before it opens anything.
/// </summary>
public class ConnectionStampTests
{
    private const string ServerA = "Server=a.example;Database=Shop;User ID=u;Password=p";
    private const string ServerB = "Server=b.example;Database=Shop;User ID=u;Password=p";

    [Fact]
    public void The_stamp_follows_server_login_database_and_kind()
    {
        var a = ConnectionStamp.Of(DatabaseKind.SqlServer, ServerA);
        Assert.Equal(a, ConnectionStamp.Of(DatabaseKind.SqlServer, ServerA));
        Assert.NotEqual(a, ConnectionStamp.Of(DatabaseKind.SqlServer, ServerB));
        Assert.NotEqual(a, ConnectionStamp.Of(DatabaseKind.SqlServer, ServerA.Replace("Database=Shop", "Database=Other")));
        Assert.NotEqual(a, ConnectionStamp.Of(DatabaseKind.SqlServer, ServerA.Replace("User ID=u", "User ID=v")));
        Assert.NotEqual(a, ConnectionStamp.Of(DatabaseKind.PostgreSql, ServerA));
        Assert.Null(ConnectionStamp.Of(DatabaseKind.SqlServer, null));
    }

    [Fact]
    public void The_stamp_does_not_carry_the_connection_string()
    {
        var stamp = ConnectionStamp.Of(DatabaseKind.SqlServer, ServerA)!;
        Assert.DoesNotContain("a.example", stamp);
        Assert.DoesNotContain("Password", stamp);
    }

    [Fact]
    public void An_unstamped_request_from_an_older_web_bundle_is_not_checked() =>
        Assert.True(ConnectionStamp.Allows(null, DatabaseKind.SqlServer, ServerB));

    [Theory]
    [InlineData(DatabaseKind.SqlServer)]
    [InlineData(DatabaseKind.PostgreSql)]
    public async Task Apply_refuses_rows_read_through_another_connection(DatabaseKind kind)
    {
        // Rows fetched from A; the session has since been switched to B, whose table has the same name and keys.
        var handler = new ApplyChangesHandler(new SessionConnectionRegistry(), _ => kind);
        var apply = new ApplyChangesRequest
        {
            SessionId = "s",
            BaseSchema = "dbo",
            BaseTable = "People",
            ConnectionStamp = ConnectionStamp.Of(kind, ServerA),
            Edits = new[]
            {
                new CrudEditDto
                {
                    Op = CrudOp.Update,
                    SetCells = new[] { new CrudCellDto { BaseColumnName = "Name", Value = "x" } },
                    KeyCells = new[] { new CrudCellDto { BaseColumnName = "Id", Value = "1" } },
                },
            },
        };
        var request = new RpcMessage { MessageType = MessageTypes.ApplyChanges, RequestId = 7, Payload = MessagePackSerializer.Serialize(apply) };

        var reply = await handler.HandleAsync(request, _ => (ServerB, "Shop"), CancellationToken.None);
        var result = MessagePackSerializer.Deserialize<ApplyChangesResult>(reply!.Payload!);

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Equal(ConnectionStamp.StaleMessage, result.ErrorMessage);
        Assert.Empty(result.Results);
        Assert.Equal(7, reply.RequestId);
    }
}
