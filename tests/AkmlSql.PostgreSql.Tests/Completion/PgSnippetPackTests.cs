using System.Text.Json;
using AkmlSql.PostgreSql.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>Spec 042 (task T096, T104, FR-066) - the PostgreSQL snippet pack.</summary>
public class PgSnippetPackTests
{
    [Fact]
    public void The_pack_has_the_snippets_the_sql_server_built_ins_have_in_postgresql_syntax()
    {
        var shortcodes = PgSnippetPack.BuiltIn.Select(s => s.Shortcode).ToArray();

        foreach (var expected in new[] { "sel", "ssf", "ins", "upd", "del", "cte", "crt", "cix", "cfn", "cvi", "txn" })
            Assert.Contains(expected, shortcodes);
        Assert.Equal(shortcodes.Length, shortcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_file_says_it_is_postgresql_and_has_a_cursor_marker()
    {
        Assert.NotEmpty(PgSnippetPack.Files);
        foreach (var (name, text) in PgSnippetPack.Files)
        {
            using var doc = JsonDocument.Parse(text);
            var meta = doc.RootElement.GetProperty("metadata");

            Assert.Equal("postgresql", meta.GetProperty("dialect").GetString());
            Assert.StartsWith("builtin-pg-", meta.GetProperty("id").GetString());
            Assert.False(string.IsNullOrWhiteSpace(meta.GetProperty("description").GetString()), name);
            Assert.Contains(doc.RootElement.GetProperty("body").EnumerateArray(), l => l.GetString()!.Contains("$CURSOR$", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_bodies_are_postgresql_and_not_t_sql()
    {
        var all = string.Join("\n", PgSnippetPack.BuiltIn.Select(s => s.Body));

        Assert.Contains("RETURNING", all);
        Assert.Contains("GENERATED ALWAYS AS IDENTITY", all);
        Assert.Contains("LANGUAGE plpgsql", all);
        Assert.Contains("timestamptz", all);
        Assert.DoesNotContain("IDENTITY(1,1)", all);
        Assert.DoesNotContain("NOCOUNT", all);
        Assert.DoesNotContain("[dbo]", all);
        Assert.DoesNotContain("@", all);
        Assert.DoesNotContain("GO\n", all);
        Assert.DoesNotContain("BEGIN TRAN", all);
    }

    [Fact]
    public void The_function_snippet_is_a_dollar_quoted_body()
    {
        var cfn = PgSnippetPack.BuiltIn.Single(s => s.Shortcode == "cfn").Body;

        Assert.StartsWith("CREATE OR REPLACE FUNCTION", cfn);
        Assert.Equal(2, cfn.Split("$$").Length - 1);
    }

    [Fact]
    public void The_transaction_snippet_is_begin_and_commit()
    {
        var txn = PgSnippetPack.BuiltIn.Single(s => s.Shortcode == "txn").Body;

        Assert.StartsWith("BEGIN;", txn);
        Assert.EndsWith("COMMIT;", txn);
    }

    [Fact]
    public void The_completion_offers_the_shortcodes_where_a_statement_begins()
    {
        var engine = new PgCompletionEngine();

        var empty = engine.GetCompletions(string.Empty, 0, NorthwindPgTestCacheFactory.Create());
        var typed = engine.GetCompletions("ins", 3, NorthwindPgTestCacheFactory.Create());
        var afterSemicolon = engine.GetCompletions("SELECT 1; ", 10, NorthwindPgTestCacheFactory.Create());

        Assert.Contains(empty.Items, i => i.ObjectType == (int)AkmlSql.Core.Ipc.Messages.CompletionObjectType.Snippet && i.DisplayText == "sel");
        Assert.Contains(typed.Items, i => i.DisplayText == "ins" && i.InsertText.StartsWith("INSERT INTO", StringComparison.Ordinal));
        Assert.Contains(afterSemicolon.Items, i => i.DisplayText == "sel");
    }

    [Fact]
    public void Snippets_are_not_offered_in_the_middle_of_a_statement()
    {
        var engine = new PgCompletionEngine();

        var response = engine.GetCompletions("SELECT * FROM customers WHERE ", 30, NorthwindPgTestCacheFactory.Create());

        Assert.DoesNotContain(response.Items, i => i.ObjectType == (int)AkmlSql.Core.Ipc.Messages.CompletionObjectType.Snippet);
    }

    [Fact]
    public void A_users_own_snippet_is_offered_beside_the_built_ins()
    {
        var engine = new PgCompletionEngine();
        var options = new PgCompletionOptions { Snippets = new[] { new PgSnippetInfo("mine", "My snippet", "SELECT 42;") } };

        var response = engine.GetCompletions("mi", 2, NorthwindPgTestCacheFactory.Create(), options);

        Assert.Contains(response.Items, i => i.DisplayText == "mine" && i.InsertText == "SELECT 42;");
    }

    [Fact]
    public void A_users_snippet_with_the_shortcode_of_a_built_in_wins()
    {
        var engine = new PgCompletionEngine();
        var options = new PgCompletionOptions { Snippets = new[] { new PgSnippetInfo("sel", "Mine", "SELECT mine;") } };

        var response = engine.GetCompletions("sel", 3, NorthwindPgTestCacheFactory.Create(), options);

        var sel = response.Items.Single(i => i.DisplayText == "sel" && i.ObjectType == (int)AkmlSql.Core.Ipc.Messages.CompletionObjectType.Snippet);
        Assert.Equal("SELECT mine;", sel.InsertText);
    }
}
