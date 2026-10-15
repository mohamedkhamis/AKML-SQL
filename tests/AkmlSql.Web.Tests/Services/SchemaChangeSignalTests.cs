using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>Spec 042 (FR-043): when the browser refetches the schema after a PostgreSQL run.</summary>
public sealed class SchemaChangeSignalTests
{
    private static ExecuteQueryResult Run(bool schemaChanged, TransactionState state, params string[] tags) => new()
    {
        SchemaChanged = schemaChanged,
        TransactionState = (int)state,
        MessageDetails = tags.Select(t => new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, Text = t }).ToArray(),
    };

    [Fact]
    public void The_engines_word_is_followed_whatever_the_transaction_state()
    {
        Assert.True(SchemaChangeSignal.RefetchAfter(Run(true, TransactionState.None, "COMMIT")));
        Assert.True(SchemaChangeSignal.RefetchAfter(Run(true, TransactionState.Open, "CREATE TABLE")));
    }

    [Fact]
    public void An_engine_from_before_the_flag_is_read_by_its_tags_outside_a_transaction()
    {
        Assert.True(SchemaChangeSignal.RefetchAfter(Run(false, TransactionState.None, "INSERT 0 1", "CREATE TABLE")));
        Assert.False(SchemaChangeSignal.RefetchAfter(Run(false, TransactionState.Open, "CREATE TABLE")));     // not visible yet
        Assert.False(SchemaChangeSignal.RefetchAfter(Run(false, TransactionState.None, "INSERT 0 1")));
    }

    [Fact]
    public void A_reply_without_messages_asks_for_nothing()
    {
        Assert.False(SchemaChangeSignal.RefetchAfter(new ExecuteQueryResult()));
        Assert.False(SchemaChangeSignal.RefetchAfter(new ExecuteQueryResult { MessageDetails = null! }));
    }
}
