using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Names that need square brackets — classic Northwind's <c>Order Details</c> above all — must be
/// written into the query bracketed, everywhere completion writes a name.
/// <para>
/// The FROM-list completion already did this. Everything that BUILDS text around a name did not:
/// the JOIN suggestion produced <c>JOIN Order Details od ON ...</c>, a qualified column after an
/// unaliased <c>FROM [Order Details]</c> produced <c>Order Details.OrderID</c>, and the ON-clause
/// predicate did the same. Each is invalid T-SQL, and each came from a suggestion whose whole job is
/// to write correct SQL for you.
/// </para>
/// <para>
/// The 1,376-case completion corpus did not catch any of it: its Northwind fixture is the W3Schools
/// variant, where the table is <c>OrderDetails</c>. So this builds its own small classic-Northwind
/// cache instead of touching that fixture, whose shape the corpus pass rates depend on.
/// </para>
/// </summary>
public class SpacedIdentifierQuotingTests
{
    private readonly TsqlParserService _parser = new();

    /// <summary>Orders ← [Order Details] → Products, plus a table with awkward column names.</summary>
    private static DatabaseCache BuildCache()
    {
        var cache = new DatabaseCache { CacheKey = "test:Northwind", Phase = PopulationPhase.PhaseB };
        var dbo = new SchemaEntry { SchemaName = "dbo" };
        cache.Schemas["dbo"] = dbo;

        dbo.Objects.Add(Table("Orders", Pk("OrderID"), Col("CustomerID"), Col("OrderDate")));
        dbo.Objects.Add(Table("Products", Pk("ProductID"), Col("ProductName")));
        dbo.Objects.Add(Table("Order Details",
            Col("OrderID"), Col("ProductID"), Col("Quantity"), Col("Discount")));
        // Column names that need brackets: a space, and a reserved word.
        dbo.Objects.Add(Table("Invoices", Pk("InvoiceID"), Col("Unit Price"), Col("Order")));

        cache.ForeignKeys.AddRange(
        [
            Fk("FK_Order_Details_Orders", "Order Details", "OrderID", "Orders", "OrderID"),
            Fk("FK_Order_Details_Products", "Order Details", "ProductID", "Products", "ProductID"),
        ]);
        cache.RebuildFkIndex();
        return cache;
    }

    private CompletionResponse Complete(string sqlWithCaret)
    {
        var offset = sqlWithCaret.IndexOf('|');
        var sql = sqlWithCaret.Replace("|", string.Empty);
        return new CompletionEngine(_parser).GetCompletions(sql, offset, BuildCache());
    }

    // ------------------------------------------------------------------ the object itself

    [Fact]
    public void FromList_InsertsTheSpacedTableBracketed()
    {
        var item = Complete("SELECT * FROM Ord|").Items
            .Single(i => i.ObjectType == (int)CompletionObjectType.Table && i.SourceObject == "dbo.Order Details");

        // Schema qualification is a separate policy (default: always), so "dbo." may precede it.
        // What matters is that the name part is bracketed and the schema part is left alone.
        Assert.Matches(@"^(dbo\.)?\[Order Details\]$", item.InsertText);

        // The label stays readable, and it is what the editor matches typing against: "Ord" must
        // still find it, which it could not by prefix if the label were "[Order Details]".
        Assert.DoesNotContain("[", item.DisplayText);
    }

    // ------------------------------------------------------------------ JOIN suggestions

    [Fact]
    public void JoinSuggestion_BracketsTheTableItJoins()
    {
        var joins = Complete("SELECT * FROM Orders o JOIN |").Items
            .Where(i => i.SourceObject == "dbo.Order Details" && i.InsertText.Contains(" ON "))
            .ToList();

        Assert.NotEmpty(joins);
        foreach (var join in joins)
        {
            Assert.Matches(@"^(dbo\.)?\[Order Details\] ", join.InsertText);
            AssertNoBareSpacedName(join.InsertText);

            // With aliases off, the qualified name is reused inside the ON. It must stay
            // `dbo.[Order Details]` there too -- bracketing it again as one part would give
            // `[dbo.[Order Details]]`, which names nothing.
            Assert.DoesNotContain("[dbo.", join.InsertText);
        }
    }

