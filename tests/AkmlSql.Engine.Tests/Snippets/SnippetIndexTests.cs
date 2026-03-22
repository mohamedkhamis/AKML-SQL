using Xunit;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;

namespace AkmlSql.Engine.Tests.Snippets;

public class SnippetIndexTests
{
    private static Snippet MakeSnippet(string id, string shortcode, string name,
        string category = "Custom", string[] ? contexts = null, bool surroundsWith = false,
        string[] ? tags = null, string[] ? body = null)
    {
        var s = new Snippet
        {
            Metadata = new SnippetMetadata
            {
                Id = id,
                Shortcode = shortcode,
                Name = name,
                Category = category,
                Context = contexts ?? ["global"],
                SurroundsWith = surroundsWith,
                Tags = tags ?? []
            },
            Body = body ?? []
        };
        return s;
    }

    private static SnippetIndex BuildIndex(params (Snippet Snippet, SnippetSourceType Source, string? FilePath)[] items)
    {
        var idx = new SnippetIndex();
        idx.Rebuild(items.ToList());
        return idx;
    }

    // ── Count ─────────────────────────────────────────────────────────────

    [Fact]
    public void Count_EmptyIndex_IsZero()
    {
        var idx = new SnippetIndex();
        Assert.Equal(0, idx.Count);
    }

    [Fact]
    public void Count_AfterRebuild_ReturnsCorrectCount()
    {
        var idx = BuildIndex(
            (MakeSnippet("1", "sel", "SELECT"), SnippetSourceType.Personal, null),
            (MakeSnippet("2", "ins", "INSERT"), SnippetSourceType.BuiltIn, null));
        Assert.Equal(2, idx.Count);
    }

    // ── Rebuild / GetById ─────────────────────────────────────────────────

    [Fact]
    public void GetById_ExistingId_ReturnsEntry()
    {
        var snippet = MakeSnippet("abc", "sel", "SELECT");
        var idx = BuildIndex((snippet, SnippetSourceType.Personal, null));
        var result = idx.GetById("abc");
        Assert.NotNull(result);
        Assert.Equal("SELECT", result.Value.Snippet.Metadata.Name);
        Assert.Equal(SnippetSourceType.Personal, result.Value.Source);
    }

    [Fact]
    public void GetById_MissingId_ReturnsNull()
    {
        var idx = new SnippetIndex();
        Assert.Null(idx.GetById("nonexistent"));
    }

    [Fact]
    public void GetById_CaseInsensitive()
    {
        var snippet = MakeSnippet("ABC", "sel", "SELECT");
        var idx = BuildIndex((snippet, SnippetSourceType.Personal, null));
        Assert.NotNull(idx.GetById("abc")); // lowercase lookup
    }

    // ── GetFilePath ───────────────────────────────────────────────────────

    [Fact]
    public void GetFilePath_WithPath_ReturnsPath()
    {
        var snippet = MakeSnippet("x", "sel", "SELECT");
        var idx = BuildIndex((snippet, SnippetSourceType.Personal, "/path/to/sel.akmlsnip"));
        Assert.Equal("/path/to/sel.akmlsnip", idx.GetFilePath("x"));
    }

    [Fact]
    public void GetFilePath_NoPath_ReturnsNull()
    {
        var snippet = MakeSnippet("x", "sel", "SELECT");
        var idx = BuildIndex((snippet, SnippetSourceType.Personal, null));
        Assert.Null(idx.GetFilePath("x"));
    }

    [Fact]
    public void GetFilePath_UnknownId_ReturnsNull()
    {
        var idx = new SnippetIndex();
        Assert.Null(idx.GetFilePath("unknown"));
    }

    // ── GetByShortcode ────────────────────────────────────────────────────

    [Fact]
    public void GetByShortcode_Existing_ReturnsHighestPriority()
    {
        var personal = MakeSnippet("p1", "sel", "Personal SELECT");
        var builtIn = MakeSnippet("b1", "sel", "BuiltIn SELECT");
        var idx = BuildIndex(
            (builtIn, SnippetSourceType.BuiltIn, null),
            (personal, SnippetSourceType.Personal, null));

        var result = idx.GetByShortcode("sel");
        Assert.NotNull(result);
        // Personal (priority 1) should beat BuiltIn (priority 3)
        Assert.Equal("Personal SELECT", result.Metadata.Name);
    }

    [Fact]
    public void GetByShortcode_Missing_ReturnsNull()
    {
        var idx = new SnippetIndex();
        Assert.Null(idx.GetByShortcode("unknown"));
    }

    [Fact]
    public void GetByShortcode_CaseInsensitive()
    {
        var snippet = MakeSnippet("1", "SEL", "SELECT");
        var idx = BuildIndex((snippet, SnippetSourceType.Personal, null));
        Assert.NotNull(idx.GetByShortcode("sel"));
    }

    // ── Rebuild clears previous state ─────────────────────────────────────

    [Fact]
    public void Rebuild_ClearsPreviousState()
    {
        var idx = new SnippetIndex();
        idx.Rebuild([(MakeSnippet("old", "old", "Old"), SnippetSourceType.Personal, null)]);
        idx.Rebuild([(MakeSnippet("new", "new", "New"), SnippetSourceType.Personal, null)]);

        Assert.Equal(1, idx.Count);
        Assert.Null(idx.GetById("old"));
        Assert.NotNull(idx.GetById("new"));
    }

    // ── GetByContext ──────────────────────────────────────────────────────

