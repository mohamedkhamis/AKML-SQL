using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// One bracket policy for the whole completion list, and aliases that are valid unbracketed.
/// <para>
/// Columns, FK-join targets and ON predicates bracketed with "when required" whatever
/// <c>IntelliSense.Qualification.BracketMode</c> said, while tables followed the option — so with
/// Never, tables came out bare and columns as <c>[Key]</c>; with Always, columns stayed bare.
/// </para>
/// <para>
/// Generated aliases are written unbracketed, and initials are often a reserved word: "Order
/// Notes" gave <c>on</c> (<c>JOIN [Order Notes] on ON on.Id = …</c>), "Orders" offered <c>or</c>.
/// </para>
/// </summary>
public sealed class BracketModeAndAliasTests
{
    private readonly TsqlParserService _parser = new();

    private static DatabaseCache BuildCache()
    {
        var cache = new DatabaseCache { CacheKey = "test:Brackets", Phase = PopulationPhase.PhaseB };
        var dbo = new SchemaEntry { SchemaName = "dbo" };
        cache.Schemas["dbo"] = dbo;
        dbo.Objects.Add(Table("Orders", Pk("OrderID"), Col("CustomerID")));
        dbo.Objects.Add(Table("Order Details", Col("OrderID"), Col("Quantity")));
        dbo.Objects.Add(Table("Invoices", Pk("InvoiceID"), Col("Unit Price"), Col("Order")));
        cache.ForeignKeys.Add(new ForeignKey
        {
            FkName = "FK_Order_Details_Orders",
            ParentSchema = "dbo", ParentTable = "Order Details", ParentColumns = ["OrderID"],
            ReferencedSchema = "dbo", ReferencedTable = "Orders", ReferencedColumns = ["OrderID"],
        });
        cache.RebuildFkIndex();
        return cache;
    }

    private CompletionResponse Complete(string sqlWithCaret, BracketMode mode)
    {
        var offset = sqlWithCaret.IndexOf('|');
        var sql = sqlWithCaret.Replace("|", string.Empty);
        return new CompletionEngine(_parser) { BracketMode = mode }.GetCompletions(sql, offset, BuildCache());
    }

    private CompletionItem Column(string sql, string name, BracketMode mode) =>
        Complete(sql, mode).Items.Single(i => i.ObjectType == (int)CompletionObjectType.Column && i.DisplayText == name);

    // ── BracketMode ─────────────────────────────────────────────────────────

    [Fact]
    public void Never_leaves_column_names_bare_like_table_names()
    {
        Assert.Equal("Unit Price", Column("SELECT | FROM Invoices", "Unit Price", BracketMode.Never).InsertText);
        Assert.Equal("Order", Column("SELECT | FROM Invoices", "Order", BracketMode.Never).InsertText);
    }

    [Fact]
    public void Always_brackets_every_column_name()
    {
        Assert.Equal("[InvoiceID]", Column("SELECT | FROM Invoices", "InvoiceID", BracketMode.Always).InsertText);
        Assert.Equal("[Unit Price]", Column("SELECT | FROM Invoices", "Unit Price", BracketMode.Always).InsertText);
    }

    [Fact]
    public void WhenRequired_is_the_default_and_brackets_only_what_needs_it()
    {
        Assert.Equal("InvoiceID", Column("SELECT | FROM Invoices", "InvoiceID", BracketMode.WhenRequired).InsertText);
        Assert.Equal("[Order]", Column("SELECT | FROM Invoices", "Order", BracketMode.WhenRequired).InsertText);
        Assert.Equal(BracketMode.WhenRequired, new CompletionEngine(_parser).BracketMode);
    }

    [Fact]
    public void The_join_suggestion_and_its_on_columns_follow_the_option()
    {
        var always = Complete("SELECT * FROM Orders o JOIN |", BracketMode.Always).Items
            .First(i => i.SourceObject == "dbo.Order Details" && i.InsertText.Contains(" ON "));
        Assert.StartsWith("[dbo].[Order Details] ", always.InsertText);
        Assert.Contains(".[OrderID]", always.InsertText);

        var never = Complete("SELECT * FROM Orders o JOIN |", BracketMode.Never).Items
            .First(i => i.SourceObject == "dbo.Order Details" && i.InsertText.Contains(" ON "));
        Assert.DoesNotContain("[", never.InsertText);
    }

    [Fact]
    public void Always_brackets_the_columns_of_an_on_predicate()
    {
        var predicates = Complete("SELECT * FROM Orders o JOIN [Order Details] od ON |", BracketMode.Always).Items
            .Where(i => i.InsertText.Contains(" = "))
            .ToList();

        Assert.Contains(predicates, p => p.InsertText.Contains("od.[OrderID]") && p.InsertText.Contains("o.[OrderID]"));
    }

