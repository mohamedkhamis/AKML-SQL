using Xunit;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using SchemaQualifyMode = AkmlSql.Core.Config.SchemaQualifyMode;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Tests that CompletionEngine honours the IntelliSense policy flags introduced in Task A.1.
/// Each test is Red (before engine wiring) then Green (after wiring) per TDD.
/// </summary>
public class EnginePolicyTests
{
    private readonly TsqlParserService _parserService = new();

    // ── helpers ───────────────────────────────────────────────────────────

    private CompletionEngine CreateEngine() => new(_parserService);

/// <summary>
    /// Creates a minimal DatabaseCache with a single user table "dbo.Customers" (ApproxRowCount=100).
    /// No real SQL Server connection required — we populate the in-memory structure directly.
    /// </summary>
    private static DatabaseCache MakeCacheWithTable(
        string schemaName = "dbo",
        string tableName = "Customers",
        long approxRows = 100)
    {
        var cache = new DatabaseCache { CacheKey = "test:testdb" };
        var obj = new DatabaseObject
        {
            ObjectId = 1,
            SchemaName = schemaName,
            ObjectName = tableName,
            ObjectType = DbObjectType.Table,
            ApproxRowCount = approxRows,
            ColumnsLoaded = true
        };
        var entry = new SchemaEntry { SchemaName = schemaName };
        entry.Objects.Add(obj);
        cache.Schemas.TryAdd(schemaName, entry);
        return cache;
    }

    // ── Test 1: IncludeKeywords = false ──────────────────────────────────

    /// <summary>
    /// When IncludeKeywords is false the engine must return zero items whose
    /// ObjectType maps to CompletionObjectType.Keyword (value 3).
    /// </summary>
    [Fact]
    public void IncludeKeywords_False_YieldsNoKeywordItems()
    {
        var engine = CreateEngine();
        engine.IncludeKeywords = false;

        // "SELECT " is the canonical keyword-trigger context
        var response = engine.GetCompletions("SELECT ", 7, null);

        var keywordItems = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Keyword)
            .ToList();

        Assert.Empty(keywordItems);
    }

    // ── Test 2: IncludeSystemObjects = false ─────────────────────────────

    /// <summary>
    /// When IncludeSystemObjects is false the engine must not surface system
    /// stored procedures (e.g. sp_help) from the static SystemProcDictionary.
    /// </summary>
    [Fact]
    public void IncludeSystemObjects_False_ExcludesSpHelp()
    {
        var engine = CreateEngine();
        engine.IncludeSystemObjects = false;

        // "EXECUTE " triggers ObjectProvider in Exec clause context,
        // which is where SystemProcDictionary items are emitted.
        var sql = "EXECUTE ";
        var response = engine.GetCompletions(sql, sql.Length, null);

        Assert.DoesNotContain(response.Items, i =>
            i.DisplayText.Equals("sp_help", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// When IncludeSystemObjects is true (the default) sp_help must be present
    /// in the EXEC context, confirming the dictionary is wired in.
    /// The engine's max suggestions is 50; sp_help (priority 400) should be among
    /// the first 33 items before keywords (priority 500).
    /// </summary>
    [Fact]
    public void IncludeSystemObjects_True_IncludesSpHelp()
    {
        var engine = CreateEngine();
        engine.IncludeSystemObjects = true;
        engine.SetMaxSuggestions(200); // ensure truncation is not the issue

        var sql = "EXECUTE ";
        var response = engine.GetCompletions(sql, sql.Length, null);

        Assert.Contains(response.Items, i =>
            i.DisplayText.Equals("sp_help", StringComparison.OrdinalIgnoreCase));
    }

    // ── Test 3: SchemaMode = Always ───────────────────────────────────────

    /// <summary>
    /// When SchemaMode is Always, even dbo-schema objects must be inserted as
    /// "dbo.TableName" rather than the bare "TableName".
    /// </summary>
    [Fact]
    public void SchemaMode_Always_DboObjectHasSchemaQualifiedInsertText()
    {
        var engine = CreateEngine();
        engine.SchemaQualifyMode = SchemaQualifyMode.Always;

        // FROM clause context with a dbo table in the cache
        var cache = MakeCacheWithTable("dbo", "Customers");
        var sql = "SELECT * FROM ";
        var response = engine.GetCompletions(sql, sql.Length, cache);

        // Find the Customers item — when SchemaMode=Always the engine qualifies both
        // DisplayText and InsertText with the schema, so search by InsertText.
        var customersItem = response.Items.FirstOrDefault(i =>
            i.InsertText.Equals("dbo.Customers", StringComparison.OrdinalIgnoreCase) ||
            i.DisplayText.Equals("dbo.Customers", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(customersItem);
        Assert.Equal("dbo.Customers", customersItem.InsertText);
    }

    /// <summary>
    /// When SchemaMode is NonDefaultOnly (the default), dbo objects are inserted
    /// without the schema prefix.
    /// </summary>
    [Fact]
    public void SchemaMode_NonDefaultOnly_DboObjectHasBareInsertText()
    {
        var engine = CreateEngine();
        engine.SchemaQualifyMode = SchemaQualifyMode.NonDefaultOnly;

        var cache = MakeCacheWithTable("dbo", "Customers");
        var sql = "SELECT * FROM ";
        var response = engine.GetCompletions(sql, sql.Length, cache);

        var customersItem = response.Items.FirstOrDefault(i =>
            i.DisplayText.Equals("Customers", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(customersItem);
        Assert.Equal("Customers", customersItem.InsertText);
    }
}
