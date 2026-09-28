using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 040 (T048, contracts/ipc.md) — the History DTO keys added for grouped actions, open
/// state and restore round-trip, and payloads from an older peer (without them) still load.
/// </summary>
public class HistoryActionContractTests
{
    private static T RoundTrip<T>(T value) =>
        MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value));

    [Fact]
    public void Action_request_keys_9_to_12_round_trip()
    {
        var back = RoundTrip(new HistoryActionRequest
        {
            Action = HistoryActions.ReconcileOpen,
            GroupScope = true,
            SessionKey = "tab-A",
            OwnerPid = 4242,
            OpenSessionKeys = new[] { "tab-A", "tab-B" },
        });

        Assert.Equal(11, HistoryActions.ReconcileOpen);
        Assert.Equal(HistoryActions.ReconcileOpen, back.Action);
        Assert.True(back.GroupScope);
        Assert.Equal("tab-A", back.SessionKey);
        Assert.Equal(4242, back.OwnerPid);
        Assert.Equal(new[] { "tab-A", "tab-B" }, back.OpenSessionKeys);
    }

    [Fact]
    public void Action_response_keys_8_to_11_round_trip()
    {
        var back = RoundTrip(new HistoryActionResponse
        {
            Success = true,
            IsFavorite = true,
            Servers = new[] { "srv1" },
            Databases = new[] { "Northwind" },
            RestorableEntryIds = new long[] { 9, 7 },
        });

        Assert.True(back.IsFavorite);
        Assert.Equal(new[] { "srv1" }, back.Servers);
        Assert.Equal(new[] { "Northwind" }, back.Databases);
        Assert.Equal(new long[] { 9, 7 }, back.RestorableEntryIds);
    }

    [Fact]
    public void Entry_session_key_round_trips()
        => Assert.Equal("tab-A", RoundTrip(new HistoryEntryDto { SqlText = "SELECT 1", SessionKey = "tab-A" }).SessionKey);

    [Fact]
    public void An_older_action_request_loads_with_the_new_keys_empty()
    {
        var back = MessagePackSerializer.Deserialize<HistoryActionRequest>(
            MessagePackSerializer.Serialize(new ActionRequestV8 { Action = HistoryActions.Delete, EntryIds = new long[] { 1 } }));

        Assert.Equal(HistoryActions.Delete, back.Action);
        Assert.Null(back.GroupScope);
        Assert.Null(back.SessionKey);
        Assert.Null(back.OwnerPid);
        Assert.Null(back.OpenSessionKeys);
    }

    [Fact]
    public void An_older_action_response_loads_with_the_new_keys_empty()
    {
        var back = MessagePackSerializer.Deserialize<HistoryActionResponse>(
            MessagePackSerializer.Serialize(new ActionResponseV7 { Success = true, DeletedCount = 3 }));

        Assert.Equal(3, back.DeletedCount);
        Assert.Null(back.IsFavorite);
        Assert.Null(back.Servers);
        Assert.Null(back.Databases);
        Assert.Null(back.RestorableEntryIds);
    }

    [Fact]
    public void An_older_entry_loads_with_no_session_key()
    {
        var back = MessagePackSerializer.Deserialize<HistoryEntryDto>(
            MessagePackSerializer.Serialize(new EntryV16 { Id = 5, SqlText = "SELECT 1" }));

        Assert.Equal(5, back.Id);
        Assert.Null(back.SessionKey);
    }

    /// <summary>HistoryActionRequest before spec 040 (keys 0–8).</summary>
    [MessagePackObject]
    public class ActionRequestV8
    {
        [Key(0)] public int Action { get; set; }
        [Key(1)] public long[] EntryIds { get; set; } = System.Array.Empty<long>();
        [Key(2)] public int? ExportFormat { get; set; }
        [Key(3)] public string? ExportPath { get; set; }
        [Key(4)] public HistorySearchRequest? Filter { get; set; }
        [Key(5)] public string? NewName { get; set; }
        [Key(6)] public bool? IsOpen { get; set; }
        [Key(7)] public string? SqlText { get; set; }
        [Key(8)] public bool? KeepFavorites { get; set; }
    }

    /// <summary>HistoryActionResponse before spec 040 (keys 0–7).</summary>
    [MessagePackObject]
    public class ActionResponseV7
    {
        [Key(0)] public bool Success { get; set; }
        [Key(1)] public string? FullSqlText { get; set; }
        [Key(2)] public string? DiffLeftSql { get; set; }
        [Key(3)] public string? DiffRightSql { get; set; }
        [Key(4)] public string? ExportPath { get; set; }
        [Key(5)] public string? Error { get; set; }
        [Key(6)] public HistoryVersionDto[]? Versions { get; set; }
        [Key(7)] public int DeletedCount { get; set; }
    }

    /// <summary>HistoryEntryDto before spec 040 (keys 0–16).</summary>
    [MessagePackObject]
    public class EntryV16
    {
        [Key(0)] public long Id { get; set; }
        [Key(1)] public string SqlText { get; set; } = string.Empty;
        [Key(2)] public string? Server { get; set; }
        [Key(3)] public string? Database { get; set; }
        [Key(4)] public string? Username { get; set; }
        [Key(5)] public string ExecutedAt { get; set; } = string.Empty;
        [Key(6)] public long DurationMs { get; set; }
        [Key(7)] public long RowCount { get; set; }
        [Key(8)] public int Status { get; set; }
        [Key(9)] public string? ErrorMessage { get; set; }
        [Key(10)] public string? Source { get; set; }
        [Key(11)] public string? TabTitle { get; set; }
        [Key(12)] public bool IsFavorite { get; set; }
        [Key(13)] public int ExecutionCount { get; set; } = 1;
        [Key(14)] public string? ContentHash { get; set; }
        [Key(15)] public bool IsOpen { get; set; }
        [Key(16)] public int VersionCount { get; set; }
    }
}
