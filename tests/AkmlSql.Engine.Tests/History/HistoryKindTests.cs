using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Models.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 042 (FR-020, FR-070 to FR-072, data-model §10) - history schema v4: every entry and query
/// session records the database kind (default <c>sqlserver</c>), search can filter on it, and entries of
/// different kinds are never grouped together. The migration only adds columns, so a database written by
/// an older engine keeps working and an older engine keeps reading a newer database.
/// </summary>
public sealed class HistoryKindTests : IAsyncLifetime
{
    private HistoryTestDb _db = null!;

    public async Task InitializeAsync() => _db = await HistoryTestDb.CreateAsync("akml-kind");

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private Task<long> RunAsync(string sql, DatabaseKind kind, string? sessionKey = null, string server = "srv1", string database = "northwind") =>
        _db.Database.InsertEntryAsync(sql, false, server, database, "user", 5, 1, (int)ExecutionStatus.Success, null,
            source: null, tabTitle: null, sessionKey: sessionKey, kind: (int)kind);

    private async Task<List<AkmlSql.Core.Ipc.Messages.HistoryEntryDto>> SearchAsync(Action<HistoryFilter>? configure = null, bool group = false)
    {
        var filter = new HistoryFilter { Limit = 100, Deduplicate = group };
        configure?.Invoke(filter);
        return (await _db.Database.SearchAsync(filter)).Entries;
    }

    [Fact]
    public async Task A_fresh_database_is_schema_v4_with_kind_columns_defaulting_to_sqlserver()
    {
        Assert.Equal("4", (await _db.ScalarAsync("SELECT value FROM metadata WHERE key = 'schema_version'"))?.ToString());
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM pragma_table_info('history') WHERE name = 'kind'"));
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM pragma_table_info('query_sessions') WHERE name = 'kind'"));

        // A row inserted the way an older engine does (no kind column in the statement) lands as SQL Server.
        await _db.ExecAsync(@"INSERT INTO history (sql_text, executed_at, duration_ms, status, content_hash)
                              VALUES ('select 1', '2026-10-14T10:00:00.0000000Z', 1, 0, 'h1');");
        Assert.Equal("sqlserver", (await _db.ScalarAsync("SELECT kind FROM history LIMIT 1"))?.ToString());
    }

    [Fact]
    public async Task A_v3_database_upgrades_in_place_and_keeps_its_rows_as_SQL_Server()
    {
        await RunAsync("select 1", DatabaseKind.SqlServer, "k1");

        // Turn the database back into what the previous engine left: no kind columns, version 3.
        await _db.ExecAsync("DROP INDEX IF EXISTS IX_history_kind_server_db;");
        await _db.ExecAsync("DROP INDEX IF EXISTS IX_history_group_kind;");
        await _db.ExecAsync("ALTER TABLE history DROP COLUMN kind;");
        await _db.ExecAsync("ALTER TABLE query_sessions DROP COLUMN kind;");
        await _db.ExecAsync("UPDATE metadata SET value = '3' WHERE key = 'schema_version';");
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM pragma_table_info('history') WHERE name = 'kind'"));

        await _db.Database.InitializeAsync();
        await _db.Database.InitializeAsync(); // idempotent

        Assert.Equal("4", (await _db.ScalarAsync("SELECT value FROM metadata WHERE key = 'schema_version'"))?.ToString());
        Assert.Equal("sqlserver", (await _db.ScalarAsync("SELECT kind FROM history LIMIT 1"))?.ToString());
        var entry = Assert.Single(await SearchAsync());
        Assert.Equal((int)DatabaseKind.SqlServer, entry.Kind);
    }

    [Fact]
    public async Task The_kind_round_trips_through_record_and_search()
    {
        await RunAsync("select 'sql server'", DatabaseKind.SqlServer);
        await RunAsync("select 'postgres'", DatabaseKind.PostgreSql);

        var entries = await SearchAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal((int)DatabaseKind.PostgreSql, entries.Single(e => e.SqlText.Contains("postgres")).Kind);
        Assert.Equal((int)DatabaseKind.SqlServer, entries.Single(e => e.SqlText.Contains("sql server")).Kind);

        var byId = await _db.Database.GetEntriesAsync(entries.Select(e => e.Id).ToList());
        Assert.Equal((int)DatabaseKind.PostgreSql, byId.Single(e => e.SqlText.Contains("postgres")).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_filters_by_kind_and_null_means_both(bool grouped)
    {
        await RunAsync("select 1 /*ms*/", DatabaseKind.SqlServer, "s-ms");
        await RunAsync("select 2 /*pg*/", DatabaseKind.PostgreSql, "s-pg");

        Assert.Equal(2, (await SearchAsync(group: grouped)).Count);

        var onlyPg = await SearchAsync(f => f.Kind = (int)DatabaseKind.PostgreSql, grouped);
        Assert.Equal("select 2 /*pg*/", Assert.Single(onlyPg).SqlText);

        var onlyMs = await SearchAsync(f => f.Kind = (int)DatabaseKind.SqlServer, grouped);
        Assert.Equal("select 1 /*ms*/", Assert.Single(onlyMs).SqlText);
    }

    [Fact]
    public async Task Identical_text_run_on_both_kinds_is_two_groups_not_one()
    {
        // No session key: the grouped view falls back to the content hash, which is the same for both.
        await RunAsync("select now()", DatabaseKind.SqlServer);
        await RunAsync("select now()", DatabaseKind.PostgreSql);

        var groups = await SearchAsync(group: true);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 0, 1 }, groups.Select(e => e.Kind).OrderBy(k => k).ToArray());
    }

    [Fact]
    public async Task One_editor_session_that_ran_on_both_kinds_lists_each_kind_as_its_own_group()
    {
        await RunAsync("select 1", DatabaseKind.SqlServer, "doc-1");
        await RunAsync("select 1", DatabaseKind.SqlServer, "doc-1");
        await RunAsync("select 1", DatabaseKind.PostgreSql, "doc-1");

        var groups = await SearchAsync(group: true);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Single(g => g.Kind == (int)DatabaseKind.SqlServer).ExecutionCount);
        Assert.Equal(1, groups.Single(g => g.Kind == (int)DatabaseKind.PostgreSql).ExecutionCount);
    }

    [Fact]
    public async Task A_new_session_records_the_kind_of_its_first_entry()
    {
        await RunAsync("select 1", DatabaseKind.PostgreSql, "pg-doc");
        await RunAsync("select 1", DatabaseKind.SqlServer, "ms-doc");

        Assert.Equal("postgresql", (await _db.ScalarAsync("SELECT kind FROM query_sessions WHERE session_key = 'pg-doc'"))?.ToString());
        Assert.Equal("sqlserver", (await _db.ScalarAsync("SELECT kind FROM query_sessions WHERE session_key = 'ms-doc'"))?.ToString());
    }
}
