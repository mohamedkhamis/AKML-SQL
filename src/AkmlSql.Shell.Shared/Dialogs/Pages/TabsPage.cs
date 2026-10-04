#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using AkmlSql.Shell.Shared.Tabs;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Queries › Color (spec 040, OPT-08, FR-054) — SQL Prompt's tab colour page: <b>Enable tab
    /// coloring</b>, <b>Use gradient colors</b>, and a themed grid of rules (Server / group
    /// pattern · Database · Environment) with add, remove, ↑/↓ reordering, the wildcard hint and
    /// <b>Edit environments…</b>. The rules and environments are a working copy loaded and saved
    /// with the page like every other row; nothing is owned by the host window any more.
    /// </summary>
    internal sealed class TabsPage : IPageBuilder
    {
        public string Key     => "Tabs & UI";
        public string Display => "Queries › Color";
        public string Title   => "Color";
        public string HelpTopic => "topics/options#queries-color";
        public string Help    => "Color each query tab by the environment its server and database match. Rules are checked from the top and the first match wins; each environment has its own color. Restoring queries on start is on the History page.";

        private const string EnabledDescription = "Color query tabs by the environment of their server and database";
        private const string GradientDescription = "Shade tab color bars from lighter at the top to the environment color at the bottom";
        private const string TitleDescription = "Use {server}, {database}, and other placeholders";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Tab coloring");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel, "Enable tab coloring", EnabledDescription);
            ctx.RegisterSearch("Enable tab coloring", EnabledDescription, "Toggle", rowEnabled);

            // Spec 040 (OPT-05, research R5): the gradient and the grid are children of the master switch.
            var (rowGradient, chkGradient) = ctx.Rows.AddToggle(panel, "Use gradient colors", GradientDescription, chkEnabled);
            ctx.RegisterSearch("Use gradient colors", GradientDescription, "Toggle", rowGradient);

            var rules = new ColorRulesGrid(ctx.Theme, chkGradient);
            panel.Children.Add(rules.Root);
            rules.GateOn(chkEnabled);
            ctx.RegisterSearch(ColorRulesGrid.RulesLabel, ColorRulesGrid.RulesDescription, "List", rules.Root);
            ctx.RegisterSearch(ColorRulesGrid.EditEnvironmentsLabel, ColorRulesGrid.EditEnvironmentsDescription, "Button", rules.ButtonRow);

            // Spec 040 (HIS-14, T145): session recovery, auto-save and restore on start moved to the
            // History page.

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Window title");

            var (rowTitle, txtTitle) = ctx.Rows.AddTextInput(panel, "Custom window title template", TitleDescription);
            ctx.RegisterSearch("Custom window title template", TitleDescription, "Text", rowTitle);

            return new TabsControls(chkEnabled, chkGradient, rules, txtTitle);
        }
    }

    internal sealed class TabsControls : IPageControls
    {
        private readonly CheckBox _coloringEnabled;
        private readonly CheckBox _gradientColors;
        private readonly TextBox _customWindowTitle;

        /// <summary>The rules grid and its environments (internal for tests).</summary>
        internal ColorRulesGrid Rules { get; }

        public TabsControls(CheckBox coloring, CheckBox gradient, ColorRulesGrid rules, TextBox title)
        {
            _coloringEnabled = coloring;
            _gradientColors = gradient;
            Rules = rules;
            _customWindowTitle = title;
        }

        public void Load(AppSettings settings)
        {
            var t = settings.Tabs;
            _coloringEnabled.IsChecked = t.ColoringEnabled;
            _gradientColors.IsChecked = t.GradientColors;
            Rules.Load(t);
            _customWindowTitle.Text = t.CustomWindowTitle ?? string.Empty;
        }

        public void Save(AppSettings settings)
        {
            settings.Tabs.ColoringEnabled = _coloringEnabled.IsChecked == true;
            settings.Tabs.GradientColors = _gradientColors.IsChecked == true;
            Rules.Save(settings.Tabs);
            // Safety keys on the environment name: a renamed environment keeps its protection.
            if (settings.Safety?.EnvironmentSeverity != null)
                EnvironmentSafety.FollowRenames(settings.Safety.EnvironmentSeverity, Rules.Renames);
            settings.Tabs.CustomWindowTitle = _customWindowTitle.Text ?? string.Empty;
        }

        // Restore defaults brings back the default rules and the four default environments
        // (SettingsWindow.ResetConfirmationText says so).
        public void Reset(AppSettings defaults) => Load(defaults);
    }

    /// <summary>
    /// Spec 040 (OPT-08, FR-054, research R8) — the Color page's rule grid: a <see cref="ListView"/>
    /// with a <see cref="GridView"/> painted entirely from <see cref="PageTheme"/> brushes (the
    /// paired hover/selection triggers of <see cref="AiAgentListView.BuildItemStyle"/>; the stock
    /// Aero header and row chrome would stay light in Dark), edited in place:
    /// <list type="bullet">
    ///   <item><b>Server / group pattern</b> and <b>Database</b> — text, <c>*</c> wildcards and
    ///     comma lists. A server with a database matches that server AND database; <c>*</c> (or an
    ///     empty server) with a database is SQL Prompt's "this database on any server".</item>
    ///   <item><b>Environment</b> — a swatch and a dropdown of the environment names.</item>
    /// </list>
    /// ↑/↓ reorder through <see cref="ColoringRuleOrdering.Move"/>; the first match wins. A row
    /// that is not edited keeps its exact pattern, match target and database. On save every rule
    /// takes its Label and Color from its environment (Safety and the History badge key on Label).
    /// </summary>
    internal sealed class ColorRulesGrid
    {
        internal const string RulesLabel = "Tab color rules";
        internal const string RulesDescription = "Rules are checked from the top; the first one that matches a query's server and database picks its environment";
        internal const string EditEnvironmentsLabel = "Edit environments";
        internal const string EditEnvironmentsDescription = "Rename environments and change their colors";
        internal const string WildcardHint = "You can use wildcards (*).";

        // The three columns (190 + 130 + 175), the list border and room for its scroll bar.
        private const double GridWidth = 515;

        private readonly PageTheme _theme;
        private readonly CheckBox _gradient;
        // The Environment dropdowns' items. Mutated only while the list is detached (Render), so
        // no live dropdown ever sees its selected item disappear.
        private readonly ObservableCollection<string> _environmentNames = new ObservableCollection<string>();
        private readonly TextBlock _label;
        private readonly TextBlock _description;
        private readonly TextBlock _hint;
        private List<TabEnvironment> _environments = new List<TabEnvironment>();
        // Name an environment had when the page loaded → its name now, across every Edit
        // environments in this visit: Safety's severity follows these on save.
        private readonly Dictionary<string, string> _renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal ObservableCollection<ColorRuleRow> Rows { get; } = new ObservableCollection<ColorRuleRow>();
        internal IReadOnlyList<TabEnvironment> Environments => _environments;

        /// <summary>Each environment renamed since the page loaded: its name then → its name now.</summary>
        internal IReadOnlyDictionary<string, string> Renames => _renames;
        internal IReadOnlyList<string> EnvironmentNames => _environmentNames;

        internal StackPanel Root { get; }
        internal DockPanel ButtonRow { get; }
        internal ListView List { get; }
        internal Button AddButton { get; }
        internal Button RemoveButton { get; }
        internal Button MoveUpButton { get; }
        internal Button MoveDownButton { get; }
        internal Button EditEnvironmentsButton { get; }

        /// <summary>Test seam: replaces the Edit environments dialog (environments, rules in use,
        /// gradient on) → the edit, or null for Cancel.</summary>
        internal Func<IReadOnlyList<TabEnvironment>, IReadOnlyList<ColoringRule>, bool, EnvironmentEditResult?>? EditEnvironmentsOverride { get; set; }

        internal ColorRulesGrid(PageTheme theme, CheckBox gradient)
        {
            _theme = theme;
            _gradient = gradient;

            // Indented one step under "Enable tab coloring", like the RowFactory child rows; as wide
            // as the grid's columns (plus a scroll bar), so the buttons line up with its edges.
            Root = new StackPanel
            {
                Margin = new Thickness(20 + RowFactory.ChildIndent, 4, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = GridWidth,
            };

            _label = new TextBlock { Text = RulesLabel, Foreground = theme.FgPrimary, FontSize = 13 };
            Root.Children.Add(_label);
            _description = new TextBlock
            {
                Text = RulesDescription + ".",
                Foreground = theme.FgSecondary,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 6),
            };
            Root.Children.Add(_description);

            List = BuildList(theme);
            List.ItemsSource = Rows;
            List.SelectionChanged += (_, __) => UpdateButtons();
            Root.Children.Add(List);

            AddButton = MakeButton("+ Add server/database", "Add a rule");
            RemoveButton = MakeButton("Remove", "Remove the selected rule");
            MoveUpButton = MakeButton("↑", "Move the selected rule up");
            MoveDownButton = MakeButton("↓", "Move the selected rule down");
            MoveUpButton.ToolTip = "Move up — rules are checked from the top";
            MoveDownButton.ToolTip = "Move down";
            EditEnvironmentsButton = MakeButton("Edit environments…", "Edit environments");
            EditEnvironmentsButton.Margin = new Thickness(0);

            AddButton.Click += (_, __) => AddRule();
            RemoveButton.Click += (_, __) => RemoveSelected();
            MoveUpButton.Click += (_, __) => MoveSelected(-1);
            MoveDownButton.Click += (_, __) => MoveSelected(+1);
            EditEnvironmentsButton.Click += (_, __) => EditEnvironments();

            ButtonRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(EditEnvironmentsButton, Dock.Right);
            ButtonRow.Children.Add(EditEnvironmentsButton);
            foreach (var button in new[] { AddButton, RemoveButton, MoveUpButton, MoveDownButton })
            {
                DockPanel.SetDock(button, Dock.Left);
                ButtonRow.Children.Add(button);
            }
            Root.Children.Add(ButtonRow);

            _hint = new TextBlock
            {
                Text = WildcardHint,
                Foreground = theme.FgSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
            };
            Root.Children.Add(_hint);

            UpdateButtons();
        }

        // ─── Load / save ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows <paramref name="tabs"/>' rules (in evaluation order) and environments. Works on a
        /// copy: the migration (<see cref="TabEnvironmentMigration.Apply"/>, a no-op for a config
        /// that <c>ConfigManager.Load</c> returned) runs on it, so a fresh <see cref="AppSettings"/>
        /// (Restore defaults) shows the four default environments.
        /// </summary>
        internal void Load(TabSettings tabs)
        {
            _renames.Clear();
            var copy = new TabSettings
            {
                ColoringRules = (tabs.ColoringRules ?? new List<ColoringRule>()).Where(r => r != null).Select(ColorRuleRow.Copy).ToList(),
                Environments = (tabs.Environments ?? new List<TabEnvironment>()).Where(e => e != null).Select(e => e.Clone()).ToList(),
            };
            TabEnvironmentMigration.Apply(copy);

            var rules = ColoringRuleOrdering.InEvaluationOrder(copy.ColoringRules);
            ColoringRuleOrdering.Renumber(rules);
            Render(() =>
            {
                _environments = copy.Environments;
                Rows.Clear();
                foreach (var rule in rules)
                    Rows.Add(new ColorRuleRow(rule, SwatchFor));
            });
        }

        /// <summary>Writes the environments and the rules (Order 0..n-1, Label/Color from the environment).</summary>
        internal void Save(TabSettings tabs)
        {
            tabs.Environments = _environments.Select(e => e.Clone()).ToList();
            tabs.ColoringRules = BuildRules();
        }

        /// <summary>
        /// The rows as rules, in grid order: rows with neither a server nor a database are left out
        /// (they could never match), <c>Order</c> is renumbered, and Label/Color come from each
        /// rule's environment.
        /// </summary>
        internal List<ColoringRule> BuildRules()
        {
            var rules = Rows.Where(r => !r.IsBlank).Select(r => r.ToRule()).ToList();
            ColoringRuleOrdering.Renumber(rules);
            TabEnvironmentMigration.WriteLabelsFromEnvironments(rules, _environments);
            return rules;
        }

        // ─── Commands ───────────────────────────────────────────────────────────────────────────

        /// <summary>Adds an empty rule at the bottom on the first environment, selects it and puts the
        /// caret in its server box.</summary>
        internal ColorRuleRow AddRule()
        {
            var rule = new ColoringRule
            {
                MatchTarget = EnvironmentMatcher.MatchTargetServerName,
                Environment = _environments.FirstOrDefault()?.Name ?? string.Empty,
            };
            var row = new ColorRuleRow(rule, SwatchFor);
            row.Rule.Order = Rows.Count;
            Rows.Add(row);
            List.SelectedItem = row;
            List.ScrollIntoView(row);
            FocusServerBox(row);
            return row;
        }

        internal void RemoveSelected()
        {
            var index = List.SelectedIndex;
            if (index < 0 || index >= Rows.Count) return;
            Rows.RemoveAt(index);
            ColoringRuleOrdering.Renumber(Rows.Select(r => r.Rule).ToList());
            if (Rows.Count > 0)
                List.SelectedIndex = Math.Min(index, Rows.Count - 1);
            UpdateButtons();
        }

        /// <summary>Moves the selected rule up (−1) or down (+1) through
        /// <see cref="ColoringRuleOrdering.Move"/>, which also renumbers Order.</summary>
        internal void MoveSelected(int delta)
        {
            var index = List.SelectedIndex;
            if (index < 0) return;
            var target = ColoringRuleOrdering.Move(Rows.Select(r => r.Rule).ToList(), index, delta);
            if (target == index) return;
            Rows.Move(index, target);
            List.SelectedIndex = target;
            List.ScrollIntoView(Rows[target]);
            UpdateButtons();
        }

        /// <summary>Opens Edit environments with the working copy and applies what it returns.</summary>
        internal void EditEnvironments()
        {
            var inUse = Rows.Where(r => !r.IsBlank).Select(r => r.ToRule()).ToList();
            var gradientOn = _gradient.IsChecked == true;
            var result = EditEnvironmentsOverride != null
                ? EditEnvironmentsOverride(_environments, inUse, gradientOn)
                : EditEnvironmentsDialog.Edit(Window.GetWindow(Root), _environments, inUse, gradientOn);
            if (result != null)
                ApplyEnvironmentEdit(result);
        }

        /// <summary>Takes the dialog's environments, follows its renames in every rule and copies
        /// its gradient choice to the page's check box. A rule left without its environment (only
        /// an empty row can be: the dialog refuses to delete one that rules use) moves to the first.</summary>
        internal void ApplyEnvironmentEdit(EnvironmentEditResult result)
        {
            foreach (var rename in result.Renames)
            {
                var from = rename.Key.Trim();
                var to = (rename.Value ?? string.Empty).Trim();
                var earlier = _renames.FirstOrDefault(r => string.Equals(r.Value, from, StringComparison.OrdinalIgnoreCase));
                if (earlier.Key != null) _renames[earlier.Key] = to;
                else _renames[from] = to;
            }

            Render(() =>
            {
                foreach (var row in Rows)
                {
                    if (result.Renames.TryGetValue((row.Environment ?? string.Empty).Trim(), out var renamed))
                        row.Environment = renamed;
                }
                _environments = result.Environments.Select(e => e.Clone()).ToList();
                foreach (var row in Rows)
                {
                    if (TabEnvironmentMigration.Find(_environments, row.Environment) == null && _environments.Count > 0)
                        row.Environment = _environments[0].Name;
                }
            });
            _gradient.IsChecked = result.GradientColors;
        }

        // ─── Gating (spec 040, OPT-05) ──────────────────────────────────────────────────────────

        /// <summary>
        /// The grid as a child of <paramref name="parent"/> ("Enable tab coloring"): disabled with its
        /// labels in <see cref="PageTheme.TextDisabled"/> while the parent is off, the RowFactory
        /// child-row idiom. The "Takes effect when …" tooltip is shown only while it is off, so it
        /// never covers the cells being edited.
        /// </summary>
        internal void GateOn(CheckBox parent)
        {
            var tooltip = "Takes effect when \"" + RowFactory.ParentLabel(parent) + "\" is on";
            ToolTipService.SetShowOnDisabled(Root, true);
            var labels = new[] { _label, _description, _hint };
            var onBrushes = labels.Select(l => l.Foreground).ToArray();

            void Refresh()
            {
                var on = parent.IsChecked == true;
                Root.IsEnabled = on;
                Root.ToolTip = on ? null : tooltip;
                for (var i = 0; i < labels.Length; i++)
                    labels[i].Foreground = on ? onBrushes[i] : _theme.TextDisabled;
            }

            parent.Checked += (_, __) => Refresh();
            parent.Unchecked += (_, __) => Refresh();
            parent.Indeterminate += (_, __) => Refresh();
            Refresh();
        }

        // ─── Helpers ────────────────────────────────────────────────────────────────────────────

        private Brush SwatchFor(string environment)
            => HexBrush.Get(TabEnvironmentMigration.Find(_environments, environment)?.Color);

        /// <summary>
        /// Runs <paramref name="change"/> with the grid detached, then refreshes the dropdown items
        /// and re-attaches: the dropdowns are rebuilt against the new names instead of watching their
        /// selected name vanish (which would write an empty environment back into the row).
        /// </summary>
        private void Render(Action change)
        {
            var selected = List.SelectedIndex;
            List.ItemsSource = null;
            change();
            _environmentNames.Clear();
            foreach (var environment in _environments)
                _environmentNames.Add(environment.Name);
            foreach (var row in Rows)
                row.RefreshSwatch();
            List.ItemsSource = Rows;
            if (selected >= 0 && selected < Rows.Count) List.SelectedIndex = selected;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var index = List.SelectedIndex;
            RemoveButton.IsEnabled = index >= 0;
            MoveUpButton.IsEnabled = index > 0;
            MoveDownButton.IsEnabled = index >= 0 && index < Rows.Count - 1;
        }

        private void FocusServerBox(ColorRuleRow row)
        {
            // The row's container exists only after layout.
            List.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (List.ItemContainerGenerator.ContainerFromItem(row) is ListViewItem item &&
                    FindDescendant<TextBox>(item) is TextBox box)
                {
                    box.Focus();
                }
            }));
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private Button MakeButton(string content, string automationName)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 32,
                Height = 28,
                FontSize = 12,
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            ThemedButton.ApplySecondary(button, _theme);
            AutomationProperties.SetName(button, automationName);
            return button;
        }

        // ─── The themed grid ────────────────────────────────────────────────────────────────────

        private ListView BuildList(PageTheme theme)
        {
            var list = new ListView
            {
                Height = 170,
                Margin = new Thickness(0),
                BorderThickness = new Thickness(1),
                BorderBrush = theme.ComboBorder,
                Background = theme.Input,
                Foreground = theme.FgPrimary,
                FontSize = 13,
                SelectionMode = SelectionMode.Single,
            };
            // Tab walks through the cells (server → database → environment → next row) instead of
            // leaving the list after one stop.
            KeyboardNavigation.SetTabNavigation(list, KeyboardNavigationMode.Continue);
            AutomationProperties.SetName(list, RulesLabel);
            list.ItemContainerStyle = BuildRowStyle(theme);

            var view = new GridView
            {
                AllowsColumnReorder = false,
                ColumnHeaderContainerStyle = BuildHeaderStyle(theme),
            };
            view.Columns.Add(new GridViewColumn { Header = "Server / group pattern", Width = 190, CellTemplate = TextCell(nameof(ColorRuleRow.Server), "Server or group pattern") });
            view.Columns.Add(new GridViewColumn { Header = "Database", Width = 130, CellTemplate = TextCell(nameof(ColorRuleRow.Database), "Database (optional)") });
            view.Columns.Add(new GridViewColumn { Header = "Environment", Width = 175, CellTemplate = EnvironmentCell() });
            list.View = view;
            return list;
        }

        private DataTemplate TextCell(string property, string automationName)
        {
            var box = new FrameworkElementFactory(typeof(TextBox));
            box.SetBinding(TextBox.TextProperty, new Binding(property)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            box.SetValue(Control.BackgroundProperty, _theme.Input);
            box.SetValue(Control.ForegroundProperty, _theme.FgPrimary);
            box.SetValue(Control.BorderBrushProperty, _theme.ComboBorder);
            box.SetValue(Control.BorderThicknessProperty, new Thickness(1));
            box.SetValue(TextBox.CaretBrushProperty, _theme.Caret);
            box.SetValue(Control.PaddingProperty, new Thickness(4, 2, 4, 2));
            box.SetValue(FrameworkElement.HeightProperty, 26.0);
            box.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            box.SetValue(AutomationProperties.NameProperty, automationName);
            return new DataTemplate { VisualTree = box };
        }

        private DataTemplate EnvironmentCell()
        {
            var dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);

            var swatch = new FrameworkElementFactory(typeof(Border));
            swatch.SetValue(DockPanel.DockProperty, Dock.Left);
            swatch.SetValue(FrameworkElement.WidthProperty, 14.0);
            swatch.SetValue(FrameworkElement.HeightProperty, 14.0);
            swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            swatch.SetValue(Border.BorderBrushProperty, _theme.Border);
            swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            swatch.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            swatch.SetBinding(Border.BackgroundProperty, new Binding(nameof(ColorRuleRow.Swatch)));
            dock.AppendChild(swatch);

            // The Options look for the dropdown comes from ComboBoxTheming's cached, sealed pair;
            // a probe hands them over so the factory can share them with every row.
            var probe = new ComboBox();
            ComboBoxTheming.Apply(probe, _theme);

            var combo = new FrameworkElementFactory(typeof(ComboBox));
            combo.SetValue(Control.TemplateProperty, probe.Template);
            combo.SetValue(ItemsControl.ItemContainerStyleProperty, probe.ItemContainerStyle);
            combo.SetValue(Control.ForegroundProperty, probe.Foreground);
            combo.SetValue(FrameworkElement.HeightProperty, 26.0);
            combo.SetValue(Control.FontSizeProperty, 13.0);
            combo.SetValue(System.Windows.Controls.Primitives.Selector.IsSynchronizedWithCurrentItemProperty, false);
            combo.SetValue(AutomationProperties.NameProperty, "Environment");
            // Items first (a shared collection set before any DataContext arrives), then the
            // selection binding, so the selected name is always found among the items.
            combo.SetValue(ItemsControl.ItemsSourceProperty, _environmentNames);
            combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(ColorRuleRow.Environment)) { Mode = BindingMode.TwoWay });
            dock.AppendChild(combo);

            return new DataTemplate { VisualTree = dock };
        }

        private static Style BuildHeaderStyle(PageTheme theme)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, theme.Panel);
            border.SetValue(Border.BorderBrushProperty, theme.Sep);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var style = new Style(typeof(GridViewColumnHeader));
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(GridViewColumnHeader)) { VisualTree = border }));
            style.Setters.Add(new Setter(Control.ForegroundProperty, theme.FgSecondary));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            return style;
        }

        /// <summary>
        /// Rows on the <see cref="AiAgentListView.BuildItemStyle"/> pattern — every background with a
        /// foreground, hover before selected — over a Border + <see cref="GridViewRowPresenter"/>
        /// template (the stock one paints its own light selection wash). The selected row takes the
        /// accent TINT, not the saturated accent: its cells are input boxes in
        /// <see cref="PageTheme.FgPrimary"/>. Focusing a cell selects its row, so Remove and ↑/↓ act
        /// on the row being edited.
        /// </summary>
        private static Style BuildRowStyle(PageTheme theme)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.BorderBrushProperty, theme.Sep);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            var presenter = new FrameworkElementFactory(typeof(GridViewRowPresenter));
            presenter.SetValue(System.Windows.Controls.Primitives.GridViewRowPresenterBase.ColumnsProperty,
                new TemplateBindingExtension(GridView.ColumnCollectionProperty));
            presenter.SetValue(GridViewRowPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            border.AppendChild(presenter);

            var style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListViewItem)) { VisualTree = border }));
            style.Setters.Add(new Setter(Control.BackgroundProperty, theme.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, theme.FgPrimary));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 3, 0, 3)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.IsTabStopProperty, false));
            style.Setters.Add(new EventSetter(UIElement.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler((sender, _) => ((ListViewItem)sender).IsSelected = true)));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, theme.TreeHover));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, theme.FgPrimary));
            style.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, theme.SelectionTint));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, theme.FgPrimary));
            style.Triggers.Add(selectedTrigger);

            return style;
        }
    }

    /// <summary>
    /// One row of the Color grid. <see cref="Server"/> and <see cref="Database"/> are what the user
    /// sees and types; <see cref="ToRule"/> turns them back into a <see cref="ColoringRule"/> only
    /// when they were edited — an untouched row keeps its exact pattern, match target and
    /// database, so existing configs behave exactly as before.
    /// </summary>
    internal sealed class ColorRuleRow : INotifyPropertyChanged
    {
        private readonly string _initialServer;
        private readonly string _initialDatabase;
        private readonly Func<string, Brush> _swatchFor;
        private string _server;
        private string _database;
        private string _environment;

        internal ColorRuleRow(ColoringRule source, Func<string, Brush> swatchFor)
        {
            Rule = Copy(source);
            _swatchFor = swatchFor;
            (_server, _database) = DisplayTexts(source);
            _initialServer = _server;
            _initialDatabase = _database;
            _environment = source.Environment ?? string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>The working rule (its <c>Order</c> follows the row's position); <see cref="ToRule"/>
        /// returns an edited copy of it.</summary>
        internal ColoringRule Rule { get; }

        public string Server
        {
            get => _server;
            set
            {
                var text = value ?? string.Empty;
                if (text == _server) return;
                _server = text;
                Raise(nameof(Server));
            }
        }

        public string Database
        {
            get => _database;
            set
            {
                var text = value ?? string.Empty;
                if (text == _database) return;
                _database = text;
                Raise(nameof(Database));
            }
        }

        /// <summary>The environment's name. Null is ignored: a dropdown losing its items must never
        /// blank the rule's environment.</summary>
        public string Environment
        {
            get => _environment;
            set
            {
                if (value == null || value == _environment) return;
                _environment = value;
                Raise(nameof(Environment));
                Raise(nameof(Swatch));
            }
        }

        public Brush Swatch => _swatchFor(_environment);

        internal void RefreshSwatch() => Raise(nameof(Swatch));

        /// <summary>Neither a server nor a database: such a rule could never match.</summary>
        internal bool IsBlank => Server.Trim().Length == 0 && Database.Trim().Length == 0;

        /// <summary>
        /// The row as a rule. Edited rows: a server (other than <c>*</c>) makes a server rule — with a
        /// database it matches that server AND database; <c>*</c> or an empty server with a database
        /// makes a database rule ("this database on any server").
        /// </summary>
        internal ColoringRule ToRule()
        {
            var rule = Copy(Rule);
            rule.Environment = _environment;

            var server = Server.Trim();
            var database = Database.Trim();
            if (server == _initialServer.Trim() && database == _initialDatabase.Trim())
                return rule;

            if ((server.Length == 0 || server == "*") && database.Length > 0)
            {
                rule.MatchTarget = EnvironmentMatcher.MatchTargetDatabase;
                rule.Pattern = string.Empty;
                rule.DatabaseName = database;
            }
            else
            {
                rule.MatchTarget = EnvironmentMatcher.MatchTargetServerName;
                rule.Pattern = server;
                rule.DatabaseName = database;
            }
            return rule;
        }

        /// <summary>What the grid shows for a rule: a database rule reads <c>*</c> · its database
        /// (its Pattern when the older configs left DatabaseName empty).</summary>
        internal static (string Server, string Database) DisplayTexts(ColoringRule rule)
        {
            if (string.Equals(rule.MatchTarget, EnvironmentMatcher.MatchTargetDatabase, StringComparison.OrdinalIgnoreCase))
            {
                var database = string.IsNullOrEmpty(rule.DatabaseName) ? rule.Pattern : rule.DatabaseName;
                return ("*", database ?? string.Empty);
            }
            return (rule.Pattern ?? string.Empty, rule.DatabaseName ?? string.Empty);
        }

        internal static ColoringRule Copy(ColoringRule rule) => new ColoringRule
        {
            Order = rule.Order,
            Pattern = rule.Pattern ?? string.Empty,
            MatchTarget = rule.MatchTarget ?? EnvironmentMatcher.MatchTargetServerName,
            DatabaseName = rule.DatabaseName ?? string.Empty,
            Color = rule.Color ?? string.Empty,
            Label = rule.Label ?? string.Empty,
            Environment = rule.Environment ?? string.Empty,
        };

        private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
