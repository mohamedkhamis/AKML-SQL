using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T125, contracts/ipc.md) — the History handler serves US5's requests: the filter
/// menu's server and database lists, draft records, and the <c>path:</c> filter.
/// </summary>
public sealed class HistoryRequestHandlerUs5Tests : IAsyncLifetime
{
    private HistoryTestDb _db = null!;
    private HistoryRequestHandler _handler = null!;

    public async Task InitializeAsync()
    {
        _db = await HistoryTestDb.CreateAsync("akml-handler-us5");
        _handler = new HistoryRequestHandler(_db.Database);
        await _db.RunAsync("SELECT 1", "a", source: @"C:\Reports\q1.sql", server: "srv-b", database: "Northwind");
        await _db.RunAsync("SELECT 2", "b", source: @"C:\Scratch\q2.sql", server: "srv-a", database: "");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request)
    {
        var reply = await (type switch
        {
            MessageTypes.HistoryAction => _handler.HandleActionAsync(Message(type, request)),
            MessageTypes.HistoryRecord => _handler.HandleRecordAsync(Message(type, request)),
            _ => _handler.HandleSearchAsync(Message(type, request)),
        });
        Assert.NotNull(reply);
        return MessagePackSerializer.Deserialize<TResponse>(reply!.Payload!);
    }

    private static RpcMessage Message<T>(int type, T payload) =>
        new() { MessageType = type, RequestId = 11, Payload = MessagePackSerializer.Serialize(payload) };

    [Fact]
    public async Task Get_filter_values_returns_the_servers_and_databases()
    {
        var r = await SendAsync<HistoryActionRequest, HistoryActionResponse>(MessageTypes.HistoryAction,
            new HistoryActionRequest { Action = HistoryActions.GetFilterValues });

        Assert.True(r.Success);
        Assert.Equal(new[] { "srv-a", "srv-b" }, r.Servers);
        Assert.Equal(new[] { "Northwind" }, r.Databases);
    }

    [Fact]
    public async Task A_draft_record_is_stored_as_not_executed_and_not_counted()
    {
        var r = await SendAsync<HistoryRecordRequest, HistoryRecordResponse>(MessageTypes.HistoryRecord,
            new HistoryRecordRequest { SqlText = "SELECT ProductName FROM dbo.Products;", Server = "(local)", Database = "Northwind", SessionKey = "draft", IsDraft = true });

        Assert.True(r.Success);
        Assert.Equal((long)ExecutionStatus.NotExecuted, await _db.ScalarAsync($"SELECT status FROM history WHERE id = {r.EntryId}"));
        var (entries, _) = await _db.Database.SearchAsync(new HistoryFilter { Deduplicate = true, SearchText = "ProductName" });
        Assert.Equal(0, Assert.Single(entries).ExecutionCount);
    }

    [Fact]
    public async Task A_record_that_is_not_a_draft_keeps_its_status()
    {
        var r = await SendAsync<HistoryRecordRequest, HistoryRecordResponse>(MessageTypes.HistoryRecord,
            new HistoryRecordRequest { SqlText = "SELECT 3", Status = (int)ExecutionStatus.Error, SessionKey = "run" });

        Assert.Equal((long)ExecutionStatus.Error, await _db.ScalarAsync($"SELECT status FROM history WHERE id = {r.EntryId}"));
    }

    [Fact]
    public async Task Get_entries_returns_whole_entries_in_the_order_asked()
    {
        var older = await _db.CountAsync("SELECT MIN(id) FROM history");
        var newer = await _db.CountAsync("SELECT MAX(id) FROM history");

        var r = await SendAsync<HistoryActionRequest, HistoryActionResponse>(MessageTypes.HistoryAction,
            new HistoryActionRequest { Action = HistoryActions.GetEntries, EntryIds = new[] { newer, 999999, older } });

        Assert.True(r.Success);
        Assert.Equal(new[] { newer, older }, r.Entries!.Select(e => e.Id).ToArray());
        var first = r.Entries![1];
        Assert.Equal("SELECT 1", first.SqlText);
        Assert.Equal("a", first.SessionKey);
        Assert.Equal("srv-b", first.Server);
        Assert.False(string.IsNullOrEmpty(first.TabTitle));
    }

    [Fact]
    public async Task Group_versions_carry_their_server_and_database()
    {
        var id = await _db.CountAsync("SELECT MIN(id) FROM history");

        var r = await SendAsync<HistoryActionRequest, HistoryActionResponse>(MessageTypes.HistoryAction,
            new HistoryActionRequest { Action = HistoryActions.GetVersions, EntryIds = new[] { id }, GroupScope = true });

        var version = Assert.Single(r.Versions!);
        Assert.Equal("srv-b", version.Server);
        Assert.Equal("Northwind", version.Database);
    }

    [Fact]
    public async Task Search_with_a_path_filter_returns_only_that_source()
    {
        var r = await SendAsync<HistorySearchRequest, HistorySearchResponse>(MessageTypes.HistorySearch,
            new HistorySearchRequest { PathFilter = "Reports", Limit = 50 });

        Assert.True(r.Success);
        Assert.Equal(@"C:\Reports\q1.sql", Assert.Single(r.Entries!).Source);
    }
}
