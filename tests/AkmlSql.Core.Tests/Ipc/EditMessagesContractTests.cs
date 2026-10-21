using System.Reflection;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 042 (task T141, FR-130 to FR-133, contracts/ipc.md "Appended keys for editing"). The edit messages grow only by appended
/// keys: the new fields sit at fixed positions, travel through a round trip, and an older sender's payload loads with them empty.
/// </summary>
public class EditMessagesContractTests
{
    private static int KeyOf<T>(string property) =>
        (int)typeof(T).GetProperty(property)!.GetCustomAttribute<KeyAttribute>()!.IntKey!;

    [Fact]
    public void The_new_fields_are_at_their_contracted_positions()
    {
        Assert.Equal(12, KeyOf<ExecuteResultSet>(nameof(ExecuteResultSet.ReadOnlyReason)));
        Assert.Equal(5, KeyOf<ApplyChangesRequest>(nameof(ApplyChangesRequest.ReturnColumns)));
        Assert.Equal(5, KeyOf<CrudEditResult>(nameof(CrudEditResult.Row)));
        Assert.Equal(6, KeyOf<CrudCellDto>(nameof(CrudCellDto.TypeName)));
    }

    [Fact]
    public void The_edit_request_and_its_reply_round_trip_with_the_new_fields()
    {
        var request = new ApplyChangesRequest
        {
            SessionId = "s", BaseSchema = "public", BaseTable = "items", ReturnColumns = new[] { "id", "name" },
            Edits = new[]
            {
                new CrudEditDto
                {
                    Op = CrudOp.Update,
                    SetCells = new[] { new CrudCellDto { BaseColumnName = "name", Value = "x", TypeName = "text" } },
                    KeyCells = new[] { new CrudCellDto { BaseColumnName = "id", Value = "1", TypeName = "integer" } },
                },
            },
        };
        var reply = new ApplyChangesResult
        {
            Status = ExecuteStatus.Ok,
            Results = new[] { new CrudEditResult { Index = 0, Ok = true, RowsAffected = 1, Row = new string?[] { "1", null } } },
        };

        var request2 = MessagePackSerializer.Deserialize<ApplyChangesRequest>(MessagePackSerializer.Serialize(request));
        var reply2 = MessagePackSerializer.Deserialize<ApplyChangesResult>(MessagePackSerializer.Serialize(reply));

        Assert.Equal(new[] { "id", "name" }, request2.ReturnColumns);
        Assert.Equal("text", request2.Edits[0].SetCells[0].TypeName);
        Assert.Equal("integer", request2.Edits[0].KeyCells[0].TypeName);
        Assert.Equal(new string?[] { "1", null }, reply2.Results[0].Row);
    }

    [Fact]
    public void A_result_set_carries_its_read_only_reason_and_an_older_one_loads_without()
    {
        var set = new ExecuteResultSet { ColumnNames = new[] { "n" }, ReadOnlyReason = "The result comes from a view, and a view cannot be edited." };
        var set2 = MessagePackSerializer.Deserialize<ExecuteResultSet>(MessagePackSerializer.Serialize(set));
        Assert.Equal(set.ReadOnlyReason, set2.ReadOnlyReason);

        // The same set cut off before key 12, as an engine from before this feature writes it.
        var older = new ExecuteResultSet { ColumnNames = new[] { "n" } };
        Assert.Null(MessagePackSerializer.Deserialize<ExecuteResultSet>(MessagePackSerializer.Serialize(older)).ReadOnlyReason);
    }

    [Fact]
    public void An_apply_reply_without_rows_loads_in_a_newer_bundle_with_no_row()
    {
        // A SQL Server engine never fills Row; the browser must treat null as "nothing to merge".
        var reply = MessagePackSerializer.Deserialize<CrudEditResult>(MessagePackSerializer.Serialize(new CrudEditResult { Index = 2, Ok = true, RowsAffected = 1, NewIdentity = "7" }));

        Assert.Null(reply.Row);
        Assert.Equal("7", reply.NewIdentity);
    }
}
