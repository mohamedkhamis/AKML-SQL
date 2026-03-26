using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Tests.Completion;

public class CompletionEngineTests
{
    private readonly TsqlParserService _parserService = new();

    private CompletionEngine CreateEngine() => new(_parserService);

    // ── GetCompletions — basic ────────────────────────────────────────────

    [Fact]
    public void GetCompletions_ValidSql_ReturnsItems()
    {
        var engine = CreateEngine();

        var response = engine.GetCompletions("SELECT ", 7, null);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
    }

    [Fact]
    public void GetCompletions_InComment_ReturnsEmpty()
    {
        var engine = CreateEngine();

        // Cursor inside a comment
        var sql = "-- SELECT ";
        var response = engine.GetCompletions(sql, sql.Length, null);

        Assert.Empty(response.Items);
    }

    [Fact]
    public void GetCompletions_InString_ReturnsEmpty()
    {
        var engine = CreateEngine();

        var sql = "SELECT 'hello ";
        var response = engine.GetCompletions(sql, sql.Length, null);

        Assert.Empty(response.Items);
    }

    // ── SetMaxSuggestions ─────────────────────────────────────────────────

    [Fact]
    public void SetMaxSuggestions_LimitsResults()
    {
        var engine = CreateEngine();
        engine.SetMaxSuggestions(3);

        // "SELECT " triggers keyword completions — lots of keywords
        var response = engine.GetCompletions("SELECT ", 7, null);

        Assert.True(response.Items.Length <= 3);
    }

    [Fact]
    public void SetMaxSuggestions_ExceedsLimit_IsIncompleteTrue()
    {
        var engine = CreateEngine();
        engine.SetMaxSuggestions(1);

        // Many keywords available for SELECT context
        var response = engine.GetCompletions("SELECT ", 7, null);

        // If more than 1 item was available, IsIncomplete = true
        if (response.Items.Length == 1)
        {
            Assert.True(response.IsIncomplete);
        }
    }

    // ── Fuzzy filtering ───────────────────────────────────────────────────

    [Fact]
    public void GetCompletions_PartialText_FiltersResults()
    {
        var engine = CreateEngine();

        // "SEL" should match SELECT but not WHERE
        var response = engine.GetCompletions("SEL", 3, null);

        if (response.Items.Length > 0)
        {
            Assert.Contains(response.Items, i =>
                i.DisplayText.StartsWith("SEL", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ── Error resilience ──────────────────────────────────────────────────

    [Fact]
    public void GetCompletions_EmptySql_NoException()
    {
        var engine = CreateEngine();

        var ex = Record.Exception(() => engine.GetCompletions("", 0, null));

        Assert.Null(ex);
    }

    [Fact]
    public void GetCompletions_LargeSql_NoException()
    {
        var engine = CreateEngine();
        var largeSql = string.Join("\n", Enumerable.Repeat("SELECT 1;", 500));

        var ex = Record.Exception(() => engine.GetCompletions(largeSql, largeSql.Length, null));

        Assert.Null(ex);
    }

    // ── With cache ────────────────────────────────────────────────────────

    [Fact]
    public void GetCompletions_WithCache_NoException()
    {
        var engine = CreateEngine();
        var cache = new DatabaseCache { CacheKey = "srv:db" };

        var ex = Record.Exception(() => engine.GetCompletions("SELECT ", 7, cache));

        Assert.Null(ex);
    }

    // ── RegisterProvider ──────────────────────────────────────────────────

    [Fact]
    public void RegisterProvider_CustomProvider_ItemsReturned()
    {
        var engine = CreateEngine();
        engine.RegisterProvider(new TestProvider());

        // Use valid SQL so providers can run; TestProvider.CanHandle always returns true
        var response = engine.GetCompletions("SELECT ", 7, null);

        // TestProvider always returns one item
        Assert.Contains(response.Items, i => i.DisplayText == "TestItem");
    }

    private class TestProvider : ICompletionProvider
    {
        public string Name => "Test";

        public bool CanHandle(CursorContext context, DatabaseCache? cache) => true;

        public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
        {
            yield return new CompletionItem
            {
                DisplayText = "TestItem",
                InsertText = "TestItem",
                ObjectType = (int)CompletionObjectType.Keyword,
                SecondaryText = "Test",
                SortPriority = 1
            };
        }
    }
}
