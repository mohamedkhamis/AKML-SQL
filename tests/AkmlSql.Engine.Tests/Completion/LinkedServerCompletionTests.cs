using Xunit;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// FR-016 — linked-server IntelliSense suggestions. Proves the ConnectionScope.IncludeLinkedServers
/// toggle is now functional: linked servers loaded into the schema cache surface as completions when
/// the flag is on, and the result set is unchanged when the flag is off or no linked servers exist.
/// Exercised through <see cref="CompletionEngine"/> so the real per-request wiring (flag → ObjectProvider)
/// is covered end-to-end. No live SQL Server required — the cache is populated in memory.
/// </summary>
public class LinkedServerCompletionTests
{
    private readonly TsqlParserService _parserService = new();

    private CompletionEngine CreateEngine()
    {
        var engine = new CompletionEngine(_parserService);
        engine.SetMaxSuggestions(200); // linked servers sort last (priority 400) — avoid truncation
        return engine;
    }

    /// <summary>
    /// Tiny cache: one dbo table plus the supplied linked servers. Kept small so linked-server
    /// items are never pushed out of the (capped) suggestion list.
    /// </summary>
    private static DatabaseCache MakeCache(params LinkedServerInfo[] linkedServers)
    {
        var cache = new DatabaseCache { CacheKey = "test:testdb" };
        var entry = new SchemaEntry { SchemaName = "dbo" };
        entry.Objects.Add(new DatabaseObject
        {
            ObjectId = 1,
            SchemaName = "dbo",
            ObjectName = "Customers",
            ObjectType = DbObjectType.Table,
            ApproxRowCount = 100,
            ColumnsLoaded = true
        });
        cache.Schemas.TryAdd("dbo", entry);
        cache.LinkedServers = linkedServers.ToList();
        return cache;
    }

    /// <summary>A dbo schema with <paramref name="objectCount"/> tables plus the supplied linked
    /// servers — used to exercise the suggestion cap (linked servers must survive it).</summary>
    private static DatabaseCache MakeCacheWithManyObjects(int objectCount, params LinkedServerInfo[] linkedServers)
    {
        var cache = new DatabaseCache { CacheKey = "test:testdb" };
        var entry = new SchemaEntry { SchemaName = "dbo" };
        for (int i = 0; i < objectCount; i++)
        {
            entry.Objects.Add(new DatabaseObject
            {
                ObjectId = i + 1,
                SchemaName = "dbo",
                ObjectName = $"Table{i:D3}",
                ObjectType = DbObjectType.Table,
                ApproxRowCount = 1,
                ColumnsLoaded = true
            });
        }
        cache.Schemas.TryAdd("dbo", entry);
        cache.LinkedServers = linkedServers.ToList();
        return cache;
    }

    private static bool IsLinkedServerItem(Core.Ipc.Messages.CompletionItem i) =>
        i.IsLinkedServer && i.SecondaryText.StartsWith("Linked Server", StringComparison.Ordinal);

    // ── (d) Linked servers survive the DEFAULT suggestion cap ─────────────────

    [Fact]
    public void LinkedServers_SurviveDefaultSuggestionCap_WithManyObjects()
    {
        // Regression: linked servers sort last (priority 400). With the default 50-item cap and more
        // than 50 higher-priority objects, they were silently truncated on a bare FROM. They must be
        // pinned past the cap. Uses the DEFAULT cap (no SetMaxSuggestions) unlike CreateEngine().
        var engine = new CompletionEngine(_parserService);
        engine.IncludeLinkedServers = true;

        var cache = MakeCacheWithManyObjects(60,
            new LinkedServerInfo { Name = "PRODLINK", Product = "SQL Server" });

        var sql = "SELECT * FROM ";
        var response = engine.GetCompletions(sql, sql.Length, cache);

        Assert.True(response.IsIncomplete);   // >50 candidates → the cap is engaged
        Assert.Contains(response.Items, i =>
            i.DisplayText.Equals("PRODLINK", StringComparison.Ordinal) && IsLinkedServerItem(i));
    }

    // ── (e) A dot after a linked server must not offer LOCAL schemas ──────────

