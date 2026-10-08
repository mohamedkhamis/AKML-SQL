#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T176, X-04, FR-063) — every icon-only control in the SQL History toolbar and row,
    /// and the style list's ⋮, has a name a screen reader can announce. "Icon-only" means the
    /// control shows a glyph or a drawing rather than words. Built headlessly (never shown).
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class HistoryAccessibilityTests : AppDataIsolatedTest
    {
        public HistoryAccessibilityTests() : base("akmlsql-history-a11y-test-") { }

        private static HistoryToolWindowControl BuildHistory()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.HistorySearch, new HistorySearchResponse { Success = true, Entries = Array.Empty<HistoryEntryDto>() });
            var vm = new HistoryViewModel(fake)
            {
                SettingsProvider = () => new AppSettings(),
                Scheduler = (_, _) => new NoOp(),
                FindOpenDocument = _ => null,
            };
            return new HistoryToolWindowControl(vm);
        }

        private sealed class NoOp : IDisposable { public void Dispose() { } }

        /// <summary>True when the control's visible content has no words: a glyph (★ ⋯ ? ✕), a drawing, or nothing.</summary>
        private static bool IsIconOnly(ContentControl control)
        {
            string? text = control.Content switch
            {
                string s => s,
                TextBlock t => t.Text,
                Path _ => null,
                Panel p => string.Concat(Descendants(p).OfType<TextBlock>().Select(t => t.Text)),
                null => null,
                _ => control.Content.ToString(),
            };
            return text == null || !text.Any(char.IsLetter);
        }

        [StaFact]
        public void Every_icon_only_toolbar_control_has_a_name()
        {
            var control = BuildHistory();
            var buttons = Descendants(control).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(IsIconOnly).ToList();
            Assert.NotEmpty(buttons);
            Assert.All(buttons, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b)),
                $"an icon-only {b.GetType().Name} ('{b.Content}') has no AutomationProperties.Name"));
        }

        [StaFact]
        public void The_row_star_and_menu_have_names()
        {
            var control = BuildHistory();
            var template = control.QueryItemTemplate!;
            template.Seal();
            var row = (DependencyObject)template.LoadContent();

            var buttons = Descendants(row).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(IsIconOnly).ToList();
            Assert.Equal(2, buttons.Count);   // the star and the ⋯ menu
            Assert.All(buttons, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
        }

        [StaFact]
        public async Task The_style_list_menu_glyph_has_a_name()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            try
            {
                var list = (ListBox)typeof(AkmlSql.Shell.Shared.Formatting.FormatStylesEditorWindow)
                    .GetField("_styleList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var template = list.ItemTemplate;
                template.Seal();
                var row = (DependencyObject)template.LoadContent();

                var glyph = Descendants(row).OfType<TextBlock>().Single(t => t.Text == "⋮");
                Assert.Equal("Style actions", AutomationProperties.GetName(glyph));
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                    stack.Push(child);
            }
        }
    }
}