    [Fact]
    public void JoinSuggestion_FromAnUnaliasedSpacedTable_BracketsTheExistingSideOfTheOn()
    {
        // `FROM [Order Details]` with no alias: the existing side of the generated ON is the table's
        // bare name, which unbracketed gives `... = Order Details.OrderID`.
        var joins = Complete("SELECT * FROM [Order Details] JOIN |").Items
            .Where(i => i.SourceObject == "dbo.Orders" && i.InsertText.Contains(" ON "))
            .ToList();

        Assert.NotEmpty(joins);
        foreach (var join in joins)
        {
            Assert.Contains("[Order Details].OrderID", join.InsertText);
            AssertNoBareSpacedName(join.InsertText);
        }
    }

    [Fact]
    public void JoinAlias_ForASpacedName_TakesTheInitialOfEachWord()
    {
        // "order details" -> "od", not "o": a space starts a word just as a capital does.
        Assert.Equal("od", AkmlSql.Engine.Completion.Providers.JoinProvider.GenerateAlias("order details", new()));
        Assert.Equal("od", AkmlSql.Engine.Completion.Providers.JoinProvider.GenerateAlias("Order Details", new()));
    }

    // ------------------------------------------------------------------ ON-clause predicates

    [Fact]
    public void OnClause_BracketsAnUnaliasedSpacedTable()
    {
        var predicates = Complete("SELECT * FROM [Order Details] JOIN Orders o ON |").Items
            .Where(i => i.InsertText.Contains(" = "))
            .ToList();

        Assert.Contains(predicates, p => p.InsertText.Contains("[Order Details].OrderID"));
        foreach (var p in predicates)
            AssertNoBareSpacedName(p.InsertText);
    }

    // ------------------------------------------------------------------ columns

    [Fact]
    public void QualifiedColumn_OnAnUnaliasedSpacedTable_IsBracketed()
    {
        // Two tables in scope, so columns are offered qualified.
        var items = Complete(
            "SELECT * FROM [Order Details] JOIN Orders o ON o.OrderID = [Order Details].OrderID WHERE |").Items;

        Assert.Contains(items, i => i.InsertText == "[Order Details].Quantity");
        Assert.DoesNotContain(items, i => i.InsertText.StartsWith("Order Details.", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ColumnWithASpace_IsBracketed()
    {
        var item = Complete("SELECT | FROM Invoices").Items
            .Single(i => i.ObjectType == (int)CompletionObjectType.Column && i.DisplayText == "Unit Price");

        Assert.Equal("[Unit Price]", item.InsertText);
    }

    [Fact]
    public void ColumnNamedAfterAReservedWord_IsBracketed()
    {
        var item = Complete("SELECT | FROM Invoices").Items
            .Single(i => i.ObjectType == (int)CompletionObjectType.Column && i.DisplayText == "Order");

        Assert.Equal("[Order]", item.InsertText);
    }

    [Fact]
    public void OrdinaryNames_AreLeftAlone()
    {
        // The rule is "when required", not "always": bracketing everything would be noise and would
        // fight every formatting style that does not use them.
        var items = Complete("SELECT | FROM Orders").Items;

        Assert.Contains(items, i => i.InsertText == "OrderID");
        Assert.DoesNotContain(items, i => i.InsertText == "[OrderID]");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Fails if the text contains "Order Details" that is not inside brackets.</summary>
    private static void AssertNoBareSpacedName(string text)
    {
        var bare = text.Replace("[Order Details]", string.Empty, System.StringComparison.Ordinal);
        Assert.False(
            bare.Contains("Order Details", System.StringComparison.Ordinal),
            $"Unbracketed 'Order Details' in: {text}");
    }

    private static DatabaseObject Table(string name, params Column[] columns)
    {
        var obj = new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = name,
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true,
        };
        var id = 1;
        foreach (var c in columns)
        {
            c.ColumnId = id++;
            obj.Columns.Add(c);
        }
        return obj;
    }

    private static Column Pk(string name) =>
        new() { ColumnName = name, TypeName = "int", IsPrimaryKey = true, IsIdentity = true };

    private static Column Col(string name) =>
        new() { ColumnName = name, TypeName = "int", IsNullable = true };

    private static ForeignKey Fk(string name, string parent, string parentCol, string referenced, string refCol) =>
        new()
        {
            FkName = name,
            ParentSchema = "dbo",
            ParentTable = parent,
            ParentColumns = [parentCol],
            ReferencedSchema = "dbo",
            ReferencedTable = referenced,
            ReferencedColumns = [refCol],
        };
}
