using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.History;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 040 (T114, contracts/ipc.md) — the History keys US5 adds: drafts (a query captured without
/// being run), a source-path filter, and the server/database lists for the filter menu. Keys are
/// appended, so an older peer's payloads still load.
/// </summary>
public class HistoryUs5ContractTests
{
    private static T RoundTrip<T>(T value) =>
        MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value));

    [Fact]
    public void Not_executed_is_status_3()
        => Assert.Equal(3, (int)ExecutionStatus.NotExecuted);

    [Fact]
    public void Get_filter_values_is_action_12()
        => Assert.Equal(12, HistoryActions.GetFilterValues);

    [Fact]
    public void Record_request_is_draft_is_key_12()
    {
        var request = new HistoryRecordRequest { SqlText = "SELECT 1", IsDraft = true };
        Assert.True(RoundTrip(request).IsDraft);
        Assert.Equal(13, MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(request)).Length);
    }

    [Fact]
    public void Search_request_path_filter_is_key_13()
    {
        var request = new HistorySearchRequest { PathFilter = "Reports" };
        Assert.Equal("Reports", RoundTrip(request).PathFilter);
        Assert.Equal(14, MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(request)).Length);
    }

    [Fact]
    public void Action_response_filter_values_round_trip()
    {
        var back = RoundTrip(new HistoryActionResponse { Success = true, Servers = new[] { "(local)" }, Databases = new[] { "Northwind" } });
        Assert.Equal(new[] { "(local)" }, back.Servers);
        Assert.Equal(new[] { "Northwind" }, back.Databases);
    }

    [Fact]
    public void An_older_record_request_loads_as_a_real_run()
    {
        var current = MessagePackSerializer.Deserialize<object[]>(
            MessagePackSerializer.Serialize(new HistoryRecordRequest { SqlText = "SELECT 1" }));
        var legacy = MessagePackSerializer.Serialize(current[..12]);

        var back = MessagePackSerializer.Deserialize<HistoryRecordRequest>(legacy);
        Assert.Equal("SELECT 1", back.SqlText);
        Assert.False(back.IsDraft);
    }

    [Fact]
    public void Restore_reads_whole_entries_by_id()
    {
        Assert.Equal(13, HistoryActions.GetEntries);
        var back = RoundTrip(new HistoryActionResponse
        {
            Success = true,
            Entries = new[] { new HistoryEntryDto { Id = 4, SqlText = "SELECT 1", SessionKey = "k4", TabTitle = "q4" } },
        });
        var entry = Assert.Single(back.Entries!);
        Assert.Equal("k4", entry.SessionKey);
        Assert.Equal("q4", entry.TabTitle);
    }

    [Fact]
    public void Versions_carry_their_server_and_database()
    {
        var back = RoundTrip(new HistoryVersionDto { Id = 1, SqlText = "SELECT 1", Server = "(local)", Database = "Northwind" });
        Assert.Equal("(local)", back.Server);
        Assert.Equal("Northwind", back.Database);
    }

    [Fact]
    public void An_older_search_request_loads_without_a_path_filter()
    {
        var current = MessagePackSerializer.Deserialize<object[]>(
            MessagePackSerializer.Serialize(new HistorySearchRequest { SearchText = "orders" }));
        var legacy = MessagePackSerializer.Serialize(current[..13]);

        var back = MessagePackSerializer.Deserialize<HistorySearchRequest>(legacy);
        Assert.Equal("orders", back.SearchText);
        Assert.Null(back.PathFilter);
    }
}
