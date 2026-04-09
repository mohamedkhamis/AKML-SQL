using Xunit;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Parser;

public class TokenBasedAliasExtractorTests
{
    private readonly TsqlParserService _parser = new();

    private static int OffsetOfCursor(string sqlWithMarker, char marker = '|')
    {
        var idx = sqlWithMarker.IndexOf(marker);
        Assert.True(idx >= 0, "test SQL must contain a cursor marker");
        return idx;
    }

    private (IList<TSqlParserToken> Tokens, int CursorOffset) Tokenize(string sqlWithMarker)
    {
        var cursorOffset = OffsetOfCursor(sqlWithMarker);
        var sqlNoMarker = sqlWithMarker.Replace("|", string.Empty);
        var tokens = _parser.GetTokenStream(sqlNoMarker);
        return (tokens, cursorOffset);
    }

    // ── Cursor BEFORE the FROM clause (the regression that the fix addresses) ──

    [Fact]
    public void Extract_CursorBeforeFrom_FindsTableAfterCursor()
    {
        // The cursor sits inside COUNT(DISTINCT ) — BEFORE the FROM clause.
        // The previous implementation stopped scanning at the cursor and missed Terminals.
        var (tokens, cursorOffset) = Tokenize("SELECT COUNT(DISTINCT |) FROM Terminals WHERE TID LIKE '%x%'");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Terminals"),
            "expected Terminals to be picked up even though FROM appears after the cursor");
        Assert.Equal("dbo.Terminals", aliases["Terminals"]);
    }

    [Fact]
    public void Extract_CursorBeforeJoin_FindsBothTables()
    {
        // Cursor in WHERE position, FROM/JOIN both later in the same statement.
        // (Reordered to put WHERE before FROM is invalid SQL — instead use WHERE+ON
        //  scenario where the cursor is inside the SELECT list.)
        var (tokens, cursorOffset) = Tokenize(
            "SELECT |, c.Name FROM Customers c JOIN Orders o ON c.Id = o.CustomerId");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("c"));
        Assert.True(aliases.ContainsKey("o"));
    }

    [Fact]
    public void Extract_CursorAfterFrom_StillWorks()
    {
        // Original behavior: cursor AFTER the FROM clause should also resolve the table.
        var (tokens, cursorOffset) = Tokenize("SELECT * FROM Terminals WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Terminals"));
    }

    // ── Statement-bounded scope (multiple statements separated by semicolons) ──

    [Fact]
    public void Extract_PreviousStatementTables_AreNotReturned()
    {
        // Cursor is in the SECOND statement. The first statement's FROM Customers
        // must NOT leak into the alias dictionary for the second statement.
        var (tokens, cursorOffset) = Tokenize(
            "SELECT * FROM Customers; SELECT * FROM Orders WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Orders"),
            "current statement's table should be present");
        Assert.False(aliases.ContainsKey("Customers"),
            "previous statement's table must NOT be present");
    }

    [Fact]
    public void Extract_LaterStatementTables_AreNotReturned()
    {
        // Cursor is in the FIRST statement. The second statement's FROM Orders
        // must NOT leak backwards.
        var (tokens, cursorOffset) = Tokenize(
            "SELECT |, * FROM Customers; SELECT * FROM Orders");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Customers"));
        Assert.False(aliases.ContainsKey("Orders"));
    }

    // ── Schema-qualified tables ──

    [Fact]
    public void Extract_SchemaQualifiedTable_PicksUpSchema()
    {
        var (tokens, cursorOffset) = Tokenize("SELECT * FROM sales.Orders WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Orders"));
        Assert.Equal("sales.Orders", aliases["Orders"]);
    }

    // ── Aliases with and without AS ──

    [Fact]
    public void Extract_AliasWithAs_RegisteredByAlias()
    {
        var (tokens, cursorOffset) = Tokenize("SELECT * FROM Customers AS c WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("c"));
        Assert.Equal("dbo.Customers", aliases["c"]);
        Assert.False(aliases.ContainsKey("Customers"),
            "alias should win over the bare table name as the dictionary key");
    }

    [Fact]
    public void Extract_AliasWithoutAs_RegisteredByAlias()
    {
        var (tokens, cursorOffset) = Tokenize("SELECT * FROM Customers c WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("c"));
    }

    // ── Edge cases ──

    [Fact]
    public void Extract_EmptyTokens_ReturnsEmpty()
    {
        var aliases = TokenBasedAliasExtractor.Extract(new List<TSqlParserToken>(), 0);
        Assert.Empty(aliases);
    }

    [Fact]
    public void Extract_NullTokens_ReturnsEmpty()
    {
        var aliases = TokenBasedAliasExtractor.Extract(null!, 0);
        Assert.Empty(aliases);
    }

    [Fact]
    public void Extract_NoFromClause_ReturnsEmpty()
    {
        var (tokens, cursorOffset) = Tokenize("SELECT 1 + |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.Empty(aliases);
    }
}