    [Fact]
    public void DotAfterLinkedServer_DoesNotSuggestLocalSchemas()
    {
        // Regression: "server." fell through to the "unknown prefix -> all local schema names" branch,
        // suggesting the LOCAL server's schemas for a REMOTE catalog we cannot resolve. Suppress instead.
        var engine = CreateEngine();
        engine.IncludeLinkedServers = true;

        var cache = MakeCache(new LinkedServerInfo { Name = "PRODLINK", Product = "SQL Server" });

        var sql = "SELECT * FROM PRODLINK.";
        var response = engine.GetCompletions(sql, sql.Length, cache);

        Assert.DoesNotContain(response.Items, i => i.SecondaryText == "Schema");
        Assert.DoesNotContain(response.Items, i => i.DisplayText.Equals("dbo", StringComparison.OrdinalIgnoreCase));
    }

    // ── (a) Flag ON + linked servers present ⇒ they appear ────────────────────

    [Fact]
    public void IncludeLinkedServers_True_SurfacesLinkedServerNames()
    {
        var engine = CreateEngine();
        engine.IncludeLinkedServers = true;

        var cache = MakeCache(
            new LinkedServerInfo { Name = "PRODLINK", Product = "SQL Server" },
            new LinkedServerInfo { Name = "ORACLE01", Product = "Oracle" });

        var sql = "SELECT * FROM ";
        var response = engine.GetCompletions(sql, sql.Length, cache);

        Assert.Contains(response.Items, i =>
            i.DisplayText.Equals("PRODLINK", StringComparison.Ordinal) && IsLinkedServerItem(i));
        Assert.Contains(response.Items, i =>
            i.DisplayText.Equals("ORACLE01", StringComparison.Ordinal) && IsLinkedServerItem(i));

        // Local objects are still present — the feature is additive, not a replacement.
        Assert.Contains(response.Items, i => i.DisplayText.Contains("Customers"));
    }

    // ── (b) Flag OFF ⇒ never appear ───────────────────────────────────────────

    [Fact]
    public void IncludeLinkedServers_False_ExcludesLinkedServerNames()
    {
        var engine = CreateEngine();
        engine.IncludeLinkedServers = false;

        var cache = MakeCache(
            new LinkedServerInfo { Name = "PRODLINK", Product = "SQL Server" });

        var sql = "SELECT * FROM ";
        var response = engine.GetCompletions(sql, sql.Length, cache);

        Assert.DoesNotContain(response.Items, IsLinkedServerItem);
        Assert.DoesNotContain(response.Items, i => i.DisplayText.Equals("PRODLINK", StringComparison.Ordinal));
    }

    // ── (c) Flag ON but no linked servers loaded ⇒ identical to flag OFF ───────

    [Fact]
    public void IncludeLinkedServers_True_ButNoneLoaded_IsIdenticalToOff()
    {
        var sql = "SELECT * FROM ";

        var offEngine = CreateEngine();
        offEngine.IncludeLinkedServers = false;
        var offItems = offEngine.GetCompletions(sql, sql.Length, MakeCache())
            .Items.Select(i => i.InsertText).OrderBy(t => t, StringComparer.Ordinal).ToList();

        var onEngine = CreateEngine();
        onEngine.IncludeLinkedServers = true; // flag on, but the cache has zero linked servers
        var onItems = onEngine.GetCompletions(sql, sql.Length, MakeCache())
            .Items.Select(i => i.InsertText).OrderBy(t => t, StringComparer.Ordinal).ToList();

        Assert.Equal(offItems, onItems);
        Assert.DoesNotContain(onEngine.GetCompletions(sql, sql.Length, MakeCache()).Items, IsLinkedServerItem);
    }

    // ── Bracketing: a dotted linked-server name is a single whole identifier ───

    /// <summary>
    /// A linked-server name containing dots (e.g. an IP address) must be bracketed as ONE token —
    /// "[10.0.0.5]" — not split into "[10].[0].[0].[5]" the way a schema-qualified object name would be.
    /// </summary>
    [Fact]
    public void LinkedServer_DottedName_IsBracketedAsWhole()
    {
        var engine = CreateEngine();
        engine.IncludeLinkedServers = true;
        engine.BracketMode = BracketMode.WhenRequired; // default, made explicit

        var cache = MakeCache(new LinkedServerInfo { Name = "10.0.0.5" });

        var sql = "SELECT * FROM ";
        var response = engine.GetCompletions(sql, sql.Length, cache);

        var item = response.Items.FirstOrDefault(i =>
            i.DisplayText.Equals("10.0.0.5", StringComparison.Ordinal) && IsLinkedServerItem(i));

        Assert.NotNull(item);
        Assert.Equal("[10.0.0.5]", item!.InsertText);
    }
}
