using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

public class HistoryRecordRequestTests
{
    [Fact]
    public void SessionKey_round_trips()
    {
        var original = new HistoryRecordRequest { SqlText = "SELECT 1", SessionKey = "tab-A" };
        var bytes = MessagePackSerializer.Serialize(original);
        var back = MessagePackSerializer.Deserialize<HistoryRecordRequest>(bytes);
        Assert.Equal("tab-A", back.SessionKey);
    }

    /// <summary>
    /// A payload written by an older shell has no Key 11. It must still deserialize, with
    /// SessionKey null — that is the compatibility contract Task 4 relies on.
    /// </summary>
    [Fact]
    public void Missing_session_key_deserializes_as_null()
    {
        var legacy = MessagePackSerializer.Serialize(new HistoryRecordRequestLegacyShape
        {
            SqlText = "SELECT 1"
        });
        var back = MessagePackSerializer.Deserialize<HistoryRecordRequest>(legacy);
        Assert.Null(back.SessionKey);
    }

    /// <summary>Mirror of HistoryRecordRequest as it existed BEFORE Key 11 was added.</summary>
    [MessagePackObject]
    public class HistoryRecordRequestLegacyShape
    {
        [Key(0)] public string SqlText { get; set; } = string.Empty;
        [Key(1)] public bool Truncated { get; set; }
        [Key(2)] public string? Server { get; set; }
        [Key(3)] public string? Database { get; set; }
        [Key(4)] public string? Username { get; set; }
        [Key(5)] public long DurationMs { get; set; }
        [Key(6)] public long RowCount { get; set; }
        [Key(7)] public int Status { get; set; }
        [Key(8)] public string? ErrorMessage { get; set; }
        [Key(9)] public string? Source { get; set; }
        [Key(10)] public string? TabTitle { get; set; }
    }
}
