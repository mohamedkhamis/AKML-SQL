using System.Text.RegularExpressions;
using AkmlSql.Web.Tests.Shell;
using Xunit;

namespace AkmlSql.Web.Tests.Theme;

/// <summary>
/// Spec 043 (US4, T090, FR-032): every CSS rule of the web edition that sets both a text colour and a background from
/// theme tokens reads at 4.5:1 in the light and the dark theme. The dark walk found six rules that put white text on a
/// status colour (2.3:1 to 2.8:1 in dark); this keeps that class of defect from coming back. Secondary and hover text may
/// sit at 3:1 (FR-032), and only the rules listed in <see cref="SecondaryText"/> do.
/// </summary>
public sealed class ThemeContrastScanTests
{
    /// <summary>Rules whose text is secondary or a hover state: held to 3:1, not 4.5:1.</summary>
    private static readonly HashSet<string> SecondaryText = new(StringComparer.Ordinal)
    {
        ".akml-results-set-header",   // "Result set 2 · 21 rows": secondary text on the elevated surface, 4.0:1 in dark
        ".akml-nav-link:hover",       // a navigation link while the pointer is on it, 4.1:1 in dark
    };

    private static string Web => Path.Combine(IconSpriteCoverageTests.RepoRoot(), "src", "AkmlSql.Web");

    private static Dictionary<string, string> Tokens(string theme)
    {
        var css = File.ReadAllText(Path.Combine(Web, "wwwroot", "css", "themes", theme + ".css"));
        return Regex.Matches(css, @"(--akml-[a-z0-9-]+)\s*:\s*([^;]+);").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
    }

    private static IEnumerable<(string File, string Css)> Sources()
    {
        foreach (var file in Directory.EnumerateFiles(Web, "*.*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Web, file);
            if (rel.StartsWith("bin") || rel.StartsWith("obj") || rel.Contains("node_modules") || rel.Contains(Path.Combine("css", "themes"))
                || rel.StartsWith("tools") || rel.Contains(Path.Combine("wwwroot", "lib"))) continue;
            if (file.EndsWith(".css", StringComparison.Ordinal)) yield return (rel, File.ReadAllText(file));
            else if (file.EndsWith(".razor", StringComparison.Ordinal))
            {
                var blocks = Regex.Matches(File.ReadAllText(file), @"<style[^>]*>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (blocks.Count > 0) yield return (rel, string.Join("\n", blocks.Select(b => b.Groups[1].Value)));
            }
        }
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Token_text_on_token_backgrounds_meets_its_contrast_target(string theme)
    {
        var tokens = Tokens(theme);
        var panel = Parse(tokens["--akml-surface-panel"])!.Value;
        var failures = new List<string>();
        int checkedPairs = 0;

        foreach (var (file, raw) in Sources())
        {
            var css = Regex.Replace(raw, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
            {
                var selector = Regex.Replace(rule.Groups[1].Value.Trim(), @"\s+", " ");
                var body = rule.Groups[2].Value;
                var color = Regex.Match(body, @"(?:^|;|\s)color\s*:\s*([^;]+)");
                var background = Regex.Match(body, @"background(?:-color)?\s*:\s*([^;]+)");
                if (!color.Success || !background.Success || !color.Groups[1].Value.Contains("--akml-") || !background.Groups[1].Value.Contains("--akml-")) continue;

                var fg = Resolve(color.Groups[1].Value, tokens);
                var bg = Resolve(background.Groups[1].Value, tokens);
                if (fg == null || bg == null) continue;
                var back = Blend(bg.Value, panel);
                var ratio = Contrast(Blend(fg.Value, back), back);
                var target = SecondaryText.Contains(selector) ? 3.0 : 4.5;
                checkedPairs++;
                if (ratio < target) failures.Add($"{ratio:F2}:1 < {target}:1  {file}  {selector}  [{color.Groups[1].Value.Trim()} on {background.Groups[1].Value.Trim()}]");
            }
        }

        Assert.True(checkedPairs > 20, $"only {checkedPairs} pairs were found; the scan is not reading the styles");
        Assert.True(failures.Count == 0, $"{theme}:\n" + string.Join("\n", failures));
    }

    private static (double R, double G, double B, double A)? Resolve(string expression, Dictionary<string, string> tokens)
    {
        var m = Regex.Match(expression, @"var\(\s*(--akml-[a-z0-9-]+)\s*(?:,\s*([^)]+))?\)");
        if (!m.Success) return null;
        var value = tokens.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Groups[2].Success ? m.Groups[2].Value.Trim() : null;
        if (value == null) return null;
        return value.StartsWith("var(", StringComparison.Ordinal) ? Resolve(value, tokens) : Parse(value);
    }

    private static (double R, double G, double B, double A)? Parse(string value)
    {
        var hex = Regex.Match(value, "^#([0-9a-fA-F]{6})$");
        if (hex.Success)
        {
            int c(int i) => Convert.ToInt32(hex.Groups[1].Value.Substring(i * 2, 2), 16);
            return (c(0), c(1), c(2), 1);
        }
        var rgba = Regex.Match(value, @"^rgba?\(([^)]+)\)$");
        if (!rgba.Success) return null;   // a system colour (high contrast) or a keyword
        var p = rgba.Groups[1].Value.Split(',').Select(x => double.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return (p[0], p[1], p[2], p.Length > 3 ? p[3] : 1);
    }

    private static (double R, double G, double B, double A) Blend((double R, double G, double B, double A) top, (double R, double G, double B, double A) under) =>
        top.A >= 1 ? top : (top.R * top.A + under.R * (1 - top.A), top.G * top.A + under.G * (1 - top.A), top.B * top.A + under.B * (1 - top.A), 1);

    private static double Contrast((double R, double G, double B, double A) a, (double R, double G, double B, double A) b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance((double R, double G, double B, double A) c)
    {
        static double Channel(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
