using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using AkmlSql.Shell.Shared.Editor.Completion;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// A disabled themed dropdown must look disabled, and committing a bracketed completion must
    /// not double a "[" the user typed.
    /// </summary>
    public class FormatStylesUiFixTests
    {
        // ── disabled ComboBox (Format Styles options switched off by another option) ──

        /// <summary>
        /// The themed template painted the same face, arrow and text whether or not the combo was
        /// enabled, so the JOIN / THEN / END / comma / AS dropdowns that another option switches off
        /// looked active and simply did nothing when clicked.
        /// </summary>
        [StaTheory]
        [InlineData("Light", false)]
        [InlineData("Dark", false)]
        [InlineData("Light", true)]
        [InlineData("Dark", true)]
        public void A_disabled_combo_looks_disabled(string theme, bool menu)
        {
            var pageTheme = theme == "Dark" ? PageTheme.Dark : PageTheme.Light;
            var combo = new ComboBox();
            combo.Items.Add("To FROM");
            combo.SelectedIndex = 0;
            if (menu) ComboBoxTheming.ApplyMenuStyle(combo, pageTheme);
            else ComboBoxTheming.Apply(combo, pageTheme);
            var window = new Window { Content = combo, Width = 300, Height = 120, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                window.Show();
                combo.ApplyTemplate();
                var content = (ContentPresenter)combo.Template.FindName("contentPresenter", combo);
                var toggle = (ToggleButton)combo.Template.FindName("toggleButton", combo);
                toggle.ApplyTemplate();
                var face = (Border)toggle.Template.FindName("Bd", toggle);
                var arrow = (Path)toggle.Template.FindName("Arrow", toggle);

                var enabledText = (Brush)content.GetValue(TextElement.ForegroundProperty);
                var enabledFace = face.Background;
                var enabledArrow = arrow.Fill;

                combo.IsEnabled = false;

                Assert.NotSame(enabledText, content.GetValue(TextElement.ForegroundProperty));
                Assert.Same(pageTheme.FgSecondary, content.GetValue(TextElement.ForegroundProperty));
                Assert.NotSame(enabledFace, face.Background);
                Assert.Same(pageTheme.InputReadOnly, face.Background);
                Assert.NotSame(enabledArrow, arrow.Fill);

                // Still readable: disabled is quieter, not invisible (WCAG 3:1 for inactive UI).
                var ratio = Contrast(((SolidColorBrush)face.Background).Color, pageTheme.FgSecondary.Color);
                Assert.True(ratio >= 3.0, $"{theme}: disabled text contrast {ratio:F2}:1");

                combo.IsEnabled = true;
                Assert.Same(enabledFace, face.Background);
            }
            finally
            {
                window.Close();
            }
        }

        private static double Contrast(Color a, Color b)
        {
            double L(Color c)
            {
                double Ch(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
                return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
            }
            var la = L(a); var lb = L(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        // ── bracketed completions in SSMS ──

        private static (string Text, int Caret) Commit(string textWithCaret, string insert)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Replace("|", string.Empty);
            var start = caret;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
            var (from, to) = CompletionController.BracketedRange(i => text[i], text.Length, insert, start, caret);
            var result = text.Substring(0, from) + insert + text.Substring(to);
            return (result, from + insert.Length);
        }

        [Theory]
        [InlineData("SELECT [Tot|] FROM t", "[Total Sales]", "SELECT [Total Sales] FROM t")]   // auto-closed "]"
        [InlineData("SELECT [Tot| FROM t", "[Total Sales]", "SELECT [Total Sales] FROM t")]    // no auto-close
        [InlineData("SELECT [Ord|]", "[Order]", "SELECT [Order]")]
        [InlineData("SELECT [|]", "[Order]", "SELECT [Order]")]
        [InlineData("SELECT [Total Sa|] FROM t", "[Total Sales]", "SELECT [Total Sales] FROM t")] // spaces inside the name
        [InlineData("SELECT [Übersicht-Ja|]", "[Übersicht-Jahr]", "SELECT [Übersicht-Jahr]")]
        [InlineData("SELECT Tot| FROM t", "[Total Sales]", "SELECT [Total Sales] FROM t")]     // nothing typed
        public void A_bracketed_insert_takes_in_the_brackets_already_typed(string before, string insert, string expected)
        {
            Assert.Equal(expected, Commit(before, insert).Text);
        }

        [Theory]
        [InlineData("SELECT [a].Tot|", "[Total]", "SELECT [a].[Total]")]           // an earlier, closed name
        [InlineData("SELECT '[x' + Tot|", "[Total]", "SELECT '[x' + [Total]")]      // a "[" inside a string
        [InlineData("SELECT [Ord|", "Orders", "SELECT [Orders")]                    // plain insert: untouched
        [InlineData("SELECT \"a[b\" + Tot|", "[Total]", "SELECT \"a[b\" + [Total]")]  // a "[" in a quoted name
        [InlineData("/* see [x */ SELECT Tot|", "[Total]", "/* see [x */ SELECT [Total]")] // a "[" in a comment
        [InlineData("SELECT [Order ID, Tot|", "[Total]", "SELECT [Order ID, [Total]")]      // an unclosed earlier name
        public void Earlier_brackets_are_left_alone(string before, string insert, string expected)
        {
            Assert.Equal(expected, Commit(before, insert).Text);
        }
    }
}
