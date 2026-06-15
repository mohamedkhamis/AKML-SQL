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
/// Bug #2 (2026-06-14): with SchemaQualifyMode = Always, a dbo object inserts schema-qualified
/// ("dbo.Customers") only when the statement has NO join. Once a join is involved (cursor at a JOIN
/// target, or ≥2 tables referenced) the dbo prefix is dropped — aliases carry the disambiguation.
/// Non-dbo schemas and the Never/NonDefaultOnly modes are unaffected.
/// </summary>
public class SchemaQualifyJoinConditionalTests
{
    private readonly TsqlParserService _parser = new();

    /// <summary>dbo{Customers,Orders} + sales{Invoices} — no FKs (keeps every object in the JOIN list).</summary>
    private static DatabaseCache BuildCache()
    {
        var cache = new DatabaseCache { CacheKey = "srv:Sales" };
        void AddSchema(string schemaName, params string[] tables)
        {
            var entry = new SchemaEntry { SchemaName = schemaName };
            foreach (var t in tables)
                entry.Objects.Add(new DatabaseObject
                {
                    SchemaName = schemaName,
                    ObjectName = t,
                    ObjectType = DbObjectType.Table,
                    ColumnsLoaded = true
                });
            cache.Schemas[schemaName] = entry;
        }
        AddSchema("dbo", "Customers", "Orders");
        AddSchema("sales", "Invoices");
        cache.RebuildFkIndex();
        return cache;
    }

    private string InsertTextFor(string sqlWithMarker, string sourceObject, SchemaQualifyMode mode)
    {
        var offset = sqlWithMarker.IndexOf('|');
        var sql = sqlWithMarker.Replace("|", string.Empty);
        var engine = new CompletionEngine(_parser) { SchemaQualifyMode = mode };
        var resp = engine.GetCompletions(sql, offset, BuildCache());
        var item = resp.Items.FirstOrDefault(i =>
            i.ObjectType == (int)CompletionObjectType.Table && i.SourceObject == sourceObject);
        Assert.NotNull(item);
        return item!.InsertText;
    }

    [Fact]
    public void Always_NoJoin_QualifiesDbo()
    {
        // The first table in a FROM (no join present) → dbo.Customers.
        Assert.Equal("dbo.Customers", InsertTextFor("SELECT * FROM |", "dbo.Customers", SchemaQualifyMode.Always));
    }

    [Fact]
    public void Always_SingleTableReselect_QualifiesDbo()
    {
        // Bug #2's exact case: re-selecting the lone table in a single-table FROM → dbo.Customers.
        Assert.Equal("dbo.Customers", InsertTextFor("SELECT * FROM Custom|", "dbo.Customers", SchemaQualifyMode.Always));
    }

    [Fact]
    public void Always_AtJoinTarget_DoesNotQualifyDbo()
    {
        // Cursor at a JOIN target → a join is present → dbo stays bare.
        Assert.Equal("Customers",
            InsertTextFor("SELECT * FROM dbo.Orders o INNER JOIN |", "dbo.Customers", SchemaQualifyMode.Always));
    }

    [Fact]
    public void Always_ReselectFirstTableWithJoinPresent_DoesNotQualifyDbo()
    {
        // Two tables are referenced (≥2 aliases) → join present → dbo stays bare even in FROM position.
        Assert.Equal("Customers",
            InsertTextFor("SELECT * FROM Custom| c JOIN dbo.Orders o ON c.Id = o.CustomerId",
                "dbo.Customers", SchemaQualifyMode.Always));
    }

    [Fact]
    public void NonDboSchema_IsAlwaysQualified_EvenWithJoin()
    {
        // The join-conditional rule only relaxes dbo; non-dbo objects always keep their prefix.
        Assert.Equal("sales.Invoices",
            InsertTextFor("SELECT * FROM dbo.Orders o INNER JOIN |", "sales.Invoices", SchemaQualifyMode.Always));
    }

    [Fact]
    public void NeverMode_NeverQualifiesDbo_RegardlessOfJoin()
    {
        Assert.Equal("Customers", InsertTextFor("SELECT * FROM |", "dbo.Customers", SchemaQualifyMode.Never));
    }
}
