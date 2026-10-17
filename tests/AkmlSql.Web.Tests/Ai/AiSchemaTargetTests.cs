using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Review finding WEB-07: the AI prompt carries the schema of the database the editor is connected to — never the most recently
/// used snapshot, which may be another database, another server or the other kind. Each cached database here has one table whose
/// name is a sentinel, so a prompt that carried the wrong one is caught.
/// </summary>
public sealed class AiSchemaTargetTests
{
    private const string Sql = "select * from x";

    private static byte[] OneTable(string table) => MessagePackSerializer.Serialize(new SchemaPhasePayload
    {
        Phase = (int)PopulationPhase.PhaseB,
        Schemas =
        [
            new SchemaPhaseSchema
            {
                Name = "dbo",
                Objects = [new SchemaPhaseObject { SchemaName = "dbo", ObjectName = table, ObjectType = (int)DbObjectType.Table,
                    Columns = [new SchemaPhaseColumn { Name = "Id", TypeName = "int", IsPrimaryKey = true }] }],
            },
        ],
    });

    /// <summary>A cache whose reads can be held, to switch the connection while a prompt is being built.</summary>
    private sealed class GatedCache : ISchemaCacheStore
    {
        private readonly ISchemaCacheStore _inner;
        public GatedCache(ISchemaCacheStore inner) => _inner = inner;
        public TaskCompletionSource? Gate { get; set; }

        public async Task<SchemaSnapshot?> GetAsync(string server, string database)
        {
            if (Gate != null) await Gate.Task;
            return await _inner.GetAsync(server, database);
        }
        public Task SetAsync(SchemaSnapshot snapshot) => _inner.SetAsync(snapshot);
        public Task TouchAsync(string server, string database) => _inner.TouchAsync(server, database);
        public Task RemoveAsync(string server, string database) => _inner.RemoveAsync(server, database);
        public Task<IReadOnlyList<SchemaSnapshot>> ListAsync() => _inner.ListAsync();
        public Task ClearAllAsync() => _inner.ClearAllAsync();
        public Task<long> EstimatedSizeAsync(string server, string database) => _inner.EstimatedSizeAsync(server, database);
    }

    private static async Task<(AiSchemaContextProvider Provider, FakeActiveConnection Connection, GatedCache Cache)> BuildAsync(
        AiPrivacyMode mode = AiPrivacyMode.FullSchema)
    {
        var db = new InMemoryIndexedDbAdapter();
        var settings = new AiFeatureSettingsStore(db);
        await settings.SetAsync(new AiFeatureSettings { GlobalDefaultMode = mode });
        var cache = new GatedCache(new SchemaCacheStore(db));
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "srv", DatabaseName = "Hr", PhaseB = OneTable("SentinelHr") });
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "pg://db1:5432", DatabaseName = "Hr", PhaseB = OneTable("SentinelPg") });
        // Sales is used last: the old provider picked it whatever the connection was.
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "srv", DatabaseName = "Sales", PhaseB = OneTable("SentinelSales") });
        await cache.TouchAsync("srv", "Sales");
        var connection = new FakeActiveConnection { Server = "srv", Database = "Hr" };
        return (new AiSchemaContextProvider(cache, settings, connection), connection, cache);
    }

    [Theory]
    [InlineData(AiPrivacyMode.FullSchema)]
    [InlineData(AiPrivacyMode.SchemaNamesOnly)]
    public async Task The_prompt_carries_the_connected_database_not_the_last_used_one(AiPrivacyMode mode)
    {
        var (provider, _, _) = await BuildAsync(mode);

        var text = await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None);

        Assert.Contains("SentinelHr", text);
        Assert.DoesNotContain("SentinelSales", text);
        Assert.DoesNotContain("SentinelPg", text);
    }

    [Fact]
    public async Task A_database_whose_snapshot_has_not_arrived_sends_no_schema()
    {
        var (provider, connection, _) = await BuildAsync();
        connection.Database = "Payroll";

        Assert.Equal(string.Empty, await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None));
    }

    [Fact]
    public async Task A_PostgreSQL_connection_gets_its_own_snapshot_not_the_SQL_Server_one_of_the_same_name()
    {
        var (provider, connection, _) = await BuildAsync();
        connection.Kind = DatabaseKind.PostgreSql;
        connection.Server = "db1:5432";
        connection.SchemaIdentity = "pg://db1:5432";

        var text = await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None);

        Assert.Contains("SentinelPg", text);
        Assert.DoesNotContain("SentinelHr", text);
    }

    [Fact]
    public async Task Without_a_connection_nothing_is_sent()
    {
        var (provider, connection, _) = await BuildAsync();
        connection.IsConnected = false;

        Assert.Equal(string.Empty, await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None));
    }

    [Fact]
    public async Task NoSchema_stays_empty()
    {
        var (provider, _, _) = await BuildAsync(AiPrivacyMode.NoSchema);

        Assert.Equal(string.Empty, await provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None));
    }

    [Fact]
    public async Task A_connection_that_moves_while_the_prompt_is_built_sends_no_schema()
    {
        var (provider, connection, cache) = await BuildAsync();
        cache.Gate = new TaskCompletionSource();

        var pending = provider.GetSchemaTextAsync("explain", Sql, CancellationToken.None);
        connection.Database = "Sales";                       // switched while the snapshot was being read
        cache.Gate.SetResult();

        Assert.Equal(string.Empty, await pending);
    }
}
