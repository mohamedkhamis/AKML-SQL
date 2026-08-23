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

    // ── Spec 032 US2 (T014) — A1: caret inside parens keeps its OWN scope + outer ──

    [Fact]
    public void Extract_CursorInsideSubquery_SeesSubqueryOwnTables()
    {
        // Campaign family: subqueries (15/70). The depth>0 skip discarded the
        // subquery's own FROM when the caret was inside the parens.
        var (tokens, cursorOffset) = Tokenize(
            "SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT | FROM dbo.OrderDetails od)");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("od"), "subquery's own FROM table must be in scope at the caret");
        Assert.Equal("dbo.OrderDetails", aliases["od"]);
    }

    [Fact]
    public void Extract_CursorInsideSubquery_AlsoSeesOuterAliases()
    {
        var (tokens, cursorOffset) = Tokenize(
            "SELECT * FROM dbo.Orders o WHERE EXISTS (SELECT | FROM dbo.OrderDetails od)");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("o"), "outer (correlated) alias must remain visible inside the subquery");
        Assert.Equal("dbo.Orders", aliases["o"]);
    }

    [Fact]
    public void Extract_CursorInsideSubquery_InnerWinsOnAliasConflict()
    {
        var (tokens, cursorOffset) = Tokenize(
            "SELECT * FROM dbo.Orders x WHERE EXISTS (SELECT | FROM dbo.OrderDetails x)");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.Equal("dbo.OrderDetails", aliases["x"]);
    }

    [Fact]
    public void Extract_CursorInsideCteBody_SeesCteBodyTables()
    {
        var (tokens, cursorOffset) = Tokenize(
            "WITH cte AS (SELECT | FROM dbo.OrderDetails od) SELECT * FROM cte");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("od"));
    }

    [Fact]
    public void Extract_SiblingCteBody_StillDoesNotLeak_Regression()
    {
        // The original depth>0 exclusion exists to keep SIBLING paren groups out —
        // that invariant must survive the cursor-scope rework.
        var (tokens, cursorOffset) = Tokenize(
            "WITH cte AS (SELECT * FROM InnerTable) SELECT * FROM cte WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.False(aliases.ContainsKey("InnerTable"),
            "a CTE body the caret is NOT inside must not leak into the outer scope");
    }

    // ── Spec 032 US2 (T014) — A2/F4: aliased DML must not poison the alias map ──

    [Fact]
    public void Extract_AliasedUpdate_ResolvesAliasToRealTable()
    {
        // Campaign UPD-045…58: `UPDATE o SET … FROM Orders o` registered a phantom
        // table `dbo.o`, and first-occurrence-wins blocked the real FROM mapping.
        var (tokens, cursorOffset) = Tokenize("UPDATE o SET | FROM dbo.Orders o");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("o"));
        Assert.Equal("dbo.Orders", aliases["o"]);
    }

    [Fact]
    public void Extract_AliasedDelete_ResolvesAliasToRealTable()
    {
        var (tokens, cursorOffset) = Tokenize("DELETE o FROM dbo.Orders o WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("o"));
        Assert.Equal("dbo.Orders", aliases["o"]);
    }

    [Fact]
    public void Extract_AliasedUpdateOverTempTable_ResolvesAlias()
    {
        // F4 — same mechanism over a temp table.
        var (tokens, cursorOffset) = Tokenize("UPDATE t SET a = 1 FROM #tmp t WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("t"));
        Assert.EndsWith("#tmp", aliases["t"]);
    }

    [Fact]
    public void Extract_FromlessUpdate_TargetStillInjected_Regression()
    {
        // Deliberate behavior (memory: dml-target-alias-resolution) — must survive the two-pass rework.
        var (tokens, cursorOffset) = Tokenize("UPDATE dbo.Orders SET |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Orders"));
        Assert.Equal("dbo.Orders", aliases["Orders"]);
    }

    [Fact]
    public void Extract_FromlessDelete_TargetStillInjected_Regression()
    {
        var (tokens, cursorOffset) = Tokenize("DELETE Customers WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Customers"));
    }

    // ── Spec 032 US2 (T014) — A5: set operators bound the scope ──

    [Fact]
    public void Extract_UnionSecondBranch_DoesNotSeeFirstBranch()
    {
        var (tokens, cursorOffset) = Tokenize(
            "SELECT * FROM dbo.Customers UNION SELECT | FROM dbo.Orders");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Orders"));
        Assert.False(aliases.ContainsKey("Customers"),
            "the other UNION branch's tables must not leak (campaign A5)");
    }

    [Fact]
    public void Extract_UnionFirstBranch_DoesNotSeeSecondBranch()
    {
        var (tokens, cursorOffset) = Tokenize(
            "SELECT |, CustomerName FROM dbo.Customers UNION SELECT OrderID FROM dbo.Orders");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("Customers"));
        Assert.False(aliases.ContainsKey("Orders"));
    }

    // ── Spec 032 US2 (T014) — A6: multi-part names ──

    [Fact]
    public void Extract_ThreePartName_ResolvesAliasWithoutBogusEntries()
    {
        // `db.dbo.Orders o` used to register a bogus alias dbo→db.dbo and drop `o`.
        var (tokens, cursorOffset) = Tokenize("SELECT * FROM OtherDb.dbo.Orders o WHERE |");

        var aliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);

        Assert.True(aliases.ContainsKey("o"), "the real alias must be registered");
        Assert.Equal("dbo.Orders", aliases["o"]);
        Assert.False(aliases.ContainsKey("dbo"), "no bogus schema-as-alias entry");
    }
}
