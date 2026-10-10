using System.Text.RegularExpressions;
using AkmlSql.Web.Tests.Shell;
using Xunit;

namespace AkmlSql.Web.Tests.Theme;

/// <summary>
/// Spec 041 (T008, research R74, FR-091, SC-010) — the web edition's colours come from the theme tokens,
/// so every surface follows Light, Dark and High contrast. Two gates, mirroring
/// <c>tests/AkmlSql.Core.Tests/Theme/HardcodedHexScannerTests.cs</c> for the WPF shell:
/// <list type="bullet">
/// <item>Literal colours (hex, <c>rgb()</c>/<c>rgba()</c>/<c>hsl()</c>/<c>hsla()</c>) in app.css, the
/// component stylesheets and every razor <c>&lt;style&gt;</c> block may only go DOWN: each file may hold at
/// most the count <c>css-literal-allowlist.txt</c> records for it (today's real inventory), and a file
/// not listed may hold none. A <c>var(--token, #hex)</c> fallback is counted separately.</item>
/// <item>Every <c>--akml-*</c> variable used must be defined — by the generated theme or by a
/// declaration in the web edition's own sources — so nothing silently falls back to a literal or to
/// nothing (the four undefined names spec 041 found).</item>
/// </list>
/// The generated theme files and the developer-only /spike page are not scanned.
/// </summary>
public sealed class WebCssTokenTests
{
    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Url = new(@"url\([^)]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex StyleBlock = new(@"<style[^>]*>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Fallback = new(@"var\(\s*--[A-Za-z0-9-]+\s*,\s*(?:#[0-9a-fA-F]{3,8}|(?:rgba?|hsla?)\([^)]*\))\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex Literal = new(@"#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(", RegexOptions.CultureInvariant);
    private static readonly Regex VarUse = new(@"var\(\s*(--akml-[a-z0-9-]+)", RegexOptions.CultureInvariant);
    private static readonly Regex VarDecl = new(@"(--akml-[a-z0-9-]+)\s*:|setProperty\(\s*['""](--akml-[a-z0-9-]+)['""]", RegexOptions.CultureInvariant);

    private static string WebRoot => Path.Combine(IconSpriteCoverageTests.RepoRoot(), "src", "AkmlSql.Web");

    private static string Relative(string path) =>
        Path.GetRelativePath(WebRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsBuildOutput(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
        || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
        || path.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar);

    /// <summary>(relative path, scanned CSS text) for every stylesheet and razor style block.</summary>
    private static IEnumerable<(string Path, string Css)> ScannedCss()
    {
        var css = Path.Combine(WebRoot, "wwwroot", "css");
        yield return ("wwwroot/css/app.css", File.ReadAllText(Path.Combine(css, "app.css")));
        foreach (var f in Directory.EnumerateFiles(Path.Combine(css, "components"), "*.css").OrderBy(f => f, StringComparer.Ordinal))
            yield return (Relative(f), File.ReadAllText(f));

        foreach (var f in Directory.EnumerateFiles(WebRoot, "*.razor", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (IsBuildOutput(f) || Path.GetFileName(f) == "Spike.razor") continue;
            var blocks = StyleBlock.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value).ToArray();
            if (blocks.Length > 0) yield return (Relative(f), string.Join("\n", blocks));
        }
    }

    private static (int Literals, int Fallbacks) Count(string css)
    {
        var text = Url.Replace(Comment.Replace(css, " "), " ");
        int fallbacks = Fallback.Matches(text).Count;
        text = Fallback.Replace(text, " ");
        return (Literal.Matches(text).Count, fallbacks);
    }

    private static Dictionary<string, (int Literals, int Fallbacks)> AllowList()
    {
        var path = Path.Combine(IconSpriteCoverageTests.RepoRoot(), "tests", "AkmlSql.Web.Tests", "Theme", "css-literal-allowlist.txt");
        var map = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            map[parts[0]] = (int.Parse(parts[1]), int.Parse(parts[2]));
        }
        return map;
    }

    [Fact]
    public void Literal_colours_only_go_down()
    {
        var allowed = AllowList();
        var over = new List<string>();
        var inventory = new List<string>();
        foreach (var (path, css) in ScannedCss())
        {
            var (literals, fallbacks) = Count(css);
            if (literals > 0 || fallbacks > 0) inventory.Add($"{path}\t{literals}\t{fallbacks}");
            allowed.TryGetValue(path, out var limit);
            if (literals > limit.Literals || fallbacks > limit.Fallbacks)
                over.Add($"{path}: {literals} literal(s) (allowed {limit.Literals}), {fallbacks} fallback(s) (allowed {limit.Fallbacks})");
        }
        Assert.True(over.Count == 0,
            "New literal colours — use a --akml-* token instead (add one to docs/theme-tokens.json if none fits):\n"
            + string.Join("\n", over)
            + "\n\nCurrent inventory (only lower the allow-list, never raise it):\n" + string.Join("\n", inventory));
    }

    [Fact]
    public void Every_akml_variable_used_is_defined()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var light = File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "css", "themes", "light.css"));
        foreach (Match m in VarDecl.Matches(light)) defined.Add(m.Groups[1].Value);

        // Declarations in the web edition's own sources: app.css (motion tokens), components, inline
        // style attributes written from .NET (grid tracks, workspace sizes) and JS setProperty calls.
        var sources = Directory.EnumerateFiles(WebRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f) && !f.Contains(Path.DirectorySeparatorChar + "themes" + Path.DirectorySeparatorChar))
            .Where(f => f.EndsWith(".css", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal)
                        || (f.EndsWith(".js", StringComparison.Ordinal) && !f.Contains(Path.DirectorySeparatorChar + "lib" + Path.DirectorySeparatorChar)));
        foreach (var f in sources)
        {
            foreach (Match m in VarDecl.Matches(File.ReadAllText(f)))
                defined.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        }

        var undefined = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (path, css) in ScannedCss())
        {
            foreach (Match m in VarUse.Matches(Comment.Replace(css, " ")))
                if (!defined.Contains(m.Groups[1].Value)) undefined.Add($"{m.Groups[1].Value} ({path})");
        }
        Assert.True(undefined.Count == 0, "Undefined --akml-* variables:\n" + string.Join("\n", undefined));
    }
}
