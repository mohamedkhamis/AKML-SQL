using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Snippets;

/// <summary>
/// Spec 030 T041 parity — SQL-Prompt-style built-in snippet pack.
/// Validates that the 12 new .akmlsnippet files shipped in SnippetPack/ (cdb, crt, csp, cvi,
/// cfn, cix, drp, ie, st, bt, tc, prt) are picked up by <see cref="SnippetLoader"/>,
/// resolve via <see cref="SnippetRequestHandler.HandleExpand"/>, and that expanding one
/// of them returns the expected body text with a valid cursor offset.
/// </summary>
public sealed class Parity2_BuiltInPackTests
{
    /// <summary>
    /// The built-in pack directory as copied into the test output by the test .csproj glob.
    /// Tests reference this directory directly so they exercise the SAME physical files that
    /// ship with the engine publish output.
    /// </summary>
    private static string PackDir => Path.Combine(AppContext.BaseDirectory, "snippets");

    // ── Prerequisite: the pack directory must exist after build ──────────────

    [Fact]
    public void PackDirectory_ExistsInTestOutput()
    {
        Assert.True(Directory.Exists(PackDir),
            $"Built-in snippet pack folder missing from test output: {PackDir}");
    }

    // ── SnippetLoader.LoadFromDirectory includes the new shortcodes ──────────

    [Theory]
    [InlineData("cdb")]
    [InlineData("crt")]
    [InlineData("csp")]
    [InlineData("cvi")]
    [InlineData("cfn")]
    [InlineData("cix")]
    [InlineData("drp")]
    [InlineData("ie")]
    [InlineData("st")]
    [InlineData("bt")]
    [InlineData("tc")]
    [InlineData("prt")]
    public void SnippetLoader_ListsNewBuiltInShortcode(string shortcode)
    {
        var loader = new SnippetLoader();
        var results = loader.LoadFromDirectory(PackDir, SnippetSourceType.BuiltIn);

        Assert.Contains(results, r =>
            string.Equals(r.Snippet.Metadata.Shortcode, shortcode, StringComparison.OrdinalIgnoreCase));
    }

    // ── SnippetLoader tags all loaded snippets as BuiltIn ────────────────────

    [Fact]
    public void SnippetLoader_AllNewSnippets_TaggedAsBuiltIn()
    {
        var newShortcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cdb", "crt", "csp", "cvi", "cfn", "cix", "drp", "ie", "st", "bt", "tc", "prt"
        };

        var loader = new SnippetLoader();
        var results = loader.LoadFromDirectory(PackDir, SnippetSourceType.BuiltIn);

        var newOnes = results.Where(r => newShortcodes.Contains(r.Snippet.Metadata.Shortcode)).ToList();
        Assert.True(newOnes.Count >= 12,
            $"Expected at least 12 new built-ins, found {newOnes.Count}");
        Assert.All(newOnes, r => Assert.Equal(SnippetSourceType.BuiltIn, r.Source));
    }

    // ── Each new file has a $CURSOR$ marker in its source JSON ──────────────

    [Theory]
    [InlineData("cdb")]
    [InlineData("crt")]
    [InlineData("csp")]
    [InlineData("cvi")]
    [InlineData("cfn")]
    [InlineData("cix")]
    [InlineData("drp")]
    [InlineData("ie")]
    [InlineData("st")]
    [InlineData("bt")]
    [InlineData("tc")]
    [InlineData("prt")]
    public void NewSnippetFile_ContainsCursorMarker(string shortcode)
    {
        var path = Path.Combine(PackDir, shortcode + ".akmlsnippet");
        Assert.True(File.Exists(path), $"File not found: {path}");

        var raw = File.ReadAllText(path);
        Assert.Contains("$CURSOR$", raw);
    }

    // ── HandleExpand resolves and expands one new snippet end-to-end ─────────

    [Fact]
    public void HandleExpand_Csp_SucceedsAndContainsExpectedKeywords()
    {
        // "csp" = CREATE OR ALTER PROCEDURE skeleton — representative DDL snippet
        var personalDir = Path.Combine(Path.GetTempPath(), $"akml_p2_personal_{Guid.NewGuid():N}");
        Directory.CreateDirectory(personalDir);
        try
        {
            var handler = new SnippetRequestHandler(personalDir, PackDir);
            var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "csp" });

            Assert.True(resp.Success, "csp snippet should expand successfully");
            Assert.False(string.IsNullOrWhiteSpace(resp.ExpandedText), "expanded text must be non-empty");
            Assert.Contains("CREATE OR ALTER PROCEDURE", resp.ExpandedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SET NOCOUNT ON", resp.ExpandedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("$CURSOR$", resp.ExpandedText);   // marker must be stripped
            Assert.True(resp.CursorOffset >= 0, "cursor offset must be non-negative on success");
        }
        finally
        {
            try { Directory.Delete(personalDir, true); } catch { }
        }
    }

    // ── HandleExpand resolves a minimal new snippet (cdb) ────────────────────

    [Fact]
    public void HandleExpand_Cdb_SucceedsAndContainsCreateDatabase()
    {
        var personalDir = Path.Combine(Path.GetTempPath(), $"akml_p2_cdb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(personalDir);
        try
        {
            var handler = new SnippetRequestHandler(personalDir, PackDir);
            var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "cdb" });

            Assert.True(resp.Success);
            Assert.Contains("CREATE DATABASE", resp.ExpandedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("$CURSOR$", resp.ExpandedText);
        }
        finally
        {
            try { Directory.Delete(personalDir, true); } catch { }
        }
    }

    // ── SnippetLoader total count includes the 12 new files ─────────────────

    [Fact]
    public void SnippetLoader_TotalCount_IncludesNewBuiltIns()
    {
        // Previously 6 shipped built-ins (ssf, sel, ins, upd, del, cte).
        // We added 12 more, so the pack must now contain >= 18 files.
        var loader = new SnippetLoader();
        var results = loader.LoadFromDirectory(PackDir, SnippetSourceType.BuiltIn);

        Assert.True(results.Count >= 18,
            $"Expected >= 18 built-in snippets after adding the SQL-Prompt pack, found {results.Count}");
    }
}
