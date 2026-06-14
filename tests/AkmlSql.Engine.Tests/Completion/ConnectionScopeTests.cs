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
/// Spec 030 T036 / FR-016 — suggestion connection scope. The object suggestion list can be limited
/// to chosen schemas, and when the connected database is excluded from a non-empty database allow-list
/// its objects are suppressed. (Linked-server inclusion has no cache data today and is covered only by
/// the settings-helper tests — it is a forward-looking, currently-inert toggle.)
/// </summary>
public class ConnectionScopeTests
{
    private readonly TsqlParserService _parser = new();

    /// <summary>dbo{Customers,Orders} + sales{Invoices,Quotes} + hr{Employees}, all columns loaded.</summary>
    private static DatabaseCache BuildMultiSchemaCache()
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
        AddSchema("sales", "Invoices", "Quotes");
        AddSchema("hr", "Employees");
        cache.RebuildFkIndex();
        return cache;
    }

    private CompletionResponse Run(string sqlWithMarker, DatabaseCache cache,
        System.Action<CompletionEngine> configure = null)
    {
        var offset = sqlWithMarker.IndexOf('|');
        Assert.True(offset >= 0, "test SQL must contain a cursor marker");
        var sql = sqlWithMarker.Replace("|", string.Empty);
        var engine = new CompletionEngine(_parser);
        configure?.Invoke(engine);
        return engine.GetCompletions(sql, offset, cache);
    }

    // Object identity uses SourceObject ("schema.object"), which is independent of the qualification
    // display mode (Always/NonDefaultOnly/Never).
    private static List<string> ObjectKeys(CompletionResponse r) => r.Items
        .Where(i => i.ObjectType == (int)CompletionObjectType.Table
                 || i.ObjectType == (int)CompletionObjectType.View
                 || i.ObjectType == (int)CompletionObjectType.Procedure
                 || i.ObjectType == (int)CompletionObjectType.Function)
        .Select(i => i.SourceObject)
        .ToList();

    private static List<string> SchemaNames(CompletionResponse r) => r.Items
        .Where(i => i.ObjectType == (int)CompletionObjectType.Schema)
        .Select(i => i.DisplayText)
        .ToList();

    [Fact]
    public void NoScope_AllSchemasOffered()
    {
        var keys = ObjectKeys(Run("SELECT * FROM |", BuildMultiSchemaCache()));

        Assert.Contains("dbo.Customers", keys);
        Assert.Contains("sales.Invoices", keys);
        Assert.Contains("hr.Employees", keys);
    }

    [Fact]
    public void SchemaScope_LimitsObjectsToAllowedSchemas()
    {
        var keys = ObjectKeys(Run("SELECT * FROM |", BuildMultiSchemaCache(),
            e => e.ScopeSchemas = new[] { "sales" }));

        Assert.Contains("sales.Invoices", keys);
        Assert.Contains("sales.Quotes", keys);
        Assert.DoesNotContain("dbo.Customers", keys);
        Assert.DoesNotContain("hr.Employees", keys);
    }

    [Fact]
    public void SchemaScope_IsCaseInsensitive()
    {
        var keys = ObjectKeys(Run("SELECT * FROM |", BuildMultiSchemaCache(),
            e => e.ScopeSchemas = new[] { "SALES" }));

        Assert.Contains("sales.Invoices", keys);
        Assert.DoesNotContain("dbo.Customers", keys);
    }

    [Fact]
    public void SchemaScope_FiltersSchemaNameSuggestions()
    {
        var schemas = SchemaNames(Run("SELECT * FROM |", BuildMultiSchemaCache(),
            e => e.ScopeSchemas = new[] { "sales" }));

        Assert.Contains("sales", schemas);
        Assert.DoesNotContain("dbo", schemas);
        Assert.DoesNotContain("hr", schemas);
    }

    [Fact]
    public void DatabaseOutOfScope_SuppressesObjectAndSchemaSuggestions()
    {
        var resp = Run("SELECT * FROM |", BuildMultiSchemaCache(), e => e.DatabaseInScope = false);

        Assert.Empty(ObjectKeys(resp));
        Assert.Empty(SchemaNames(resp));
    }

    // ── Settings helpers (FR-016 allow-list semantics) ──

    [Fact]
    public void IncludesDatabase_EmptyList_IncludesAll()
        => Assert.True(new ConnectionScopeSettings().IncludesDatabase("AnyDb"));

    [Fact]
    public void IncludesDatabase_NonEmpty_CaseInsensitive_AndUnknownNotSuppressed()
    {
        var s = new ConnectionScopeSettings { Databases = new[] { "Sales", "HR" } };

        Assert.True(s.IncludesDatabase("sales"));   // case-insensitive match
        Assert.False(s.IncludesDatabase("Finance")); // excluded
        Assert.True(s.IncludesDatabase(null));       // unknown DB → not suppressed
    }

    [Fact]
    public void IncludesSchema_NonEmpty_CaseInsensitive()
    {
        var s = new ConnectionScopeSettings { Schemas = new[] { "sales" } };

        Assert.True(s.IncludesSchema("SALES"));
        Assert.False(s.IncludesSchema("hr"));
    }
}
