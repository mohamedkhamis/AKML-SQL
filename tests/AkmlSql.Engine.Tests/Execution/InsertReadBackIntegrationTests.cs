using System;
using System.Data;
using System.Linq;
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
/// Review round 3 (REV-06, REV-09) end to end: a grid insert returns the row as SQL Server stored it — after the table's triggers
/// ran, with server-generated keys — whether the table is in the session's database or, by a three-part name, another one. The
/// tables are uniquely named in the local server's tempdb and dropped afterwards; SKIPPED when no local SQL Server is reachable.
/// Setup runs on a tempdb session, the edits on a session whose database is <c>master</c> unless a test says otherwise.
/// </summary>
[Collection("ExecuteQueryIntegration")]
public sealed class InsertReadBackIntegrationTests
{
    private static string ConnString(string database) =>
        $"Server=(local);Database={database};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=15";

    private static bool TryReachLocalSql()
    {
        try
        {
            using var conn = new SqlConnection(ConnString("tempdb"));
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class Engine
    {
        public required InProcessTransport Transport { get; init; }
        public string Setup { get; } = Guid.NewGuid().ToString("N");
        public string Editor { get; } = Guid.NewGuid().ToString("N");
        private int _id;
        public int NextId() => Interlocked.Increment(ref _id);
    }

    private static async Task<Engine> StartAsync(string editorDatabase = "master")
    {
        var comp = EngineComposition.Build();
        var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => comp.Router.RouteAsync(msg, comp.Context, ct);
        await transport.StartAsync(CancellationToken.None);
        var engine = new Engine { Transport = transport };
        foreach (var (sid, db) in new[] { (engine.Setup, "tempdb"), (engine.Editor, editorDatabase) })
        {
            await transport.SendAsync(new RpcMessage
            {
                MessageType = MessageTypes.ConnectionChanged,
                Payload = MessagePackSerializer.Serialize(new ConnectionInfo { SessionId = sid, DatabaseName = db, ConnectionString = ConnString(db) }),
            }, CancellationToken.None);
        }
        return engine;
    }

    private static async Task<TResponse> SendAsync<TRequest, TResponse>(Engine engine, int type, TRequest request)
    {
        var reply = await engine.Transport.SendAsync(new RpcMessage { MessageType = type, RequestId = engine.NextId(), Payload = MessagePackSerializer.Serialize(request) },
            CancellationToken.None);
        return MessagePackSerializer.Deserialize<TResponse>(reply!.Payload!);
    }

    /// <summary>Runs <paramref name="sql"/> in tempdb and returns the first result set's rows.</summary>
    private static async Task<string?[][]> SqlAsync(Engine engine, string sql)
    {
        var result = await SendAsync<ExecuteQueryRequest, ExecuteQueryResult>(engine, MessageTypes.ExecuteQuery, new ExecuteQueryRequest
        {
            SessionId = engine.Setup, Sql = sql, MaxRows = 100, CommandTimeoutSeconds = 30, QueryId = Guid.NewGuid().ToString("N"),
        });
        Assert.True(result.Status == ExecuteStatus.Ok, result.ErrorMessage);
        return result.ResultSets.FirstOrDefault()?.Rows ?? Array.Empty<string?[]>();
    }

    private static Task<ApplyChangesResult> ApplyAsync(Engine engine, string table, string[] returnColumns, params CrudEditDto[] edits) =>
        SendAsync<ApplyChangesRequest, ApplyChangesResult>(engine, MessageTypes.ApplyChanges, new ApplyChangesRequest
        {
            SessionId = engine.Editor, BaseCatalog = "tempdb", BaseSchema = "dbo", BaseTable = table, ReturnColumns = returnColumns, Edits = edits,
        });

    private static CrudCellDto Text(string column, string? value) =>
        new() { BaseColumnName = column, ProviderType = (int)SqlDbType.NVarChar, Size = 50, Value = value };

    private static CrudEditDto Insert(params CrudCellDto[] cells) => new() { Op = CrudOp.Insert, SetCells = cells };

    private static string NewName(string what) => "akml_rb_" + what + "_" + Guid.NewGuid().ToString("N");

    private static Task DropAsync(Engine engine, params string[] tables) =>
        SqlAsync(engine, string.Concat(tables.Select(t => $"DROP TABLE IF EXISTS dbo.[{t}]; ")));

    private static void AssertOk(ApplyChangesResult result) => Assert.True(result.Status == ExecuteStatus.Ok, result.ErrorMessage);

    [SkippableFact]
    public async Task From_another_database_an_insert_returns_the_stored_row_with_an_enabled_a_disabled_or_no_trigger()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local).");
        var engine = await StartAsync();
        string plain = NewName("plain"), enabled = NewName("enabled"), disabled = NewName("disabled");
        try
        {
            foreach (var t in new[] { plain, enabled, disabled })
                await SqlAsync(engine, $"CREATE TABLE dbo.[{t}] (Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL, Tag nvarchar(50) NOT NULL DEFAULT N'tagged');");
            await SqlAsync(engine, $"CREATE TRIGGER dbo.[trg_{enabled}] ON dbo.[{enabled}] AFTER INSERT AS SET NOCOUNT ON;");
            await SqlAsync(engine, $"CREATE TRIGGER dbo.[trg_{disabled}] ON dbo.[{disabled}] AFTER INSERT AS SET NOCOUNT ON;");
            await SqlAsync(engine, $"DISABLE TRIGGER dbo.[trg_{disabled}] ON dbo.[{disabled}];");

            foreach (var t in new[] { plain, enabled, disabled })
            {
                var result = await ApplyAsync(engine, t, new[] { "Id", "Name", "Tag" }, Insert(Text("Name", "first")));
                AssertOk(result);
                Assert.Equal(new string?[] { "1", "first", "tagged" }, Assert.Single(result.Results).Row);
                Assert.Equal("1", result.Results[0].NewIdentity);
            }
        }
        finally
        {
            await DropAsync(engine, plain, enabled, disabled);
        }
    }

