using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.PostgreSql.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>
/// Spec 042 (task T096, FR-061) - a name is inserted in double quotes only when PostgreSQL needs them: not lower case, a space or
/// other odd character, or a reserved word. A word typed after an opening quote always gets its quotes.
/// </summary>
public class PgIdentifierInsertionTests
{
    private static readonly PgCompletionEngine Engine = new();

    private static DatabaseCache CacheWithAwkwardNames()
    {
        var cache = NorthwindPgTestCacheFactory.Create();
        var entry = cache.Schemas["public"];
        var table = new DatabaseObject { ObjectId = 5000, SchemaName = "public", ObjectName = "user", ObjectType = DbObjectType.Table, ColumnsLoaded = true };
        table.Columns.Add(new Column { ColumnId = 1, ColumnName = "order", TypeName = "integer" });
        table.Columns.Add(new Column { ColumnId = 2, ColumnName = "Name", TypeName = "text" });
        table.Columns.Add(new Column { ColumnId = 3, ColumnName = "plain_col", TypeName = "text" });
        table.Columns.Add(new Column { ColumnId = 4, ColumnName = "9lives", TypeName = "integer" });
        entry.Objects.Add(table);
        return cache;
    }

    private static CompletionItem Item(string doc, string display)
    {
        var caret = doc.IndexOf('|');
        var response = Engine.GetCompletions(doc.Remove(caret, 1), caret, CacheWithAwkwardNames(), new PgCompletionOptions { MaxSuggestions = 200 });
        return response.Items.First(i => i.DisplayText == display);
    }

    [Fact]
    public void A_plain_lower_case_table_is_inserted_as_it_is()
    {
        Assert.Equal("customers", Item("SELECT * FROM |", "customers").InsertText);
    }

    [Fact]
    public void A_table_with_a_space_and_capitals_is_quoted()
    {
        Assert.Equal("\"Audit Log\"", Item("SELECT * FROM |", "Audit Log").InsertText);
    }

    [Fact]
    public void A_reserved_word_is_quoted()
    {
        Assert.Equal("\"user\"", Item("SELECT * FROM |", "user").InsertText);
    }

    [Fact]
    public void Columns_are_quoted_for_reserved_words_capitals_and_a_leading_digit()
    {
        Assert.Equal("\"order\"", Item("SELECT | FROM \"user\"", "order").InsertText);
        Assert.Equal("\"Name\"", Item("SELECT | FROM \"user\"", "Name").InsertText);
        Assert.Equal("\"9lives\"", Item("SELECT | FROM \"user\"", "9lives").InsertText);
        Assert.Equal("plain_col", Item("SELECT | FROM \"user\"", "plain_col").InsertText);
    }

    [Fact]
    public void A_column_after_an_alias_dot_is_quoted_the_same_way()
    {
        Assert.Equal("\"Event Name\"", Item("SELECT a.| FROM \"Audit Log\" a", "Event Name").InsertText);
        Assert.Equal("log_id", Item("SELECT a.| FROM \"Audit Log\" a", "log_id").InsertText);
    }

    [Fact]
    public void An_object_of_another_schema_is_qualified_and_each_part_is_quoted_on_its_own()
    {
        Assert.Equal("sales.orders", Item("SELECT * FROM |", "sales.orders").InsertText);
    }

    [Fact]
    public void With_several_tables_the_alias_and_the_column_are_each_quoted_as_needed()
    {
        var item = Item("SELECT | FROM \"Audit Log\" a, customers c", "a.Event Name");

        Assert.Equal("a.\"Event Name\"", item.InsertText);
    }

    [Fact]
    public void A_word_typed_after_an_opening_quote_is_always_inserted_quoted()
    {
        Assert.Equal("\"customers\"", Item("SELECT * FROM \"cus|", "customers").InsertText);
    }

    [Fact]
    public void A_quoted_prefix_matches_the_name_without_its_quote()
    {
        var caret = "SELECT * FROM \"Aud".Length;
        var response = Engine.GetCompletions("SELECT * FROM \"Aud", caret, CacheWithAwkwardNames());

        Assert.Contains(response.Items, i => i.DisplayText == "Audit Log" && i.InsertText == "\"Audit Log\"");
        Assert.DoesNotContain(response.Items, i => i.ObjectType == (int)CompletionObjectType.Keyword);
    }

    [Fact]
    public void A_quote_inside_a_name_is_doubled()
    {
        var cache = NorthwindPgTestCacheFactory.Create();
        cache.Schemas["public"].Objects.Add(new DatabaseObject { ObjectId = 5001, SchemaName = "public", ObjectName = "say \"hi\"", ObjectType = DbObjectType.Table, ColumnsLoaded = true });

        var response = Engine.GetCompletions("SELECT * FROM ", 14, cache, new PgCompletionOptions { MaxSuggestions = 200 });

        Assert.Contains(response.Items, i => i.DisplayText == "say \"hi\"" && i.InsertText == "\"say \"\"hi\"\"\"");
    }
}
