using AkmlSql.Core.Ipc.Messages;
using AkmlSql.PostgreSql.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>
/// UI scan 2026-11-03 (W9): after FROM the PostgreSQL list offered sequences between the tables, and the column list after
/// an alias dot was purely alphabetical where the SQL Server list puts the primary key first.
/// </summary>
public class PgObjectListTests
{
    private static readonly PgCompletionEngine Engine = new();

    private static IReadOnlyList<CompletionItem> Items(string doc)
    {
        var caret = doc.IndexOf('|');
        var response = Engine.GetCompletions(doc.Remove(caret, 1), caret, NorthwindPgTestCacheFactory.Create(), new PgCompletionOptions { MaxSuggestions = 200 });
        return response.Items;
    }

    [Fact]
    public void A_sequence_is_not_offered_after_FROM()
    {
        var items = Items("SELECT * FROM |");

        Assert.Contains(items, i => i.DisplayText == "orders");
        Assert.DoesNotContain(items, i => i.DisplayText == "orders_order_id_seq");
    }

    [Fact]
    public void A_sequence_is_not_offered_after_JOIN()
    {
        var items = Items("SELECT * FROM orders o JOIN |");

        Assert.Contains(items, i => i.DisplayText == "customers");
        Assert.DoesNotContain(items, i => i.DisplayText == "orders_order_id_seq");
    }

    [Fact]
    public void A_sequence_is_still_offered_where_DDL_names_one()
    {
        var items = Items("DROP SEQUENCE |");

        Assert.Contains(items, i => i.DisplayText == "orders_order_id_seq");
    }

    [Fact]
    public void The_primary_key_column_comes_first_after_an_alias_dot()
    {
        var columns = Items("SELECT o.| FROM orders o").Where(i => (CompletionObjectType)i.ObjectType == CompletionObjectType.Column).ToList();

        Assert.True(columns.Count > 2, "the orders columns are expected");
        Assert.Equal("order_id", columns[0].DisplayText);
        Assert.Equal(columns.Skip(1).Select(c => c.DisplayText).OrderBy(n => n, StringComparer.OrdinalIgnoreCase), columns.Skip(1).Select(c => c.DisplayText));
    }
}