    [Fact]
    public void GetByContext_NoSelection_ExcludesSurroundsWithSnippets()
    {
        var normal = MakeSnippet("1", "a", "Normal", surroundsWith: false);
        var surrounds = MakeSnippet("2", "b", "Surrounds", surroundsWith: true);
        var idx = BuildIndex(
            (normal, SnippetSourceType.Personal, null),
            (surrounds, SnippetSourceType.Personal, null));

        var results = idx.GetByContext(null, hasSelection: false).ToList();
        Assert.Single(results);
        Assert.Equal("Normal", results[0].Snippet.Metadata.Name);
    }

    [Fact]
    public void GetByContext_WithSelection_OnlyShowsSurroundsWithSnippets()
    {
        var normal = MakeSnippet("1", "a", "Normal", surroundsWith: false);
        var surrounds = MakeSnippet("2", "b", "Surrounds",
            surroundsWith: true, contexts: ["global"]);
        var idx = BuildIndex(
            (normal, SnippetSourceType.Personal, null),
            (surrounds, SnippetSourceType.Personal, null));

        var results = idx.GetByContext(null, hasSelection: true).ToList();
        Assert.Single(results);
        Assert.Equal("Surrounds", results[0].Snippet.Metadata.Name);
    }

    [Fact]
    public void GetByContext_NullClause_ReturnsGlobalSnippets()
    {
        var global = MakeSnippet("1", "a", "Global", contexts: ["global"]);
        var batchStart = MakeSnippet("2", "b", "BatchStart", contexts: ["batch_start"]);
        var afterFrom = MakeSnippet("3", "c", "AfterFrom", contexts: ["after_from"]);
        var idx = BuildIndex(
            (global, SnippetSourceType.Personal, null),
            (batchStart, SnippetSourceType.Personal, null),
            (afterFrom, SnippetSourceType.Personal, null));

        var results = idx.GetByContext(null, false).ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Snippet.Metadata.Name == "Global");
        Assert.Contains(results, r => r.Snippet.Metadata.Name == "BatchStart");
    }

    [Fact]
    public void GetByContext_UnknownClause_ReturnsGlobalSnippets()
    {
        var global = MakeSnippet("1", "a", "Global", contexts: ["global"]);
        var idx = BuildIndex((global, SnippetSourceType.Personal, null));

        var results = idx.GetByContext("Unknown", false).ToList();
        Assert.Single(results);
    }

    [Fact]
    public void GetByContext_WithClauseType_MatchesAfterContext()
    {
        var afterFrom = MakeSnippet("1", "a", "AfterFrom", contexts: ["after_from"]);
        var global = MakeSnippet("2", "b", "Global", contexts: ["global"]);
        var idx = BuildIndex(
            (afterFrom, SnippetSourceType.Personal, null),
            (global, SnippetSourceType.Personal, null));

        var results = idx.GetByContext("FROM", false).ToList();
        // "after_from" matches, "global" also matches (always included)
        Assert.Equal(2, results.Count);
    }

    // ── Search ────────────────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyQuery_ReturnsAll()
    {
        var idx = BuildIndex(
            (MakeSnippet("1", "a", "A"), SnippetSourceType.Personal, null),
            (MakeSnippet("2", "b", "B"), SnippetSourceType.Personal, null));

        var results = idx.Search("").ToList();
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_WhitespaceQuery_ReturnsAll()
    {
        var idx = BuildIndex(
            (MakeSnippet("1", "a", "Alpha"), SnippetSourceType.Personal, null));

        Assert.Single(idx.Search("   ").ToList());
    }

    [Fact]
    public void Search_ByShortcode_ReturnsMatch()
    {
        var idx = BuildIndex(
            (MakeSnippet("1", "sel", "SELECT"), SnippetSourceType.Personal, null),
            (MakeSnippet("2", "ins", "INSERT"), SnippetSourceType.Personal, null));

        var results = idx.Search("sel").ToList();
        Assert.Single(results);
        Assert.Equal("SELECT", results[0].Snippet.Metadata.Name);
    }

    [Fact]
    public void Search_ByName_ReturnsMatch()
    {
        var idx = BuildIndex(
            (MakeSnippet("1", "a", "My Select Template"), SnippetSourceType.Personal, null));

        var results = idx.Search("select template").ToList();
        Assert.Single(results);
    }

    [Fact]
    public void Search_ByTag_ReturnsMatch()
    {
        var s = MakeSnippet("1", "a", "Q", tags: ["query", "dml"]);
        var idx = BuildIndex((s, SnippetSourceType.Personal, null));
        var results = idx.Search("dml").ToList();
        Assert.Single(results);
    }

    [Fact]
    public void Search_ByBody_ReturnsMatch()
    {
        var s = MakeSnippet("1", "a", "Q", body: ["SELECT TOP 10 * FROM $Table$"]);
        var idx = BuildIndex((s, SnippetSourceType.Personal, null));
        var results = idx.Search("top 10").ToList();
        Assert.Single(results);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var idx = BuildIndex(
            (MakeSnippet("1", "sel", "SELECT"), SnippetSourceType.Personal, null));

        Assert.Empty(idx.Search("xyz").ToList());
    }

    // ── GetAll ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_ReturnsAllEntries()
    {
        var idx = BuildIndex(
            (MakeSnippet("1", "a", "A"), SnippetSourceType.Personal, null),
            (MakeSnippet("2", "b", "B"), SnippetSourceType.Team, null));

        Assert.Equal(2, idx.GetAll().Count());
    }
}
