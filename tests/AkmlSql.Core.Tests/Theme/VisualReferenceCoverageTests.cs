using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Core.Tests.Theme
{
    /// <summary>
    /// Spec 020 SC-003 / SC-004 supporting gate: every chrome surface enumerated in FR-005..FR-014
    /// has a <c>VisualReferencePath</c> that resolves to an existing section in <c>doc/SQL-PROMPT/</c>.
    /// This is the design-contract anchor for the screenshot review process — if a surface lacks
    /// a reference, parity cannot be objectively verified.
    ///
    /// <para>
    /// At Phase 2 (Foundational) the runtime <c>Surface</c> records do not yet exist (they land
    /// alongside the WPF re-skin tasks in P2 / P3). This test currently asserts the looser
    /// preconditions:
    /// </para>
    /// <list type="bullet">
    ///   <item>The expected reference markdown files exist in <c>doc/SQL-PROMPT/</c>.</item>
    ///   <item>Every documented reference file is non-empty.</item>
    /// </list>
    /// <para>
    /// When <c>Surface</c> records are introduced (US1 / US3), extend this test to assert
    /// each <c>Surface</c> in the catalog resolves its <c>VisualReferencePath</c> to an
    /// existing anchor inside one of these files.
    /// </para>
    /// </summary>
    public class VisualReferenceCoverageTests
    {
        private readonly ITestOutputHelper _output;

        public VisualReferenceCoverageTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // The doc/SQL-PROMPT/ files that every in-scope surface ultimately references. This is the
        // canonical visual contract listed in spec.md.
        private static readonly string[] ExpectedReferenceFiles =
        {
            "doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md",
            "doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_AI.md",
            "doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md",
            "doc/SQL-PROMPT/SQL-Prompt-History/SQL_Prompt_SQL_History.md",
        };

        // The SVG mockups every documented surface ultimately compares against. Same source as
        // the spec's "References" section.
        private static readonly string[] ExpectedSvgMockups =
        {
            // Features
            "doc/SQL-PROMPT/SQL-Prompt-Features/01_suggestion_popup.svg",
            "doc/SQL-PROMPT/SQL-Prompt-Features/02_tab_coloring.svg",
            "doc/SQL-PROMPT/SQL-Prompt-Features/03_code_analysis.svg",
            "doc/SQL-PROMPT/SQL-Prompt-Features/04_formatting_before_after.svg",
            "doc/SQL-PROMPT/SQL-Prompt-Features/05_icon_types.svg",
            "doc/SQL-PROMPT/SQL-Prompt-Features/06_ai_window.svg",
            "doc/SQL-PROMPT/SQL-Prompt-Features/07_ai_ghost_text.svg",
            "doc/SQL-PROMPT/SQL-Prompt-Features/08_column_picker_snippets.svg",
            // History
            "doc/SQL-PROMPT/SQL-Prompt-History/09_sql_history_window.svg",
            "doc/SQL-PROMPT/SQL-Prompt-History/10_sql_history_toolbar.svg",
            "doc/SQL-PROMPT/SQL-Prompt-History/11_sql_history_search.svg",
            "doc/SQL-PROMPT/SQL-Prompt-History/12_crash_recovery.svg",
            // Options
            "doc/SQL-PROMPT/SQL-Prompt-Option/13_options_dialog.svg",
            "doc/SQL-PROMPT/SQL-Prompt-Option/14_format_styles_editor.svg",
        };

        [Fact]
        public void AllReferenceMarkdownFilesExistAndAreNonEmpty()
        {
            var repoRoot = FindRepoRoot();
            var missing = new List<string>();
            var empty = new List<string>();

            foreach (var rel in ExpectedReferenceFiles)
            {
                var abs = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs)) { missing.Add(rel); continue; }
                if (new FileInfo(abs).Length == 0) empty.Add(rel);
            }

            if (missing.Count > 0)
            {
                _output.WriteLine($"Missing reference markdown files ({missing.Count}):");
                foreach (var m in missing) _output.WriteLine("  " + m);
            }
            if (empty.Count > 0)
            {
                _output.WriteLine($"Empty reference markdown files ({empty.Count}):");
                foreach (var e in empty) _output.WriteLine("  " + e);
            }

            Assert.True(missing.Count == 0,
                $"{missing.Count} expected SQL Prompt reference markdown file(s) missing. " +
                "The visual-parity spec depends on these as the canonical design contract.");
            Assert.True(empty.Count == 0,
                $"{empty.Count} expected SQL Prompt reference file(s) are empty.");
        }

        [Fact]
        public void AllSvgMockupsExist()
        {
            var repoRoot = FindRepoRoot();
            var missing = new List<string>();

            foreach (var rel in ExpectedSvgMockups)
            {
                var abs = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs)) missing.Add(rel);
            }

            if (missing.Count > 0)
            {
                _output.WriteLine($"Missing SVG mockups ({missing.Count}):");
                foreach (var m in missing) _output.WriteLine("  " + m);
            }

            Assert.True(missing.Count == 0,
                $"{missing.Count} expected SVG mockup(s) missing. " +
                "Side-by-side screenshot review (SC-003 / SC-004) requires each mockup as the baseline.");
        }

        // -----------------------------------------------------------------------

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate AKML-SQL.slnx walking up from " + AppContext.BaseDirectory);
        }
    }
}
