using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Tests.Completion;

public class CompletionEngineTests
{
    private readonly TsqlParserService _parserService = new();

    private CompletionEngine CreateEngine()
    {
        return new CompletionEngine(_parserService);
    }

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
    public void GetCompletions_InString_ReturnsKeywords()
    {
        // Inside string literals, the engine still provides keyword completions
        // to support dynamic SQL authoring scenarios. Spec 032 D: expression positions
        // now also carry built-in functions (ObjectType 5) from the same keyword catalog.
        var engine = CreateEngine();

        var sql = "SELECT 'hello ";
        var response = engine.GetCompletions(sql, sql.Length, null);

        Assert.NotEmpty(response.Items);
        Assert.All(response.Items, item => Assert.True(item.ObjectType is 3 or 5,
            $"expected keyword/built-in items only, got ObjectType {item.ObjectType} ({item.DisplayText})"));
    }

    // ── FilterText scoring (spec 032 T006, FR-026) ────────────────────────

    private sealed class StubProvider : Engine.Completion.ICompletionProvider
    {
        private readonly CompletionItem[] _items;
        public StubProvider(params CompletionItem[] items) => _items = items;
        public string Name => "Stub";
        public bool CanHandle(CursorContext context, DatabaseCache? cache) => true;
        public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache) => _items;
    }

    [Fact]
    public void FuzzyFilter_scores_FilterText_when_set()
    {
        var engine = CreateEngine();
        engine.IncludeKeywords = false;
        // Display is alias-qualified ("x.OrderID") — a typed prefix "Ord" does not
        // prefix-match the display, but must match via FilterText = "OrderID".
        engine.RegisterProvider(new StubProvider(
            new CompletionItem { DisplayText = "x.OrderID", InsertText = "x.OrderID", FilterText = "OrderID" }));

        var sql = "SELECT Ord";
        var response = engine.GetCompletions(sql, sql.Length, null);

        Assert.Contains(response.Items, i => i.DisplayText == "x.OrderID");
    }

    [Fact]
    public void FuzzyFilter_falls_back_to_DisplayText_when_FilterText_null()
    {
        var engine = CreateEngine();
        engine.IncludeKeywords = false;
        engine.RegisterProvider(new StubProvider(
            new CompletionItem { DisplayText = "OrderDate", InsertText = "OrderDate" },
            new CompletionItem { DisplayText = "CustomerName", InsertText = "CustomerName" }));

        var sql = "SELECT Ord";
        var response = engine.GetCompletions(sql, sql.Length, null);

        Assert.Contains(response.Items, i => i.DisplayText == "OrderDate");
        Assert.DoesNotContain(response.Items, i => i.DisplayText == "CustomerName");
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

        public bool CanHandle(CursorContext context, DatabaseCache? cache)
        {
            return true;
        }

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
