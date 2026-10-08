#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The 2026-10 Format Styles redesign: the preview beside the options (under them when the
    /// window is narrow), a style list that folds into a rail, the SELECT examples as a preview
    /// source, and "what each value does" — one card per value of the focused option, formatted
    /// by the engine with the working style, with "Use this" to pick it.
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormatStylesRedesignTests : AppDataIsolatedTest
    {
        public FormatStylesRedesignTests() : base("akmlsql-redesign-test-") { }

        private const string ParenthesisStyle = "sqlPrompt.parentheses.parenthesisStyle";
        private const string InFirstValue = "sqlPrompt.operators.in.placeFirstValueOnNewLine";

        private static FormatStylesSchemaModel.Model Model() => FormatStylesSchemaModel.Parse(FormatStylesRowLayoutTests.SchemaJson());

        private static FormatSettingNode Setting(FormatStylesSchemaModel.Model model, string id) =>
            model.FlatGroups.SelectMany(g => g.Settings).Single(s => s.Id == id);

        private static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

        private static object? Call(object target, string name, params object?[] args) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

        // ── the schema and the plan (no WPF) ─────────────────────────────────────────────────

        [Fact]
        public void Every_option_has_an_example_whose_sql_resolves()
        {
            var model = Model();
            Assert.Equal(9, model.SelectExamples.Count);
            Assert.All(model.SelectExamples, q => Assert.Contains("SELECT", q.Sql, StringComparison.OrdinalIgnoreCase));
            foreach (var group in model.FlatGroups)
            {
                foreach (var setting in group.Settings)
                {
                    Assert.True(setting.Example != null, $"{setting.Id} has no example");
                    Assert.True(setting.Example!.Values.Count >= 2, setting.Id);
                    foreach (var value in setting.Example.Values)
                        Assert.False(string.IsNullOrWhiteSpace(model.SqlFor(setting.Example.TargetFor(value).QueryId, group)), $"{setting.Id}={value}");
                }
            }
        }

        [Fact]
        public void A_plan_formats_each_request_once_with_the_default_as_every_cards_baseline()
        {
            var model = Model();
            var plan = FormatStylesExamples.For(model, Setting(model, ParenthesisStyle))!;

            Assert.Equal(9, plan.Cards.Count);                 // SQL Prompt's nine parenthesis styles
            Assert.Equal(9, plan.Requests.Count);              // the default's output doubles as the baseline
            var defaultCard = plan.Cards.Single(c => c.IsDefault);
            Assert.Equal(defaultCard.Output, defaultCard.Baseline);
            Assert.All(plan.Cards, c => Assert.Equal(defaultCard.Output, c.Baseline));
            Assert.All(plan.Requests, r => Assert.Contains(r.Settings, s => s.Key == ParenthesisStyle));
            Assert.Contains("SELECT", plan.Requests[0].Sql);
        }

        [Fact]
        public void A_value_that_needs_its_own_query_gets_it_with_its_own_baseline()
        {
            var model = Model();
            var plan = FormatStylesExamples.For(model, Setting(model, InFirstValue))!;

            var own = plan.Cards.Where(c => c.OwnQueryTitle != null).ToList();
            Assert.NotEmpty(own);
            var main = plan.Cards.Single(c => c.IsDefault);
            Assert.All(own, c =>
            {
                Assert.NotEqual(main.Baseline, c.Baseline);    // compared with the default on the same query
                Assert.NotEqual(plan.Requests[main.Output].Sql, plan.Requests[c.Output].Sql);
            });
        }

        /// <summary>
        /// A value that needs another setting on the SAME query is not "shown on another query";
        /// its card names the extra setting instead (review finding: the note named the main query).
        /// </summary>
        [Fact]
        public void A_value_that_needs_an_extra_setting_says_which()
        {
            var model = Model();
            var plan = FormatStylesExamples.For(model, Setting(model, "sqlPrompt.joinStatements.join.keywordAlignment"))!;
            var card = plan.Cards.Single(c => Equals(c.Value, "rightAlignedToFrom"));
            Assert.Null(card.OwnQueryTitle);
            Assert.Contains(card.OwnWith, w => w.Key == "sqlPrompt.dml.clauses.clauseAlignment" && Equals(w.Value, "rightAligned"));
            Assert.All(plan.Cards.Where(c => !ReferenceEquals(c, card)), c => Assert.Empty(c.OwnWith));
        }

        [Fact]
        public void A_spaces_per_tab_card_renders_at_its_own_width()
        {
            const string tab = FormatStylesEditorViewModel.SqlPromptTabSizeId;
            var model = Model();
            var plan = FormatStylesExamples.For(model, Setting(model, tab))!;
            var widths = plan.Cards.Select(c => FormatStylesExamples.TabSizeOf(plan.Requests[c.Output].Settings, tab, working: 3)).ToList();
            Assert.Equal(plan.Cards.Select(c => Convert.ToInt32(c.Value)), widths);
            // Any other option's cards use the working width.
            var other = FormatStylesExamples.For(model, Setting(model, ParenthesisStyle))!;
            Assert.All(other.Cards, c => Assert.Equal(3, FormatStylesExamples.TabSizeOf(other.Requests[c.Output].Settings, tab, working: 3)));
        }

        [Fact]
        public void Changed_lines_mark_an_insertion_not_every_line_below_it()
        {
            var before = "SELECT a,\n       b\nFROM t\nWHERE x = 1;";
            var after = "SELECT\n    a,\n    b\nFROM t\nWHERE x = 1;";
            Assert.Equal(new[] { 0, 1, 2 }, FormatStylesExamples.ChangedLines(before, after));
            Assert.Equal(new[] { 2 }, FormatStylesExamples.ChangedLines("a\nb\nc", "a\nb\nNEW\nc"));
            Assert.Empty(FormatStylesExamples.ChangedLines("same", "same"));
            Assert.Empty(FormatStylesExamples.ChangedLines(null, "x"));
        }

        // ── the view model ───────────────────────────────────────────────────────────────────

        [StaFact]
        public async Task Option_examples_never_touch_the_live_preview()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var fake = (FakeRpcClientAccessor)typeof(FormatStylesEditorViewModel)
                .GetField("_rpc", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            fake.Respond<FormatPreviewRequest>(MessageTypes.FormatPreview, r => new FormatPreviewResponse { FormattedText = r.SessionId + ":" + r.ProfileJson });

            var settings = new[] { new KeyValuePair<string, object?>(ParenthesisStyle, "expandedSplit") };
            var outputs = await vm.FormatExamplesAsync(new[] { ("SELECT 1", (IReadOnlyList<KeyValuePair<string, object?>>)settings) }, CancellationToken.None);
            await Task.Delay(300); // any live preview still queued from loading lands

            Assert.NotNull(outputs);
            Assert.StartsWith(FormatStylesEditorViewModel.ExampleSessionId + ":", outputs![0]);
            Assert.Contains("expandedSplit", outputs[0]);
            Assert.DoesNotContain(FormatStylesEditorViewModel.ExampleSessionId, vm.PreviewText ?? string.Empty);
            Assert.NotEqual("expandedSplit", vm.GetWorkingValue(ParenthesisStyle)); // the working style is untouched
        }

        // ── the window ───────────────────────────────────────────────────────────────────────

        [StaFact]
        public async Task The_preview_sits_beside_the_options_and_goes_under_them_when_narrow()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            try
            {
                var root = FormatStylesRowLayoutTests.LayoutRoot(window);
                var page = Model().FlatGroups.First(g => g.Settings.Count > 0);
                var form = Field<Border>(window, "_formCard");
                var preview = Field<Border>(window, "_previewCard");

                FormatStylesRowLayoutTests.ShowPage(window, root, page, FormatStylesRowLayoutTests.DefaultClientSize);
                Assert.Equal(Grid.GetRow(form), Grid.GetRow(preview));
                Assert.True(Grid.GetColumn(preview) > Grid.GetColumn(form));
                Assert.True(preview.ActualHeight > 500, $"preview is {preview.ActualHeight:F0}px tall at the default size");

                FormatStylesRowLayoutTests.ShowPage(window, root, page, FormatStylesRowLayoutTests.MinimumClientSize);
                Assert.Equal(Grid.GetColumn(form), Grid.GetColumn(preview));
                Assert.True(Grid.GetRow(preview) > Grid.GetRow(form));
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        /// <summary>
        /// Beside the preview the form is narrower: at the default size every drop-down still shows
        /// its whole value (seen in SSMS: "Expanded: to stateme…" before the label column shrank).
        /// </summary>
        [StaFact]
        public async Task Drop_downs_show_their_whole_value_at_the_default_size()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            try
            {
                var root = FormatStylesRowLayoutTests.LayoutRoot(window);
                var clipped = new List<string>();
                foreach (var page in Model().FlatGroups.Where(g => g.Settings.Count > 0))
                {
                    FormatStylesRowLayoutTests.ShowPage(window, root, page);
                    // Every choice, not just the selected one: any of them can be picked.
                    foreach (var combo in FormatStylesRowLayoutTests.Descendants<ComboBox>(FormatStylesRowLayoutTests.Host(window)))
                    foreach (var text in combo.Items.Cast<object>().Select(i => i?.ToString() ?? string.Empty))
                    {
                        var probe = new TextBlock { Text = text, FontFamily = combo.FontFamily, FontSize = combo.FontSize };
                        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        if (probe.DesiredSize.Width + 26 > combo.ActualWidth)   // + the arrow and padding
                            clipped.Add($"{page.DisplayName}: '{text}' needs {probe.DesiredSize.Width + 26:F0}px, has {combo.ActualWidth:F0}px");
                    }
                }
                Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaFact]
        public async Task The_style_list_folds_into_a_rail_and_comes_back()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            try
            {
                var card = Field<Border>(window, "_stylesCard");
                var rail = Field<Border>(window, "_stylesRail");
                var column = Field<ColumnDefinition>(window, "_stylesColumn");

                Call(window, "SetStylesCollapsed", true, false);
                Assert.Equal(Visibility.Collapsed, card.Visibility);
                Assert.Equal(Visibility.Visible, rail.Visibility);
                Assert.True(column.Width.IsAuto);
                Assert.NotNull(Field<ListBox>(window, "_styleList")); // still there for its keys and tests

                Call(window, "SetStylesCollapsed", false, false);
                Assert.Equal(Visibility.Visible, card.Visibility);
                Assert.Equal(Visibility.Collapsed, rail.Visibility);
                Assert.True(column.Width.IsAbsolute && column.Width.Value >= 160);
            }
            finally
            {
                Call(window, "SetStylesCollapsed", false, false); // the choice lasts for the session: leave it open
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaFact]
        public async Task The_preview_offers_the_select_examples()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            try
            {
                Call(window, "PopulateSourceCombo");
                var combo = Field<ComboBox>(window, "_sourceCombo");
                var items = combo.Items.OfType<ComboBoxItem>().ToList();
                Assert.Equal("This page's sample", items[0].Content);
                Assert.Equal(FormatPreviewSource.PageSample, vm.PreviewSourceMode);
                var selects = items.Where(i => ((string)i.Content).StartsWith("SELECT: ", StringComparison.Ordinal)).ToList();
                Assert.Equal(9, selects.Count);

                combo.SelectedItem = selects[1];
                Assert.Equal(FormatPreviewSource.SelectExample, vm.PreviewSourceMode);
                Assert.Equal(Model().SelectExamples[1].Sql, vm.SelectExampleSql);
                Assert.False(Field<CheckBox>(window, "_editSampleToggle").IsEnabled);   // only "My sample" is editable

                combo.SelectedItem = items.Single(i => Equals(i.Content, "My sample"));
                Assert.Equal(FormatPreviewSource.Sample, vm.PreviewSourceMode);
                Assert.True(Field<CheckBox>(window, "_editSampleToggle").IsEnabled);
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        /// <summary>
        /// The window is built before the engine's schema arrives, when only "My sample" can be
        /// picked; once the SQL Prompt schema loads, the preview must open on this page's sample —
        /// the first fill is not the user's choice. (Seen in SSMS: it stayed on "My sample".)
        /// </summary>
        [StaFact]
        public async Task The_preview_opens_on_the_page_sample_once_the_schema_loads()
        {
            var vm = FormatStylesRowLayoutTests.NewViewModel(FormatStylesRowLayoutTests.SchemaJson());
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            try
            {
                var combo = Field<ComboBox>(window, "_sourceCombo");
                Assert.Equal("My sample", ((ComboBoxItem)combo.SelectedItem).Content);

                await vm.LoadAsync();
                Call(window, "PopulateSourceCombo");
                Assert.Equal("This page's sample", ((ComboBoxItem)combo.SelectedItem).Content);
                Assert.Equal(FormatPreviewSource.PageSample, vm.PreviewSourceMode);

                // A source the user picks does stick.
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Content, "My sample"));
                Call(window, "PopulateSourceCombo");
                Assert.Equal("My sample", ((ComboBoxItem)combo.SelectedItem).Content);
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        [StaFact]
        public async Task Each_value_gets_a_card_and_use_this_sets_it()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var fake = (FakeRpcClientAccessor)typeof(FormatStylesEditorViewModel)
                .GetField("_rpc", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            // A different text per value, so every card differs from the default's.
            fake.Respond<FormatPreviewRequest>(MessageTypes.FormatPreview, r => new FormatPreviewResponse { FormattedText = "SELECT 1\n" + r.ProfileJson });
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            try
            {
                var model = Model();
                var page = model.FlatGroups.Single(g => g.Settings.Any(s => s.Id == ParenthesisStyle));
                var root = FormatStylesRowLayoutTests.LayoutRoot(window);
                FormatStylesRowLayoutTests.ShowPage(window, root, page);
                Call(window, "FocusOption", ParenthesisStyle);
                await (Task)Call(window, "RefreshExamplesAsync")!;

                var host = Field<StackPanel>(window, "_examplesHost");
                var cards = host.Children.OfType<Border>().ToList();
                Assert.Equal(9, cards.Count);
                Assert.Contains(fake.Requests, r => r.MessageType == MessageTypes.FormatPreview
                                                    && ((FormatPreviewRequest)r.Payload!).SessionId == FormatStylesEditorViewModel.ExampleSessionId);

                // The focused row carries the accent bar.
                var row = FormatStylesRowLayoutTests.Host(window).Children.OfType<Border>().Single(b => Equals(b.Tag, ParenthesisStyle));
                Assert.NotEqual(System.Windows.Media.Brushes.Transparent, row.BorderBrush);

                // Changed lines are marked with bars, not a fill (contrast, High Contrast).
                var views = cards.Select(c => FormatStylesRowLayoutTests.Descendants<AkmlSql.Shell.Shared.Ui.SqlPreview.SqlPreviewView>(c).Single()).ToList();
                Assert.All(views, v => Assert.Empty(v.HighlightLines));
                Assert.All(views.Where((v, i) => !Equals(cards[i].Tag, "compactSimple")), v => Assert.NotEmpty(v.MarkedLines!));

                var split = cards.Single(c => Equals(c.Tag, "expandedSplit"));
                var use = FormatStylesRowLayoutTests.Descendants<Button>(split).Single(b => Equals(b.Content, "Use this"));
                use.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal("expandedSplit", vm.GetWorkingValue(ParenthesisStyle));
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        /// <summary>
        /// A read-only team style disables the options form; its example cards must not be a way
        /// around that (review finding: "Use this" made the style dirty and unsaveable).
        /// </summary>
        [StaFact]
        public async Task Use_this_is_off_for_a_read_only_team_style()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var fake = (FakeRpcClientAccessor)typeof(FormatStylesEditorViewModel)
                .GetField("_rpc", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            fake.Respond<FormatPreviewRequest>(MessageTypes.FormatPreview, r => new FormatPreviewResponse { FormattedText = "SELECT 1\n" + r.ProfileJson });
            typeof(FormatStylesEditorViewModel).GetProperty(nameof(FormatStylesEditorViewModel.IsSelectedReadOnly))!
                .GetSetMethod(nonPublic: true)!.Invoke(vm, new object[] { true });
            var window = FormatStylesRowLayoutTests.NewWindow(vm);
            try
            {
                var model = Model();
                var page = model.FlatGroups.Single(g => g.Settings.Any(s => s.Id == ParenthesisStyle));
                FormatStylesRowLayoutTests.ShowPage(window, FormatStylesRowLayoutTests.LayoutRoot(window), page);
                Call(window, "FocusOption", ParenthesisStyle);
                await (Task)Call(window, "RefreshExamplesAsync")!;

                var buttons = FormatStylesRowLayoutTests.Descendants<Button>(Field<StackPanel>(window, "_examplesHost"))
                    .Where(b => Equals(b.Content, "Use this")).ToList();
                Assert.NotEmpty(buttons);
                Assert.All(buttons, b => Assert.False(b.IsEnabled));

                var before = vm.GetWorkingValue(ParenthesisStyle);
                Call(window, "ApplyExampleValue", Setting(model, ParenthesisStyle), "expandedSplit");
                Assert.Equal(before, vm.GetWorkingValue(ParenthesisStyle));
                Assert.False(vm.IsDirty);
            }
            finally
            {
                FormatStylesRowLayoutTests.CloseWithoutPrompt(window);
            }
        }

        /// <summary>A failed example request (the engine refused the format) shows no card rather than raw SQL.</summary>
        [StaFact]
        public async Task A_value_the_engine_could_not_format_gets_no_card()
        {
            var vm = await FormatStylesRowLayoutTests.LoadedAsync(FormatStylesRowLayoutTests.SchemaJson());
            var fake = (FakeRpcClientAccessor)typeof(FormatStylesEditorViewModel)
                .GetField("_rpc", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            fake.Respond<FormatPreviewRequest>(MessageTypes.FormatPreview, r => r.ProfileJson!.Contains("expandedSplit")
                ? new FormatPreviewResponse { FormattedText = r.SampleText, ValidationError = "The layout changed the meaning." }
                : new FormatPreviewResponse { FormattedText = "SELECT 1\n" + r.ProfileJson });

            var settings = new[] { new KeyValuePair<string, object?>(ParenthesisStyle, "expandedSplit") };
            var outputs = await vm.FormatExamplesAsync(new[] { ("SELECT 1", (IReadOnlyList<KeyValuePair<string, object?>>)settings) }, CancellationToken.None);
            Assert.NotNull(outputs);
            Assert.Null(outputs![0]);
        }
    }
}
