using System.Text.RegularExpressions;
using AkmlSql.Web.Tests.Shell;
using Xunit;

namespace AkmlSql.Web.Tests.Theme;

/// <summary>
/// Spec 043 (T063, research R12, R13, FR-016, FR-018, FR-019) — the generated theme files declare the editor's line
/// height and the four editor colours, and the syntax colours measured against the editor surface meet 4.5:1 in the
/// light and dark themes. The generator writes these from <c>docs/theme-tokens.json</c>; this pins the output, so a
/// change to either that drops a variable or a contrast target fails here before it reaches a browser.
/// </summary>
public sealed class EditorThemeTokenTests
{
    private static string Theme(string file) =>
        File.ReadAllText(Path.Combine(IconSpriteCoverageTests.RepoRoot(), "src", "AkmlSql.Web", "wwwroot", "css", "themes", file));

    private static string? Var(string css, string name)
    {
        var m = Regex.Match(css, $@"{Regex.Escape(name)}\s*:\s*([^;]+);");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    [Theory]
    [InlineData("light.css")]
    [InlineData("dark.css")]
    [InlineData("high-contrast.css")]
    public void Every_theme_declares_the_editor_line_height_and_colours(string file)
    {
        var css = Theme(file);

        Assert.Equal("1.25", Var(css, "--akml-type-editor-line-height"));
        foreach (var name in new[] { "--akml-editor-selection", "--akml-editor-activeline", "--akml-editor-matchingbracket", "--akml-editor-searchmatch" })
        {
            Assert.False(string.IsNullOrWhiteSpace(Var(css, name)), $"{file}: {name}");
        }
    }

    [Theory]
    [InlineData("light.css")]
    [InlineData("dark.css")]
    public void Syntax_colours_read_at_4_5_on_the_editor_and_on_the_active_line(string file)
    {
        var css = Theme(file);
        var editor = Var(css, "--akml-surface-editor")!;
        var activeLine = Var(css, "--akml-editor-activeline")!;

        foreach (var token in new[] { "keyword", "string", "number", "comment", "function", "tablecolumn" })
        {
            var colour = Var(css, $"--akml-syntax-{token}")!;
            Assert.True(Contrast(colour, editor) >= 4.5, $"{file} {token} on the editor: {Contrast(colour, editor):F2}");
            Assert.True(Contrast(colour, activeLine) >= 4.5, $"{file} {token} on the active line: {Contrast(colour, activeLine):F2}");
        }

        var text = Var(css, "--akml-text-primary")!;
        Assert.True(Contrast(text, Var(css, "--akml-editor-selection")!) >= 4.5, $"{file} text on the selection");
        Assert.True(Contrast(text, Var(css, "--akml-editor-searchmatch")!) >= 4.5, $"{file} text on a find match");
        Assert.True(Contrast(Var(css, "--akml-editor-matchingbracket")!, editor) >= 3.0, $"{file} bracket outline on the editor");
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        Assert.Matches("^#[0-9a-fA-F]{6}$", hex);
        double Channel(int i)
        {
            var v = Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }
}
