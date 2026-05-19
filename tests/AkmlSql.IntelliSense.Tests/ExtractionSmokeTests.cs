using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.IntelliSense.Tests;

/// <summary>
/// Spec 021 T102. Smoke tests proving the IntelliSense extraction works: the moved types
/// are reachable, instantiable, and produce sensible output from a project that ONLY
/// references AkmlSql.IntelliSense (no AkmlSql.Engine dependency).
///
/// The bulk of completion-engine functional coverage lives in the existing
/// tests/AkmlSql.Engine.Tests/Completion/ suite (50+ tests) and still passes against the
/// migrated types via the engine's transitive reference. Those will migrate to this
/// project in a follow-up sweep.
/// </summary>
public sealed class ExtractionSmokeTests
{
    [Fact]
    public void CompletionEngine_constructs_without_engine_assembly()
    {
        // Verifies that CompletionEngine, TsqlParserService, and all built-in providers
        // (minus DatabaseProvider which is engine-only) are reachable from a project that
        // has no reference to AkmlSql.Engine.
        var parser = new TsqlParserService();
        var engine = new CompletionEngine(parser);
        Assert.NotNull(engine);
    }

    [Fact]
    public void CompletionEngine_returns_response_for_basic_select()
    {
        var engine = new CompletionEngine(new TsqlParserService());
        var response = engine.GetCompletions("SELECT ", 7, null);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
        Assert.NotEmpty(response.Items);   // SELECT context produces at least keyword completions
    }

    [Fact]
    public void DatabaseCache_constructs_with_empty_state()
    {
        // DatabaseCache is the shared cache shape between the engine's
        // SchemaCacheManager (stays in engine) and the web edition's IndexedDB cache
        // (lands at T107). Confirm it's reachable from the extracted library.
        var cache = new DatabaseCache { CacheKey = "srv:AdventureWorks" };
        Assert.NotNull(cache);
        Assert.Equal("srv:AdventureWorks", cache.CacheKey);
        Assert.Equal(PopulationPhase.NotLoaded, cache.Phase);
    }

    [Fact]
    public void Models_DatabaseObject_and_ForeignKey_are_reachable()
    {
        // Just constructing them is enough to prove the type-forwarding worked.
        _ = new DatabaseObject();
        _ = new ForeignKey();
    }

    [Fact]
    public void TsqlParserService_GetTokenStream_returns_non_empty_for_valid_sql()
    {
        var parser = new TsqlParserService();
        var tokens = parser.GetTokenStream("SELECT 1;");
        Assert.NotNull(tokens);
        Assert.NotEmpty(tokens);
    }
}
