using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 030 — Redgate SQL Prompt parity: BracketMode (Always/WhenRequired/Never) and
/// CREATE TABLE column-definition keyword ordering (data types ranked first).
/// </summary>
public class Parity_BracketModeAndColumnDefTests
{
    private readonly TsqlParserService _parser = new();

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal cache with one dbo table that has a reserved-word name
    /// ("Order", a T-SQL reserved word) and one plain table ("Customers").
    /// </summary>
    private static DatabaseCache BuildSimpleCache()
    {
        var cache = new DatabaseCache { CacheKey = "srv:Test" };
        var entry = new SchemaEntry { SchemaName = "dbo" };
        entry.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Order",         // reserved word — would normally need brackets
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = false
        });
        entry.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Customers",      // plain identifier — no brackets needed
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = false
        });
        cache.Schemas["dbo"] = entry;
        cache.RebuildFkIndex();
        return cache;
    }

    private CompletionResponse Run(
        string sqlWithMarker,
        DatabaseCache? cache,
        System.Action<CompletionEngine>? configure = null)
    {
        var offset = sqlWithMarker.IndexOf('|');
        Assert.True(offset >= 0, "test SQL must contain a cursor marker '|'");
        var sql = sqlWithMarker.Replace("|", string.Empty);
        var engine = new CompletionEngine(_parser);
        configure?.Invoke(engine);
        return engine.GetCompletions(sql, offset, cache);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  BracketMode tests
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BracketMode.Always: every object insert text must be wrapped in [brackets],
    /// regardless of whether the name is a reserved word or a plain identifier.
    /// </summary>
    [Fact]
    public void BracketMode_Always_WrapsAllInsertTexts()
    {
        var response = Run("SELECT * FROM |", BuildSimpleCache(), engine =>
        {
            engine.BracketMode = BracketMode.Always;
            engine.SchemaQualifyMode = SchemaQualifyMode.Never; // isolate bracket logic
        });

        var tableItems = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Table)
            .ToList();

        Assert.True(tableItems.Count >= 2, "Expected at least two table suggestions");

        foreach (var item in tableItems)
        {
            Assert.StartsWith("[", item.InsertText, StringComparison.Ordinal);
            Assert.EndsWith("]", item.InsertText, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// BracketMode.Never: no object insert text should be wrapped in brackets, even
    /// for identifiers that are T-SQL reserved words (like "Order").
    /// </summary>
    [Fact]
    public void BracketMode_Never_StripsAllBrackets()
    {
        var response = Run("SELECT * FROM |", BuildSimpleCache(), engine =>
        {
            engine.BracketMode = BracketMode.Never;
            engine.SchemaQualifyMode = SchemaQualifyMode.Never;
        });

        var tableItems = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Table)
            .ToList();

        Assert.True(tableItems.Count >= 2, "Expected at least two table suggestions");

        foreach (var item in tableItems)
        {
            Assert.DoesNotContain("[", item.InsertText);
            Assert.DoesNotContain("]", item.InsertText);
        }
    }

    /// <summary>
    /// BracketMode.WhenRequired (default): plain identifiers must NOT be bracketed;
    /// this is the pre-existing default behaviour — we must not regress it.
    /// </summary>
    [Fact]
    public void BracketMode_WhenRequired_PlainIdentifierNotBracketed()
    {
        var response = Run("SELECT * FROM |", BuildSimpleCache(), engine =>
        {
            engine.BracketMode = BracketMode.WhenRequired;
            engine.SchemaQualifyMode = SchemaQualifyMode.Never;
        });

        var customersItem = response.Items
            .FirstOrDefault(i => i.ObjectType == (int)CompletionObjectType.Table
                               && i.DisplayText.Contains("Customers", System.StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(customersItem);
        Assert.Equal("Customers", customersItem!.InsertText);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  ApplyBrackets — QUOTENAME ']' doubling + WhenRequired quoting (bugs #4/#5)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bug #4: a name containing a literal ']' must have it doubled (QUOTENAME
    /// semantics), otherwise the bracketed identifier is invalid T-SQL.
    /// "Has]Bracket" -> "[Has]]Bracket]", NOT "[Has]Bracket]".
    /// </summary>
    [Fact]
    public void ApplyBrackets_Always_DoublesEmbeddedClosingBracket()
    {
        var result = AkmlSql.Engine.Completion.Providers.ObjectProvider
            .ApplyBrackets("Has]Bracket", BracketMode.Always);

        Assert.Equal("[Has]]Bracket]", result);
    }

    /// <summary>
    /// Bug #4 (also under WhenRequired): the embedded ']' makes the name an invalid
    /// regular identifier, so it must be bracketed AND the ']' doubled.
    /// </summary>
    [Fact]
    public void ApplyBrackets_WhenRequired_DoublesEmbeddedClosingBracket()
    {
        var result = AkmlSql.Engine.Completion.Providers.ObjectProvider
            .ApplyBrackets("Has]Bracket", BracketMode.WhenRequired);

        Assert.Equal("[Has]]Bracket]", result);
    }

    /// <summary>
    /// Bug #5: a name containing a space requires quoting; under WhenRequired it must
    /// be bracketed (previously the default arm returned it unchanged -> invalid SQL).
    /// </summary>
    [Fact]
    public void ApplyBrackets_WhenRequired_BracketsNameWithSpace()
    {
        var result = AkmlSql.Engine.Completion.Providers.ObjectProvider
            .ApplyBrackets("Order Date", BracketMode.WhenRequired);

        Assert.Equal("[Order Date]", result);
    }

    /// <summary>
    /// Bug #5: a name containing a hyphen is not a valid regular identifier; under
    /// WhenRequired it must be bracketed.
    /// </summary>
    [Fact]
    public void ApplyBrackets_WhenRequired_BracketsNameWithHyphen()
    {
        var result = AkmlSql.Engine.Completion.Providers.ObjectProvider
            .ApplyBrackets("My-Table", BracketMode.WhenRequired);

        Assert.Equal("[My-Table]", result);
    }

    /// <summary>
    /// Bug #5 (must-not-regress): a plain valid regular identifier must stay UNbracketed
    /// under WhenRequired.
    /// </summary>
    [Fact]
    public void ApplyBrackets_WhenRequired_LeavesPlainNameUnbracketed()
    {
        var result = AkmlSql.Engine.Completion.Providers.ObjectProvider
            .ApplyBrackets("Orders", BracketMode.WhenRequired);

        Assert.Equal("Orders", result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  ClauseType.CreateTableColumnDef detection
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inside CREATE TABLE parens, immediately after a column-name identifier,
    /// CursorContextAnalyzer should classify the position as CreateTableColumnDef.
    /// </summary>
    [Fact]
    public void CursorContext_InCreateTableBody_AfterColumnName_IsCreateTableColumnDef()
    {
        var analyzer = new CursorContextAnalyzer();
        var parser = new TsqlParserService();
        // Cursor is right after "ColumnName " — expects a data-type keyword
        const string sql = "CREATE TABLE dbo.T (\n    ColumnName |";
        var tokens = parser.GetTokenStream(sql);
        var offset = sql.IndexOf('|');
        var sql2 = sql.Replace("|", string.Empty);
        tokens = parser.GetTokenStream(sql2);
        var ctx = analyzer.Analyze(tokens, offset);

        Assert.Equal(ClauseType.CreateTableColumnDef, ctx.ClauseType);
    }

    /// <summary>
    /// At the very start of a CREATE TABLE column list (immediately after the opening paren,
    /// with no column name yet), the context should NOT be CreateTableColumnDef — it should
    /// remain Create or Unknown because no column name has been typed yet.
    /// </summary>
    [Fact]
    public void CursorContext_InCreateTableBody_RightAfterOpenParen_IsNotCreateTableColumnDef()
    {
        var analyzer = new CursorContextAnalyzer();
        var parser = new TsqlParserService();
        const string sql = "CREATE TABLE dbo.T (\n    |";
        var tokens = parser.GetTokenStream(sql);
        var offset = sql.IndexOf('|');
        var sql2 = sql.Replace("|", string.Empty);
        tokens = parser.GetTokenStream(sql2);
        var ctx = analyzer.Analyze(tokens, offset);

        Assert.NotEqual(ClauseType.CreateTableColumnDef, ctx.ClauseType);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  CREATE TABLE column-def keyword ordering
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// In CreateTableColumnDef context, data-type keywords must appear before
    /// constraint keywords in the suggestion list (lower SortPriority number = higher).
    /// </summary>
    [Fact]
    public void Keywords_InCreateTableColumnDef_DataTypesRankBeforeConstraints()
    {
        var response = Run("CREATE TABLE dbo.T (\n    ColumnName |", null);
        // null cache: only keyword provider fires

        var keywords = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Keyword)
            .ToList();

        Assert.True(keywords.Count >= 2, "Expected keyword completions in CREATE TABLE body");

        // Data-type keywords (e.g., INT, VARCHAR, NVARCHAR) must have a lower SortPriority
        // than constraint keywords (e.g., NOT NULL, PRIMARY KEY, DEFAULT).
        var intItem = keywords.FirstOrDefault(k =>
            k.DisplayText.Equals("INT", System.StringComparison.OrdinalIgnoreCase));
        var notNullItem = keywords.FirstOrDefault(k =>
            k.DisplayText.Equals("NOT NULL", System.StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(intItem);
        Assert.NotNull(notNullItem);
        Assert.True(intItem!.SortPriority < notNullItem!.SortPriority,
            $"Expected INT (data type, priority={intItem.SortPriority}) to sort before " +
            $"NOT NULL (constraint, priority={notNullItem.SortPriority})");
    }

    /// <summary>
    /// In CreateTableColumnDef context, both data types and constraint keywords must
    /// be offered (the list must not be empty or reduced to one category only).
    /// </summary>
    [Fact]
    public void Keywords_InCreateTableColumnDef_IncludesBothDataTypesAndConstraints()
    {
        var response = Run("CREATE TABLE dbo.T (\n    ColumnName |", null);

        var keywords = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Keyword)
            .Select(i => i.DisplayText.ToUpperInvariant())
            .ToHashSet();

        // Data types
        Assert.True(keywords.Contains("INT"), "Expected INT in CreateTableColumnDef keywords");
        Assert.True(keywords.Contains("VARCHAR"), "Expected VARCHAR in CreateTableColumnDef keywords");
        Assert.True(keywords.Contains("NVARCHAR"), "Expected NVARCHAR in CreateTableColumnDef keywords");

        // Constraint keywords
        Assert.True(keywords.Contains("NOT NULL"), "Expected NOT NULL in CreateTableColumnDef keywords");
        Assert.True(keywords.Contains("NULL"), "Expected NULL in CreateTableColumnDef keywords");
        Assert.True(keywords.Contains("DEFAULT"), "Expected DEFAULT in CreateTableColumnDef keywords");
        Assert.True(keywords.Contains("IDENTITY"), "Expected IDENTITY in CreateTableColumnDef keywords");
    }
}