    // ── aliases ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Order Notes", "on2")]
    [InlineData("OrderNotes", "on2")]
    [InlineData("InvoiceFee", "if2")]
    [InlineData("IssueSummary", "is2")]
    [InlineData("AccountSettings", "as2")]
    public void A_join_alias_is_never_a_reserved_word(string table, string expected)
    {
        Assert.Equal(expected, JoinProvider.GenerateAlias(table, new Dictionary<string, string>()));
    }

    [Fact]
    public void A_reserved_alias_skips_numbers_already_in_use()
    {
        var taken = new Dictionary<string, string> { ["on2"] = "dbo.Other" };
        Assert.Equal("on3", JoinProvider.GenerateAlias("Order Notes", taken));
    }

    [Theory]
    [InlineData("123", "t")]
    [InlineData("2024 Sales", "s")]
    public void A_name_without_letter_initials_still_gets_a_valid_alias(string table, string expected)
    {
        var alias = JoinProvider.GenerateAlias(table, new Dictionary<string, string>());
        Assert.Equal(expected, alias);
        Assert.False(SqlIdentifier.NeedsQuoting(alias));
    }

    [Theory]
    [InlineData("Orders", "or")]
    [InlineData("Invoices", "in")]
    [InlineData("Issues", "is")]
    public void The_alias_list_does_not_offer_reserved_words(string table, string reserved)
    {
        var items = new AliasProvider { IncludeAs = false }
            .BuildAliasItems(table, new HashSet<string>(System.StringComparer.OrdinalIgnoreCase));

        Assert.NotEmpty(items);
        Assert.DoesNotContain(items, i => string.Equals(i.Display, reserved, System.StringComparison.OrdinalIgnoreCase));
        Assert.All(items, i => Assert.False(SqlIdentifier.NeedsQuoting(i.Display), i.Display));
    }

    // ── fixtures ────────────────────────────────────────────────────────────

    private static DatabaseObject Table(string name, params Column[] columns)
    {
        var obj = new DatabaseObject { SchemaName = "dbo", ObjectName = name, ObjectType = DbObjectType.Table, ColumnsLoaded = true };
        var id = 1;
        foreach (var c in columns)
        {
            c.ColumnId = id++;
            obj.Columns.Add(c);
        }
        return obj;
    }

    private static Column Pk(string name) => new() { ColumnName = name, TypeName = "int", IsPrimaryKey = true, IsIdentity = true };

    private static Column Col(string name) => new() { ColumnName = name, TypeName = "int", IsNullable = true };

    // ── names in other scripts ───────────────────────────────────────────────

    [Theory]
    [InlineData("Übersicht", "ü")]
    [InlineData("Заказы", "з")]
    [InlineData("العملاء", "ا")]
    [InlineData("CustomerÉvénements", "cé")]
    public void A_join_alias_for_a_name_in_another_script_is_its_initials(string table, string expected)
    {
        // SQL Server's regular identifiers are Unicode: these need no brackets and their initials
        // are valid aliases. With an ASCII-only rule this looped for ever (no digit could fix "ü").
        var alias = JoinProvider.GenerateAlias(table, new Dictionary<string, string>());
        Assert.Equal(expected, alias);
        Assert.False(SqlIdentifier.NeedsQuoting(alias));
    }

    [Fact]
    public void Names_in_other_scripts_are_regular_identifiers()
    {
        Assert.False(SqlIdentifier.NeedsQuoting("Übersicht"));
        Assert.False(SqlIdentifier.NeedsQuoting("Заказы"));
        Assert.False(SqlIdentifier.NeedsQuoting("العملاء"));
        Assert.True(SqlIdentifier.NeedsQuoting("Order Details"));
        Assert.True(SqlIdentifier.NeedsQuoting("1stQuarter"));
        Assert.True(SqlIdentifier.NeedsQuoting("Order"));
    }

    [Theory]
    [InlineData("Übersicht")]
    [InlineData("Заказы")]
    public void The_alias_list_offers_aliases_for_names_in_other_scripts(string table)
    {
        var items = new AliasProvider { IncludeAs = false }
            .BuildAliasItems(table, new HashSet<string>(System.StringComparer.OrdinalIgnoreCase));
        Assert.NotEmpty(items);
    }

    // ── one bracket policy ───────────────────────────────────────────────────

    [Theory]
    [InlineData("[Order Details]", BracketMode.Never, "Order Details")]
    [InlineData("Orders", BracketMode.Always, "[Orders]")]
    [InlineData("[Orders]", BracketMode.Always, "[Orders]")]
    [InlineData("Order Details", BracketMode.WhenRequired, "[Order Details]")]
    public void Tables_and_columns_share_one_bracket_rule(string name, BracketMode mode, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.Apply(name, mode));
        Assert.Equal(expected, ObjectProvider.ApplyBrackets(name, mode));
    }
}