    [SkippableFact]
    public async Task What_an_AFTER_trigger_changed_is_what_comes_back_in_the_session_database_and_from_another()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local).");
        foreach (var database in new[] { "tempdb", "master" })
        {
            var engine = await StartAsync(database);
            var t = NewName("changed");
            try
            {
                await SqlAsync(engine, $"CREATE TABLE dbo.[{t}] (Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL, Tag nvarchar(50) NOT NULL DEFAULT N'tagged');");
                await SqlAsync(engine,
                    $"CREATE TRIGGER dbo.[trg_{t}] ON dbo.[{t}] AFTER INSERT AS BEGIN SET NOCOUNT ON; " +
                    $"UPDATE x SET Tag = N'by trigger', Name = UPPER(x.Name) FROM dbo.[{t}] AS x JOIN inserted AS i ON i.Id = x.Id; END;");

                var result = await ApplyAsync(engine, t, new[] { "Id", "Name", "Tag" }, Insert(Text("Name", "first")), Insert(Text("Name", "second")));

                AssertOk(result);
                Assert.Equal(new string?[] { "1", "FIRST", "by trigger" }, result.Results[0].Row);
                Assert.Equal(new string?[] { "2", "SECOND", "by trigger" }, result.Results[1].Row);
            }
            finally
            {
                await DropAsync(engine, t);
            }
        }
    }

    [SkippableFact]
    public async Task A_server_generated_key_is_read_back_and_the_next_update_and_delete_find_the_rows_by_it()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local).");
        var engine = await StartAsync();
        var t = NewName("guid");
        try
        {
            await SqlAsync(engine, $"CREATE TABLE dbo.[{t}] (Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY, Name nvarchar(50) NOT NULL);");
            await SqlAsync(engine, $"CREATE TRIGGER dbo.[trg_{t}] ON dbo.[{t}] AFTER INSERT AS SET NOCOUNT ON;");

            var inserted = await ApplyAsync(engine, t, new[] { "Id", "Name" }, Insert(Text("Name", "keep")), Insert(Text("Name", "drop")));
            AssertOk(inserted);
            var keep = inserted.Results[0].Row!;
            var drop = inserted.Results[1].Row!;
            Assert.True(Guid.TryParse(keep[0], out _), keep[0]);
            Assert.NotEqual(keep[0], drop[0]);
            Assert.Null(inserted.Results[0].NewIdentity);   // not an identity column

            CrudCellDto Key(string? id) => new() { BaseColumnName = "Id", ProviderType = (int)SqlDbType.UniqueIdentifier, Value = id };
            var edited = await ApplyAsync(engine, t, new[] { "Id", "Name" },
                new CrudEditDto { Op = CrudOp.Update, SetCells = new[] { Text("Name", "kept") }, KeyCells = new[] { Key(keep[0]) } },
                new CrudEditDto { Op = CrudOp.Delete, KeyCells = new[] { Key(drop[0]) } });
            AssertOk(edited);

            var rows = await SqlAsync(engine, $"SELECT CONVERT(nvarchar(36), Id), Name FROM dbo.[{t}];");
            var row = Assert.Single(rows);
            Assert.Equal(keep[0], row[0], ignoreCase: true);
            Assert.Equal("kept", row[1]);
        }
        finally
        {
            await DropAsync(engine, t);
        }
    }

    [SkippableFact]
    public async Task A_composite_key_with_its_own_collation_is_compared_in_the_table_database()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local).");
        var engine = await StartAsync();
        var t = NewName("collated");
        try
        {
            // A collation neither database defaults to: a table variable declared without it could not be compared to the column.
            await SqlAsync(engine,
                $"CREATE TABLE dbo.[{t}] (Code nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL, Seq int NOT NULL, " +
                "Note nvarchar(50) NOT NULL DEFAULT N'noted', CONSTRAINT [PK_" + t + "] PRIMARY KEY (Code, Seq));");
            await SqlAsync(engine, $"CREATE TRIGGER dbo.[trg_{t}] ON dbo.[{t}] AFTER INSERT AS SET NOCOUNT ON;");

            var result = await ApplyAsync(engine, t, new[] { "Code", "Seq", "Note" },
                Insert(Text("Code", "abc"), new CrudCellDto { BaseColumnName = "Seq", ProviderType = (int)SqlDbType.Int, Value = "7" }));

            AssertOk(result);
            Assert.Equal(new string?[] { "abc", "7", "noted" }, Assert.Single(result.Results).Row);
        }
        finally
        {
            await DropAsync(engine, t);
        }
    }

    [SkippableFact]
    public async Task An_INSTEAD_OF_trigger_that_writes_elsewhere_commits_and_returns_no_row()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local).");
        var engine = await StartAsync();
        string t = NewName("instead"), audit = NewName("instead_audit");
        try
        {
            await SqlAsync(engine, $"CREATE TABLE dbo.[{audit}] (Name nvarchar(50) NOT NULL);");
            await SqlAsync(engine, $"CREATE TABLE dbo.[{t}] (Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL);");
            await SqlAsync(engine,
                $"CREATE TRIGGER dbo.[trg_{t}] ON dbo.[{t}] INSTEAD OF INSERT AS BEGIN SET NOCOUNT ON; " +
                $"INSERT INTO dbo.[{audit}] (Name) SELECT Name FROM inserted; END;");

            var result = await ApplyAsync(engine, t, new[] { "Id", "Name" }, Insert(Text("Name", "rerouted")));

            AssertOk(result);
            Assert.Null(Assert.Single(result.Results).Row);   // nothing was stored under a key the grid could show
            Assert.Equal("rerouted", Assert.Single(await SqlAsync(engine, $"SELECT Name FROM dbo.[{audit}];"))[0]);
            Assert.Empty(await SqlAsync(engine, $"SELECT Id FROM dbo.[{t}];"));
        }
        finally
        {
            await DropAsync(engine, t, audit);
        }
    }

    [SkippableFact]
    public async Task An_INSTEAD_OF_insert_must_not_return_an_existing_row_with_the_attempted_key()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local).");
        var engine = await StartAsync();
        string t = NewName("instead_collision"), audit = NewName("collision_audit");
        try
        {
            await SqlAsync(engine, $"CREATE TABLE dbo.[{t}] (Id int PRIMARY KEY, Name nvarchar(50) NOT NULL); INSERT dbo.[{t}] VALUES (7, N'existing');");
            await SqlAsync(engine, $"CREATE TABLE dbo.[{audit}] (Name nvarchar(50) NOT NULL);");
            await SqlAsync(engine, $"CREATE TRIGGER dbo.[trg_{t}] ON dbo.[{t}] INSTEAD OF INSERT AS BEGIN SET NOCOUNT ON; INSERT dbo.[{audit}] SELECT Name FROM inserted; END;");

            var result = await ApplyAsync(engine, t, new[] { "Id", "Name" }, Insert(
                new CrudCellDto { BaseColumnName = "Id", ProviderType = (int)SqlDbType.Int, Value = "7" }, Text("Name", "new")));

            AssertOk(result);
            Assert.Null(Assert.Single(result.Results).Row);
            Assert.Equal("existing", Assert.Single(await SqlAsync(engine, $"SELECT Name FROM dbo.[{t}];"))[0]);
            Assert.Equal("new", Assert.Single(await SqlAsync(engine, $"SELECT Name FROM dbo.[{audit}];"))[0]);
        }
        finally
        {
            await DropAsync(engine, t, audit);
        }
    }

    [SkippableFact]
    public async Task A_table_without_a_key_returns_its_row_only_when_no_trigger_can_change_it()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local).");
        var engine = await StartAsync();
        string heap = NewName("heap"), triggered = NewName("heap_trg");
        try
        {
            foreach (var t in new[] { heap, triggered })
                await SqlAsync(engine, $"CREATE TABLE dbo.[{t}] (Name nvarchar(50) NOT NULL, Tag nvarchar(50) NOT NULL DEFAULT N'tagged');");
            await SqlAsync(engine, $"CREATE TRIGGER dbo.[trg_{triggered}] ON dbo.[{triggered}] AFTER INSERT AS SET NOCOUNT ON;");

            var plain = await ApplyAsync(engine, heap, new[] { "Name", "Tag" }, Insert(Text("Name", "first")));
            AssertOk(plain);
            Assert.Equal(new string?[] { "first", "tagged" }, Assert.Single(plain.Results).Row);

            var withTrigger = await ApplyAsync(engine, triggered, new[] { "Name", "Tag" }, Insert(Text("Name", "first")));
            AssertOk(withTrigger);
            Assert.Null(Assert.Single(withTrigger.Results).Row);    // committed, but the grid must refresh to see it
            Assert.Single(await SqlAsync(engine, $"SELECT Name FROM dbo.[{triggered}];"));
        }
        finally
        {
            await DropAsync(engine, heap, triggered);
        }
    }

    [SkippableFact]
    public async Task A_failed_insert_rolls_back_every_edit_of_the_apply_in_another_database()
    {
        Skip.IfNot(TryReachLocalSql(), "No local SQL Server reachable on (local).");
        var engine = await StartAsync();
        var t = NewName("rollback");
        try
        {
            await SqlAsync(engine, $"CREATE TABLE dbo.[{t}] (Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL);");
            await SqlAsync(engine, $"INSERT INTO dbo.[{t}] (Name) VALUES (N'before');");
            await SqlAsync(engine, $"CREATE TRIGGER dbo.[trg_{t}] ON dbo.[{t}] AFTER INSERT AS SET NOCOUNT ON;");

            var result = await ApplyAsync(engine, t, new[] { "Id", "Name" },
                new CrudEditDto
                {
                    Op = CrudOp.Update, SetCells = new[] { Text("Name", "changed") },
                    KeyCells = new[] { new CrudCellDto { BaseColumnName = "Id", ProviderType = (int)SqlDbType.Int, Value = "1" } },
                },
                Insert(Text("Name", "good")),
                Insert(Text("Name", null)));   // NOT NULL: fails, and everything before it rolls back

            Assert.Equal(ExecuteStatus.Error, result.Status);
            Assert.All(result.Results, r => Assert.False(r.Ok));
            var rows = await SqlAsync(engine, $"SELECT Id, Name FROM dbo.[{t}];");
            Assert.Equal(new string?[] { "1", "before" }, Assert.Single(rows));
        }
        finally
        {
            await DropAsync(engine, t);
        }
    }
}
