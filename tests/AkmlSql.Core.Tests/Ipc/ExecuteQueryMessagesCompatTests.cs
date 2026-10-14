using System.Linq;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 041 (T076, research R20, contracts/ipc.md) — the execute reply grows only by appended keys, so an
/// older engine's reply still loads in a newer web bundle (new fields at their defaults) and a newer
/// engine's reply still loads in an older bundle (unknown trailing keys ignored).
/// </summary>
public class ExecuteQueryMessagesCompatTests
{
    [Fact]
    public void An_older_engines_8_key_result_loads_with_the_new_fields_at_their_defaults()
    {
        var legacy = MessagePackSerializer.Serialize(new LegacyExecuteQueryResult
        {
            QueryId = "q1",
            Status = ExecuteStatus.Error,
            ErrorMessage = "boom",
            ResultSets = new[] { new LegacyExecuteResultSet { ColumnNames = new[] { "n" }, Rows = new[] { new string?[] { "1" } } } },
            Messages = new[] { "hello" },
            ElapsedMs = 12,
            TotalRowsAffected = 3,
            ConnectionWasReset = true,
        });

        var back = MessagePackSerializer.Deserialize<ExecuteQueryResult>(legacy);

        Assert.Equal("q1", back.QueryId);
        Assert.Equal(ExecuteStatus.Error, back.Status);
        Assert.Equal(new[] { "hello" }, back.Messages);
        Assert.True(back.ConnectionWasReset);
        Assert.NotNull(back.MessageDetails);
        Assert.Empty(back.MessageDetails);
        Assert.NotNull(back.Batches);
        Assert.Empty(back.Batches);
        Assert.Equal(0, back.CompletedAtUnixMs);
        Assert.Equal(0, back.MessagesOmitted);
        Assert.Null(back.CurrentDatabase);
        var set = Assert.Single(back.ResultSets);
        Assert.Equal(0, set.BatchIndex);
        Assert.Equal("1", set.Rows[0][0]);
    }

    [Fact]
    public void A_newer_engines_13_key_result_loads_in_an_older_bundle()
    {
        var current = MessagePackSerializer.Serialize(new ExecuteQueryResult
        {
            QueryId = "q2",
            Status = ExecuteStatus.Ok,
            Messages = new[] { "(1 row affected)" },
            ResultSets = new[] { new ExecuteResultSet { ColumnNames = new[] { "n" }, BatchIndex = 2 } },
            MessageDetails = new[] { new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, RowCount = 1, Text = "(1 row affected)" } },
            Batches = new[] { new ExecuteBatchDto { Index = 0, StartLine = 1 } },
            CompletedAtUnixMs = 1_700_000_000_000,
            MessagesOmitted = 0,
            CurrentDatabase = "tempdb",
        });

        var old = MessagePackSerializer.Deserialize<LegacyExecuteQueryResult>(current);

