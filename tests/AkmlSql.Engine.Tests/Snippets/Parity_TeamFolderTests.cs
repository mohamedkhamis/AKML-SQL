using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;
using Xunit;

namespace AkmlSql.Engine.Tests.Snippets;

/// <summary>
/// Spec 030 parity — shared/team snippet folder. Validates that
/// <see cref="SnippetRequestHandler"/> loads <c>.akmlsnippet</c> files from a team folder
/// when the optional third constructor argument is supplied, and that snippets from that
/// folder are surfaced as <see cref="SnippetSourceType.Team"/> (source == 2).
///
/// The EngineHandlerRegistry wiring (<c>ctx.EnsureSettings().Snippets.TeamFolder</c>) is
/// covered separately at the composition root; these tests characterize the handler-level
/// behaviour directly without the IPC stack.
/// </summary>
public sealed class Parity_TeamFolderTests : IDisposable
{
    private readonly string _personalDir;
    private readonly string _builtInDir;
    private readonly string _teamDir;

    public Parity_TeamFolderTests()
    {
        var tag = Guid.NewGuid().ToString("N");
        _personalDir = Path.Combine(Path.GetTempPath(), $"akml_tf_personal_{tag}");
        _builtInDir  = Path.Combine(Path.GetTempPath(), $"akml_tf_builtin_{tag}");
        _teamDir     = Path.Combine(Path.GetTempPath(), $"akml_tf_team_{tag}");
        Directory.CreateDirectory(_personalDir);
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_teamDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_personalDir, true); } catch { }
        try { Directory.Delete(_builtInDir, true); } catch { }
        try { Directory.Delete(_teamDir, true); } catch { }
    }

    private static void WriteSnippet(string dir, string shortcode, params string[] body)
    {
        var bodyJson = string.Join(", ", body.Select(b => System.Text.Json.JsonSerializer.Serialize(b)));
        var json = $@"{{
  ""metadata"": {{ ""id"": ""team-{shortcode}"", ""shortcode"": ""{shortcode}"", ""name"": ""{shortcode}"",
                  ""description"": """", ""category"": ""Query"", ""context"": [ ""global"" ], ""surroundsWith"": false }},
  ""variables"": [],
  ""body"": [ {bodyJson} ]
}}";
        File.WriteAllText(Path.Combine(dir, $"{shortcode}.akmlsnippet"), json);
    }

    // ── Team folder supplied — snippets load and are tagged as Team ───────

    [Fact]
    public void TeamFolder_Supplied_SnippetAppearsInList_WithTeamSource()
    {
        WriteSnippet(_teamDir, "ztm1", "SELECT 'team'");
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir, teamFolder: _teamDir);

        var response = handler.HandleList(new SnippetListRequest());

        Assert.NotNull(response.Snippets);
        var teamSnippet = response.Snippets.FirstOrDefault(s => s.Shortcode == "ztm1");
        Assert.NotNull(teamSnippet);
        Assert.Equal((int)SnippetSourceType.Team, teamSnippet.Source);
    }

    [Fact]
    public void TeamFolder_Supplied_SnippetExpandable_ByShortcode()
    {
        WriteSnippet(_teamDir, "ztm2", "SELECT 'from team folder'");
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir, teamFolder: _teamDir);

        var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "ztm2" });

        Assert.True(resp.Success);
        Assert.Contains("from team folder", resp.ExpandedText);
    }

    // ── Team folder not supplied (null) — no crash, no team snippets ──────

    [Fact]
    public void TeamFolder_Null_NoTeamSnippets_NoCrash()
    {
        WriteSnippet(_personalDir, "ztm3", "SELECT 'personal'");
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir, teamFolder: null);

        var response = handler.HandleList(new SnippetListRequest());

        Assert.NotNull(response.Snippets);
        Assert.DoesNotContain(response.Snippets, s => s.Source == (int)SnippetSourceType.Team);
        // Personal snippet is still present
        Assert.Contains(response.Snippets, s => s.Shortcode == "ztm3");
    }

    // ── Team folder supplied but empty string — treated as not set ────────

    [Fact]
    public void TeamFolder_EmptyString_NoTeamSnippets_NoCrash()
    {
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir, teamFolder: string.Empty);

        var response = handler.HandleList(new SnippetListRequest());

        Assert.NotNull(response.Snippets);
        Assert.DoesNotContain(response.Snippets, s => s.Source == (int)SnippetSourceType.Team);
    }

    // ── Priority: personal overrides team for the same shortcode ─────────

    [Fact]
    public void TeamFolder_PersonalSnippetWinsOnShortcodeCollision()
    {
        WriteSnippet(_personalDir, "ztmcol", "SELECT 'personal wins'");
        WriteSnippet(_teamDir,     "ztmcol", "SELECT 'team loses'");
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir, teamFolder: _teamDir);

        var resp = handler.HandleExpand(new SnippetExpandRequest { Shortcode = "ztmcol" });

        Assert.True(resp.Success);
        Assert.Contains("personal wins", resp.ExpandedText);
    }
}
