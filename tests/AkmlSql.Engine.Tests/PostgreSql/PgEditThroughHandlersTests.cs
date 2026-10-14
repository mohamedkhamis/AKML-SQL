using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Execution;
using AkmlSql.Engine.Schema;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T141, FR-130 to FR-133). The two handlers together, as the web edition drives them over the wire: a query asked
/// to include provenance says which sets can be edited, an Apply on the same session writes them on the session's own connection,
/// and the user's transaction state is carried between the two.
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class PgEditThroughHandlersTests
{
    private const string Seed = """
        CREATE TABLE people (id serial PRIMARY KEY, name text NOT NULL);
        INSERT INTO people (name) VALUES ('Ada'), ('Alan');
        """;

    private sealed class Rig : IAsyncDisposable
    {
        private readonly PgTestDatabase _database;
        private readonly SessionConnectionRegistry _registry = new();
        private readonly ExecuteQueryHandler _execute;
        private readonly ApplyChangesHandler _apply;

        public Rig(PgTestDatabase database)
        {
            _database = database;
            Func<string, DatabaseKind> kindOf = _ => DatabaseKind.PostgreSql;
            _execute = new ExecuteQueryHandler(_registry, new SchemaCacheManager(), null, null, kindOf);
            _apply = new ApplyChangesHandler(_registry, kindOf);
        }

        public static async Task<Rig> OpenAsync() => new(await PgTestServer.CreateDatabaseAsync(Seed));

        private (string? ConnectionString, string? DatabaseName) Lookup(string _) => (_database.ConnectionString, null);

        public async Task<ExecuteQueryResult> QueryAsync(string sql, bool provenance)
        {
            var request = new RpcMessage
            {
                MessageType = MessageTypes.ExecuteQuery,
                RequestId = 1,
                Payload = MessagePackSerializer.Serialize(new ExecuteQueryRequest
                {
                    SessionId = "s", QueryId = Guid.NewGuid().ToString("N"), Sql = sql, MaxRows = 1000, CommandTimeoutSeconds = 30,
                    IncludeProvenance = provenance,
                }),
            };
            var reply = await _execute.HandleAsync(request, Lookup, CancellationToken.None);
            return MessagePackSerializer.Deserialize<ExecuteQueryResult>(reply!.Payload!);
        }

        public async Task<ApplyChangesResult> ApplyAsync(ApplyChangesRequest apply)
        {
            apply.SessionId = "s";
            var request = new RpcMessage { MessageType = MessageTypes.ApplyChanges, RequestId = 2, Payload = MessagePackSerializer.Serialize(apply) };
            var reply = await _apply.HandleAsync(request, Lookup, CancellationToken.None);
            return MessagePackSerializer.Deserialize<ApplyChangesResult>(reply!.Payload!);
        }

        public async Task<string?> NameOfAsync(int id)
        {
            var result = await QueryAsync($"SELECT name FROM people WHERE id = {id}", provenance: false);
            return result.ResultSets[0].Rows[0][0];
        }

        public ValueTask DisposeAsync()
        {
            _registry.Dispose("s");
            return _database.DisposeAsync();
        }
    }

    private static ApplyChangesRequest Rename(int id, string name) => new()
    {
        BaseSchema = "public", BaseTable = "people", ReturnColumns = new[] { "id", "name" },
        Edits = new[]
        {
            new CrudEditDto
            {
                Op = CrudOp.Update,
                SetCells = new[] { new CrudCellDto { BaseColumnName = "name", Value = name, TypeName = "text" } },
                KeyCells = new[] { new CrudCellDto { BaseColumnName = "id", Value = id.ToString(), TypeName = "integer" } },
            },
        },
    };

    [SkippableFact]
    public async Task A_query_that_asks_for_provenance_returns_an_editable_set_and_one_that_does_not_ask_does_not()
    {
        await using var rig = await Rig.OpenAsync();

        var asked = await rig.QueryAsync("SELECT id, name FROM people ORDER BY id", provenance: true);
        var notAsked = await rig.QueryAsync("SELECT id, name FROM people ORDER BY id", provenance: false);

        var set = Assert.Single(asked.ResultSets);
        Assert.True(set.IsEditable, set.ReadOnlyReason);
        Assert.Equal("people", set.BaseTable);
        Assert.False(Assert.Single(notAsked.ResultSets).IsEditable);
    }

    [SkippableFact]
    public async Task A_read_only_set_carries_the_reason_over_the_wire()
    {
        await using var rig = await Rig.OpenAsync();

        var result = await rig.QueryAsync("SELECT count(*) AS n FROM people", provenance: true);

        var set = Assert.Single(result.ResultSets);
        Assert.False(set.IsEditable);
        Assert.Contains("not a table", set.ReadOnlyReason);
    }

    [SkippableFact]
    public async Task An_apply_writes_the_row_and_returns_it_as_stored()
    {
        await using var rig = await Rig.OpenAsync();
        await rig.QueryAsync("SELECT id, name FROM people", provenance: true);

        var result = await rig.ApplyAsync(Rename(2, "Alan T"));

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Equal(new string?[] { "2", "Alan T" }, result.Results[0].Row);
        Assert.Equal("Alan T", await rig.NameOfAsync(2));
    }

    [SkippableFact]
    public async Task An_apply_inside_the_users_open_transaction_joins_it_and_a_rollback_takes_it_back()
    {
        await using var rig = await Rig.OpenAsync();
        await rig.QueryAsync("BEGIN", provenance: false);

        var result = await rig.ApplyAsync(Rename(1, "Changed"));
        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Equal("Changed", await rig.NameOfAsync(1));      // visible on the same session while the transaction is open

        await rig.QueryAsync("ROLLBACK", provenance: false);
        Assert.Equal("Ada", await rig.NameOfAsync(1));
    }

    [SkippableFact]
    public async Task An_apply_after_the_transaction_was_aborted_is_refused_until_it_is_rolled_back()
    {
        await using var rig = await Rig.OpenAsync();
        await rig.QueryAsync("BEGIN", provenance: false);
        var failed = await rig.QueryAsync("SELECT 1/0", provenance: false);
        Assert.NotEqual(ExecuteStatus.Ok, failed.Status);

        var refused = await rig.ApplyAsync(Rename(1, "Nope"));
        Assert.Equal(ExecuteStatus.Error, refused.Status);
        Assert.Contains("ROLLBACK", refused.ErrorMessage);

        await rig.QueryAsync("ROLLBACK", provenance: false);
        var accepted = await rig.ApplyAsync(Rename(1, "Now"));
        Assert.Equal(ExecuteStatus.Ok, accepted.Status);
    }

    [SkippableFact]
    public async Task A_failing_apply_reports_the_servers_words_and_leaves_the_session_usable()
    {
        await using var rig = await Rig.OpenAsync();
        var bad = Rename(1, "x");
        bad.Edits[0].SetCells[0] = new CrudCellDto { BaseColumnName = "name", Value = null, TypeName = "text" };      // NOT NULL

        var result = await rig.ApplyAsync(bad);

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Contains("not-null constraint", result.Results[0].Error);
        Assert.Equal("Ada", await rig.NameOfAsync(1));
    }
}
