using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T010, research R62) — every icon the web edition names must exist in the generated
/// sprite. A razor file that asks for an icon the sprite lacks renders an empty box, and nothing
/// else would catch it: the browser reports no error for a missing <c>&lt;use&gt;</c> target.
/// </summary>
public sealed class IconSpriteCoverageTests
{
    private static readonly Regex IconUse = new(@"<Icon\b[^>]*?\bName=""([a-z0-9-]+)""", RegexOptions.CultureInvariant);
    private static readonly Regex SymbolId = new(@"<symbol id=""akml-icon-([a-z0-9-]+)""", RegexOptions.CultureInvariant);

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (AKML-SQL.slnx) not found.");
    }

    private static string WebRoot => Path.Combine(RepoRoot(), "src", "AkmlSql.Web");

    private static HashSet<string> SpriteSymbols()
    {
        var sprite = File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "lib", "icons", "akml-icons.svg"));
        return SymbolId.Matches(sprite).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Every_icon_named_in_a_razor_file_is_in_the_sprite()
    {
        var symbols = SpriteSymbols();
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(WebRoot, "*.razor", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            foreach (Match m in IconUse.Matches(File.ReadAllText(file)))
            {
                if (!symbols.Contains(m.Groups[1].Value))
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            }
        }
        Assert.True(missing.Count == 0, "Icons missing from the sprite (add them to tools/icons/icons.json and rebuild):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Sprite_holds_exactly_the_allow_listed_icons()
    {
        var json = File.ReadAllText(Path.Combine(WebRoot, "tools", "icons", "icons.json"));
        var listed = JsonSerializer.Deserialize<string[]>(json)!.ToHashSet(StringComparer.Ordinal);
        var symbols = SpriteSymbols();

        Assert.Empty(listed.Except(symbols));   // listed but not built: rerun build-sprite.mjs
        Assert.Empty(symbols.Except(listed));   // built but not listed: the sprite is stale
    }

    // ── Spec 041 (T204, research R62, FR-081) — one icon set, nothing else ─────────────────────────────

    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex BlockComment = new(@"/\*.*?\*/|<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex LineComment = new(@"(^|[\s;{}])//[^\n]*", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex CommandIcon = new(@"\bIcon\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);

    /// <summary>Glyphs that stay text on purpose. Empty today: every chevron, star and arrow is an Icon.</summary>
    private static readonly HashSet<char> AllowedGlyphs = new();

    private static bool IsIconLikeGlyph(char c) =>
        c is >= '\u2190' and <= '\u21FF'      // arrows
        or >= '\u2300' and <= '\u23FF'        // technical (⌘ ⏎)
        or >= '\u2460' and <= '\u24FF'        // enclosed alphanumerics
        or >= '\u25A0' and <= '\u25FF'        // geometric shapes (▶ ▾ ● ◆)
        or >= '\u2600' and <= '\u27BF'        // miscellaneous symbols and dingbats (★ ⚠ ✕ ✓ ⚙)
        or >= '\u2900' and <= '\u297F'        // supplemental arrows (⤢)
        or >= '\u2B00' and <= '\u2BFF'        // miscellaneous symbols and arrows
        or '\uFE0F'                           // emoji presentation selector
        || char.IsSurrogate(c);               // emoji outside the BMP

    private static IEnumerable<string> WebSources(string pattern) =>
        Directory.EnumerateFiles(WebRoot, pattern, SearchOption.AllDirectories).Where(f =>
            !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
            && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
            && !f.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar)
            && Path.GetFileName(f) != "Spike.razor");

    [Fact]
    public void NoEmojiInRazor()
    {
        var found = new List<string>();
        foreach (var file in WebSources("*.razor"))
        {
            var text = LineComment.Replace(BlockComment.Replace(RazorComment.Replace(File.ReadAllText(file), " "), " "), "$1");
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var glyphs = lines[i].Where(c => IsIconLikeGlyph(c) && !AllowedGlyphs.Contains(c)).Distinct().ToArray();
                if (glyphs.Length > 0)
                    found.Add($"{Path.GetFileName(file)}: '{new string(glyphs)}' in: {lines[i].Trim()}");
            }
        }
        Assert.True(found.Count == 0, "Emoji or symbol glyphs in razor markup (use Shared/Icon.razor):\n" + string.Join("\n", found));
    }

    [Fact]
    public void Every_command_icon_is_in_the_sprite()
    {
        var symbols = SpriteSymbols();
        var missing = new List<string>();
        foreach (var file in WebSources("*.razor").Concat(WebSources("*.cs")))
        {
            foreach (Match m in CommandIcon.Matches(File.ReadAllText(file)))
            {
                if (!symbols.Contains(m.Groups[1].Value))
                    missing.Add($"{Path.GetFileName(file)}: Icon = \"{m.Groups[1].Value}\"");
            }
        }
        Assert.True(missing.Count == 0, "Command icons must be sprite names (Shared/Icon.razor):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_view_command_names_a_sprite_icon()
    {
        var symbols = SpriteSymbols();
        var host = new NoViewHost();

        Assert.All(AkmlSql.Web.Services.WorkspaceViewActions.Build(host), a => Assert.Contains(a.Icon, symbols));
        Assert.Contains(new AkmlSql.Web.Services.CommandAction { Id = "x", Title = "x", Group = AkmlSql.Web.Services.CommandGroup.Action, Run = () => Task.CompletedTask }.Icon, symbols);
    }

    private sealed class NoViewHost : AkmlSql.Web.Services.IWorkspaceViewHost
    {
        public AkmlSql.Web.Services.WorkspaceLayout Layout { get; } = new();
        public Task ToggleNavAsync() => Task.CompletedTask;
        public Task ToggleSchemaAsync() => Task.CompletedTask;
        public Task ToggleResultsAsync() => Task.CompletedTask;
        public Task ToggleAiAsync() => Task.CompletedTask;
        public Task ToggleMaximiseResultsAsync() => Task.CompletedTask;
        public Task CyclePanesAsync(bool backwards) => Task.CompletedTask;
        public Task ResetLayoutAsync() => Task.CompletedTask;
    }
}