        Assert.Equal("q2", old.QueryId);
        Assert.Equal(new[] { "(1 row affected)" }, old.Messages);
        Assert.Equal(new[] { "n" }, Assert.Single(old.ResultSets).ColumnNames);
    }

    [Fact]
    public void A_message_round_trips_with_the_contract_key_order()
    {
        var message = new ExecuteMessageDto
        {
            Kind = ExecuteMessageKind.Error, Text = "bad", BatchIndex = 1, Number = 208, Severity = 16, State = 1,
            Line = 4, Procedure = "dbo.p", ResultSetIndex = 2, RowCount = 9,
        };

        var bytes = MessagePackSerializer.Serialize(message);
        var slots = MessagePackSerializer.Deserialize<object[]>(bytes);
        var back = MessagePackSerializer.Deserialize<ExecuteMessageDto>(bytes);

        // Keys 0-9 are fixed; later specs append (spec 042 added 10-13), so the positions are pinned, not the length.
        Assert.True(slots.Length >= 10);
        Assert.Equal(ExecuteMessageKind.Error, System.Convert.ToInt32(slots[0]));
        Assert.Equal("bad", slots[1]);
        Assert.Equal(1, System.Convert.ToInt32(slots[2]));
        Assert.Equal(208, System.Convert.ToInt32(slots[3]));
        Assert.Equal(16, System.Convert.ToInt32(slots[4]));
        Assert.Equal(1, System.Convert.ToInt32(slots[5]));
        Assert.Equal(4, System.Convert.ToInt32(slots[6]));
        Assert.Equal("dbo.p", slots[7]);
        Assert.Equal(2, System.Convert.ToInt32(slots[8]));
        Assert.Equal(9L, System.Convert.ToInt64(slots[9]));
        Assert.Equal("dbo.p", back.Procedure);
        Assert.Equal(9, back.RowCount);
    }

    [Fact]
    public void A_message_without_a_set_or_a_count_says_so_with_minus_one()
    {
        var m = new ExecuteMessageDto();
        Assert.Equal(-1, m.ResultSetIndex);
        Assert.Equal(-1, m.RowCount);
    }

    [Fact]
    public void A_batch_round_trips_with_the_contract_key_order()
    {
        var batch = new ExecuteBatchDto { Index = 3, StartLine = 12, StartOffset = 100, EndOffset = 140, Status = ExecuteStatus.Skipped, ElapsedMs = 7, RepeatCount = 2, ErrorCount = 1 };

        var bytes = MessagePackSerializer.Serialize(batch);
        var slots = MessagePackSerializer.Deserialize<object[]>(bytes);

        Assert.Equal(8, slots.Length);
        Assert.Equal(new long[] { 3, 12, 100, 140, ExecuteStatus.Skipped, 7, 2, 1 }, slots.Select(System.Convert.ToInt64).ToArray());
        Assert.Equal(2, MessagePackSerializer.Deserialize<ExecuteBatchDto>(bytes).RepeatCount);
    }

    [Fact]
    public void The_result_and_set_keys_are_appended_in_contract_order()
    {
        var result = MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(new ExecuteQueryResult { CurrentDatabase = "db" }));
        // Position, not length: spec 042 appended TransactionState (13) after CurrentDatabase (12).
        Assert.True(result.Length >= 13);
        Assert.Equal("db", result[12]);

        var set = MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(new ExecuteResultSet { BatchIndex = 4 }));
        Assert.Equal(12, set.Length);
        Assert.Equal(4, System.Convert.ToInt32(set[11]));
    }

    [Fact]
    public void Parse_has_its_own_adjacent_pair_and_the_new_statuses_follow_the_old()
    {
        Assert.Equal(219, MessageTypes.ExecuteParse);
        Assert.Equal(220, MessageTypes.ExecuteParseResult);
        Assert.Equal(5, ExecuteStatus.Skipped);
        Assert.Equal(6, ExecuteStatus.Disconnected);
        Assert.Equal(7, ExecuteStatus.NoReply);
    }

    /// <summary>Mirror of ExecuteQueryResult before spec 041 (keys 0–7).</summary>
    [MessagePackObject]
    public class LegacyExecuteQueryResult
    {
        [Key(0)] public string QueryId { get; set; } = string.Empty;
        [Key(1)] public int Status { get; set; }
        [Key(2)] public string? ErrorMessage { get; set; }
        [Key(3)] public LegacyExecuteResultSet[] ResultSets { get; set; } = System.Array.Empty<LegacyExecuteResultSet>();
        [Key(4)] public string[] Messages { get; set; } = System.Array.Empty<string>();
        [Key(5)] public long ElapsedMs { get; set; }
        [Key(6)] public int TotalRowsAffected { get; set; }
        [Key(7)] public bool ConnectionWasReset { get; set; }
    }

    /// <summary>Mirror of ExecuteResultSet before spec 041 (keys 0–10).</summary>
    [MessagePackObject]
    public class LegacyExecuteResultSet
    {
        [Key(0)] public string[] ColumnNames { get; set; } = System.Array.Empty<string>();
        [Key(1)] public string[] ColumnSqlTypes { get; set; } = System.Array.Empty<string>();
        [Key(2)] public int[] ClrTypeHints { get; set; } = System.Array.Empty<int>();
        [Key(3)] public string?[][] Rows { get; set; } = System.Array.Empty<string?[]>();
        [Key(4)] public bool Truncated { get; set; }
        [Key(5)] public int RowsOmitted { get; set; }
        [Key(6)] public ColumnProvenanceDto[] Provenance { get; set; } = System.Array.Empty<ColumnProvenanceDto>();
        [Key(7)] public bool IsEditable { get; set; }
        [Key(8)] public string? BaseSchema { get; set; }
        [Key(9)] public string? BaseTable { get; set; }
        [Key(10)] public string? BaseCatalog { get; set; }
    }
}
