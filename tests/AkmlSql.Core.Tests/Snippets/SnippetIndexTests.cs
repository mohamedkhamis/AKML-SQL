using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;
using Xunit;

namespace AkmlSql.Core.Tests.Snippets;

public class SnippetIndexTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Snippet MakeSnippet(string shortcode, string name = "",
        string description = "", string category = "Custom",
        string[] tags = null!, string[] context = null!,
        bool surroundsWith = false, string id = "") =>
        new()
        {
            Metadata = new SnippetMetadata
            {
                Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id,
                Shortcode = shortcode,
                Name = string.IsNullOrEmpty(name) ? shortcode : name,
                Description = description,
                Category = category,
                Tags = tags ?? [],
                Context = context ?? ["global"],
                SurroundsWith = surroundsWith
            },
            Body = [$"-- snippet:{shortcode}"]
        };

    private static (Snippet Snippet, SnippetSourceType Source, string? FilePath) Entry(
        Snippet snippet, SnippetSourceType source = SnippetSourceType.Personal, string? filePath = null) =>
        (snippet, source, filePath);

    // ── Rebuild + Count ───────────────────────────────────────────────────────

    [Fact]
    public void Rebuild_PopulatesCount()
    {
        var index = new SnippetIndex();
        var snippets = new List<(Snippet, SnippetSourceType, string?)>
        {
            Entry(MakeSnippet("sel")),
            Entry(MakeSnippet("ins")),
        };
        index.Rebuild(snippets);
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void Rebuild_ClearsPreviousData()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("sel"))]);
        index.Rebuild([Entry(MakeSnippet("ins"))]);

        Assert.Equal(1, index.Count);
        Assert.Null(index.GetByShortcode("sel"));
    }

    [Fact]
    public void Rebuild_TracksFilePath()
    {
        var index = new SnippetIndex();
        var snippet = MakeSnippet("sel", id: "id1");
        index.Rebuild([Entry(snippet, filePath: @"C:\snippets\sel.akmlsnippet")]);

        Assert.Equal(@"C:\snippets\sel.akmlsnippet", index.GetFilePath("id1"));
    }

    [Fact]
    public void Rebuild_FilePath_IsNullWhenNotProvided()
    {
        var index = new SnippetIndex();
        var snippet = MakeSnippet("sel", id: "id2");
        index.Rebuild([Entry(snippet, filePath: null)]);

        Assert.Null(index.GetFilePath("id2"));
    }

    // ── GetByShortcode ────────────────────────────────────────────────────────

    [Fact]
    public void GetByShortcode_ReturnsSnippet_WhenFound()
    {
        var index = new SnippetIndex();
        var snippet = MakeSnippet("sel");
        index.Rebuild([Entry(snippet)]);

        Assert.Equal(snippet, index.GetByShortcode("sel"));
    }

    [Fact]
    public void GetByShortcode_ReturnsNull_WhenNotFound()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("sel"))]);

        Assert.Null(index.GetByShortcode("xyz"));
    }

    [Fact]
    public void GetByShortcode_IsCaseInsensitive()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("SEL"))]);

        Assert.NotNull(index.GetByShortcode("sel"));
        Assert.NotNull(index.GetByShortcode("SEL"));
        Assert.NotNull(index.GetByShortcode("Sel"));
    }

    [Fact]
    public void GetByShortcode_ReturnsHighestPriority_WhenMultipleSources()
    {
        var index = new SnippetIndex();
        var personal = MakeSnippet("sel", name: "Personal");
        var builtIn = MakeSnippet("sel", name: "BuiltIn");

        // SnippetSourceType.Personal = 1, BuiltIn = 3 — lower value = higher priority
        index.Rebuild(
        [
            Entry(personal, SnippetSourceType.Personal),
            Entry(builtIn,  SnippetSourceType.BuiltIn)
        ]);

        // Personal (1) outranks BuiltIn (3) — lower source int = higher priority
        var result = index.GetByShortcode("sel");
        Assert.Equal("Personal", result!.Metadata.Name);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetById_ReturnsSnippetAndSource_WhenFound()
    {
        var index = new SnippetIndex();
        var snippet = MakeSnippet("sel", id: "my-id");
        index.Rebuild([Entry(snippet, SnippetSourceType.Team)]);

        var result = index.GetById("my-id");
        Assert.NotNull(result);
        Assert.Equal("sel", result.Value.Snippet.Metadata.Shortcode);
        Assert.Equal(SnippetSourceType.Team, result.Value.Source);
    }

    [Fact]
    public void GetById_ReturnsNull_WhenNotFound()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("sel", id: "abc"))]);

        Assert.Null(index.GetById("not-found"));
    }

    [Fact]
    public void GetById_IsCaseInsensitive()
    {
        var index = new SnippetIndex();
        var snippet = MakeSnippet("sel", id: "MYID");
        index.Rebuild([Entry(snippet)]);

        Assert.NotNull(index.GetById("myid"));
    }

    // ── GetByContext ──────────────────────────────────────────────────────────

    [Fact]
    public void GetByContext_ReturnsGlobalSnippets_WhenNoSelection()
    {
        var index = new SnippetIndex();
        index.Rebuild(
        [
            Entry(MakeSnippet("g1", context: ["global"])),
            Entry(MakeSnippet("g2", context: ["after_select"])),
        ]);

        var results = index.GetByContext(clauseType: null, hasSelection: false).ToList();
        Assert.Single(results);
        Assert.Equal("g1", results[0].Snippet.Metadata.Shortcode);
    }

    [Fact]
    public void GetByContext_IncludesAfterClauseSnippets_WhenClauseMatches()
    {
        var index = new SnippetIndex();
        index.Rebuild(
        [
            Entry(MakeSnippet("g1", context: ["global"])),
            Entry(MakeSnippet("g2", context: ["after_select"])),
        ]);

        var results = index.GetByContext("SELECT", hasSelection: false).ToList();
        Assert.Equal(2, results.Count); // global + after_select
    }

    [Fact]
    public void GetByContext_ExcludesNormalSnippets_WhenSelectionExists()
    {
        var index = new SnippetIndex();
        index.Rebuild(
        [
            Entry(MakeSnippet("wrap", surroundsWith: true, context: ["global"])),
            Entry(MakeSnippet("normal", surroundsWith: false, context: ["global"])),
        ]);

        var results = index.GetByContext(null, hasSelection: true).ToList();
        Assert.Single(results);
        Assert.Equal("wrap", results[0].Snippet.Metadata.Shortcode);
    }

    [Fact]
    public void GetByContext_ExcludesSurroundSnippets_WhenNoSelection()
    {
        var index = new SnippetIndex();
        index.Rebuild(
        [
            Entry(MakeSnippet("wrap", surroundsWith: true, context: ["global"])),
            Entry(MakeSnippet("normal", surroundsWith: false, context: ["global"])),
        ]);

        var results = index.GetByContext(null, hasSelection: false).ToList();
        Assert.Single(results);
        Assert.Equal("normal", results[0].Snippet.Metadata.Shortcode);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyQuery_ReturnsAll()
    {
        var index = new SnippetIndex();
        index.Rebuild(
        [
            Entry(MakeSnippet("sel")),
            Entry(MakeSnippet("ins")),
        ]);

        Assert.Equal(2, index.Search("").Count());
        Assert.Equal(2, index.Search("  ").Count());
    }

    [Fact]
    public void Search_MatchesShortcode()
    {
        var index = new SnippetIndex();
        index.Rebuild(
        [
            Entry(MakeSnippet("select_all")),
            Entry(MakeSnippet("insert_row")),
        ]);

        var results = index.Search("select").ToList();
        Assert.Single(results);
        Assert.Equal("select_all", results[0].Snippet.Metadata.Shortcode);
    }

    [Fact]
    public void Search_MatchesName()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("s1", name: "Insert New Order"))]);

        var results = index.Search("order").ToList();
        Assert.Single(results);
    }

    [Fact]
    public void Search_MatchesDescription()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("s1", description: "Queries for reporting"))]);

        Assert.Single(index.Search("reporting").ToList());
    }

    [Fact]
    public void Search_MatchesTags()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("s1", tags: ["dml", "transaction"]))]);

        Assert.Single(index.Search("transaction").ToList());
        Assert.Empty(index.Search("nonexistent").ToList());
    }

    [Fact]
    public void Search_MatchesBodyContent()
    {
        var snippet = MakeSnippet("s1");
        snippet.Body = ["SELECT * FROM dbo.Orders WHERE Status = 'Active'"];
        var index = new SnippetIndex();
        index.Rebuild([Entry(snippet)]);

        Assert.Single(index.Search("active").ToList());
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("SELECT_ALL"))]);

        Assert.Single(index.Search("select").ToList());
        Assert.Single(index.Search("SELECT").ToList());
    }

    [Fact]
    public void Search_ReturnsEmpty_WhenNoMatches()
    {
        var index = new SnippetIndex();
        index.Rebuild([Entry(MakeSnippet("sel"))]);

        Assert.Empty(index.Search("zzznomatch"));
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_ReturnsAllSnippets()
    {
        var index = new SnippetIndex();
        index.Rebuild(
        [
            Entry(MakeSnippet("a")),
            Entry(MakeSnippet("b")),
            Entry(MakeSnippet("c")),
        ]);

        Assert.Equal(3, index.GetAll().Count());
    }

    [Fact]
    public void GetAll_ReturnsEmpty_WhenIndexIsEmpty()
    {
        var index = new SnippetIndex();
        Assert.Empty(index.GetAll());
    }
}
