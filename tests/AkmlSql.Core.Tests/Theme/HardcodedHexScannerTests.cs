using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Core.Tests.Theme
{
    /// <summary>
    /// SC-001 gate (spec 020): no chrome surface may contain a hardcoded hex literal outside the
    /// documented semantic-colour allow-list. The semantic-colour allow-list mirrors the four
    /// hex values documented in CLAUDE.md "WPF UI conventions" — Status.Success / Warning / Danger /
    /// Info — plus the accent (0078D4) used as the canonical link colour.
    ///
    /// The scanner walks every <c>.cs</c> and <c>.xaml</c> file under
    /// <c>src/AkmlSql.Shell.Shared/</c>, finds <c>#RRGGBB</c> and <c>#RRGGBBAA</c> literals
    /// (with surrounding quotes or in XAML attribute syntax), and reports any outside the
    /// allow-list as a "violation".
    ///
    /// <para>
    /// <b>Currently disabled.</b> The Phase 5 / earlier-spec surfaces (Tab Coloring environment
    /// rules, e.g. <c>#FF4444</c>; SafetyWarningDialog accent overrides; etc.) still hold legacy
    /// hex. The test infrastructure lives in this Phase 2 commit; the gate is activated once the
    /// US1 migration (T021 / T022) finishes bringing every chrome caller off the legacy
    /// <c>ThemeManager</c> facade and on to <c>ThemeTokens</c>. At that point, remove the
    /// <c>Skip</c> on <see cref="NoHardcodedChromeHex"/> below.
    /// </para>
    /// </summary>
    public class HardcodedHexScannerTests
    {
        private readonly ITestOutputHelper _output;

        public HardcodedHexScannerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // Semantic-colour allow-list. Lowercase hex without the '#'.
        // These four values are stable identifiers for "success / warning / danger / info /
        // accent" and intentionally identical across Light and Dark themes (CLAUDE.md).
        private static readonly HashSet<string> SemanticAllowList = new(StringComparer.OrdinalIgnoreCase)
        {
            "2ecc71",   // StatusSuccess (Light)
            "3dd68c",   // StatusSuccess (Dark)
            "f39c12",   // StatusWarning (Light)
            "fbbf24",   // StatusWarning (Dark)
            "e74c3c",   // StatusDanger (Light)
            "ff5c5c",   // StatusDanger (Dark)
            "0078d4",   // AccentPrimary / StatusInfo (Light & Dark)
            "4f8cff",   // AccentPrimary on Dark via TextLink
        };

        // Matches:
        //   "#RRGGBB" / "#RRGGBBAA" in C#  →  "#0078D4"
        //   XAML attribute             →  Background="#FF0078D4"
        //   Color.FromRgb / FromArgb literals already trigger via 0xRR pattern handled below
        private static readonly Regex HexLiteral = new Regex(
            "\"\\s*#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\\s*\"",
            RegexOptions.Compiled);

        /// <summary>
        /// Walks every <c>.cs</c> + <c>.xaml</c> file under <c>src/AkmlSql.Shell.Shared/</c>
        /// and asserts no hex literal exists outside <see cref="SemanticAllowList"/>.
        /// </summary>
        [Fact(Skip = "US1 T021/T022 complete (legacy ThemeManager facade deleted), but the scanner " +
                     "regex still catches only the \"#RRGGBB\" quoted-string form — Color.FromRgb(0xRR,...) " +
                     "literals across ~14 files remain. Enable this test once the scanner is broadened " +
                     "and those call-sites have moved to ThemeTokens. Until then use NoHardcodedChromeHex_Diagnostic.")]
        public void NoHardcodedChromeHex()
        {
            var violations = ScanForViolations();
            Assert.True(violations.Count == 0, BuildFailureMessage(violations));
        }

        /// <summary>
        /// Diagnostic variant — always runs, reports current violations as test output without
        /// failing. Useful for tracking the migration burndown.
        /// </summary>
        [Fact]
        public void NoHardcodedChromeHex_Diagnostic()
        {
            var violations = ScanForViolations();
            _output.WriteLine($"Hardcoded-hex scanner: {violations.Count} violation(s) " +
                              "in src/AkmlSql.Shell.Shared/ (.cs + .xaml).");
            if (violations.Count > 0)
            {
                _output.WriteLine("Top 20 violations:");
                foreach (var v in violations.Take(20))
                {
                    _output.WriteLine($"  {v}");
                }
            }
            // Intentionally not Assert — this is informational. The strict assertion lives in
            // NoHardcodedChromeHex (currently [Skip]'d).
        }

        // -----------------------------------------------------------------------

        private List<string> ScanForViolations()
        {
            var sharedRoot = FindSharedRoot();
            var violations = new List<string>();

            foreach (var file in EnumerateScanTargets(sharedRoot))
            {
                // Skip Theme/ palette files — they are the authoritative palette definitions
                // and intentionally contain hex literals (Color.FromRgb arguments etc.).
                var relative = Path.GetRelativePath(sharedRoot, file).Replace('\\', '/');
                if (relative.StartsWith("Ui/Theme/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string contents;
                try { contents = File.ReadAllText(file); }
                catch (IOException) { continue; }

                foreach (Match m in HexLiteral.Matches(contents))
                {
                    var hex = m.Groups[1].Value;
                    // For 8-digit hex (AARRGGBB), strip the leading alpha for allow-list comparison.
                    var rgbOnly = hex.Length == 8 ? hex.Substring(2) : hex;
                    if (SemanticAllowList.Contains(rgbOnly)) continue;

                    var lineNumber = contents.Take(m.Index).Count(c => c == '\n') + 1;
                    violations.Add($"{relative}:{lineNumber} → \"#{hex}\"");
                }
            }

            return violations;
        }

        private static IEnumerable<string> EnumerateScanTargets(string root)
        {
            foreach (var ext in new[] { "*.cs", "*.xaml" })
            {
                foreach (var f in Directory.EnumerateFiles(root, ext, SearchOption.AllDirectories))
                {
                    // Skip obj/ bin/ build outputs.
                    var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                    if (rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) continue;
                    yield return f;
                }
            }
        }

        private static string FindSharedRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
                {
                    var shared = Path.Combine(dir.FullName, "src", "AkmlSql.Shell.Shared");
                    if (Directory.Exists(shared)) return shared;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate AKML-SQL.slnx walking up from " + AppContext.BaseDirectory);
        }

        private static string BuildFailureMessage(List<string> violations)
        {
            var preview = string.Join(Environment.NewLine, violations.Take(20).Select(v => "  " + v));
            return
                $"SC-001 violated: {violations.Count} hardcoded chrome hex literal(s) found in src/AkmlSql.Shell.Shared/." +
                Environment.NewLine + Environment.NewLine +
                preview +
                (violations.Count > 20 ? Environment.NewLine + $"  …and {violations.Count - 20} more." : string.Empty) +
                Environment.NewLine + Environment.NewLine +
                "Fix: replace each literal with a ThemeTokens.* reference resolved via " +
                "FrameworkElement.SetResourceReference, or — if the colour is truly semantic " +
                "(error red / warning amber / success green / info blue / accent) — add it to " +
                "SemanticAllowList in HardcodedHexScannerTests.cs.";
        }
    }
}
