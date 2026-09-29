#nullable enable
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class TabsPage : IPageBuilder
    {
        public string Key     => "Tabs & UI";
        public string Display => "Tabs › Color";
        public string Title   => "Tabs & UI";
        public string Help    => "Configure environment-based tab coloring rules and a custom window title template. Restoring queries on start is on the SQL History page.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Tab Coloring");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable environment-based tab coloring",
                "Color tabs based on server name patterns (e.g. PROD=red, DEV=green)");
            ctx.RegisterSearch("Enable environment-based tab coloring", "Color tabs based on server name patterns", "Toggle", rowEnabled);

            var (rowGradient, chkGradient) = ctx.Rows.AddToggle(panel,
                "Use gradient colors",
                "Apply a vertical gradient to tab color bars (lighter at top, base color at bottom)");
            ctx.RegisterSearch("Use gradient colors", "Apply a vertical gradient to tab color bars", "Toggle", rowGradient);

            // Environment Rules editor — list + Add/Edit/Remove buttons.
            // The host wires the Click handlers (CRUD operations involve a custom
            // dialog editor that lives on SettingsWindow).
            panel.Children.Add(new TextBlock
            {
                Text = "Environment Rules",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(20, 16, 20, 4),
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Define server name patterns to match environments. Rules are evaluated top-down; first match wins.",
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 0, 20, 8),
            });

            var rulesList = new ListBox
            {
                Height = 120,
                Margin = new Thickness(20, 4, 20, 4),
                BorderThickness = new Thickness(1),
                FontSize = 13,
            };
            panel.Children.Add(rulesList);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 4, 20, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var btnAdd    = new Button { Content = "Add...",    Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            var btnEdit   = new Button { Content = "Edit...",   Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            var btnRemove = new Button { Content = "Remove",    Padding = new Thickness(12, 4, 12, 4) };
            buttonRow.Children.Add(btnAdd);
            buttonRow.Children.Add(btnEdit);
            buttonRow.Children.Add(btnRemove);
            panel.Children.Add(buttonRow);

            // Spec 040 (HIS-14, T145): session recovery, auto-save and restore on start moved to the
            // SQL History page.

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Window Title");

            var (rowTitle, txtTitle) = ctx.Rows.AddTextInput(panel,
                "Custom window title template",
                "Use {server}, {database}, and other placeholders");
            ctx.RegisterSearch("Custom window title template", "Use {server}, {database}, and other placeholders", "Text", rowTitle);

            return new TabsControls(chkEnabled, chkGradient, rulesList, btnAdd, btnEdit, btnRemove, txtTitle);
        }
    }

    internal sealed class TabsControls : IPageControls
    {
        private readonly CheckBox _coloringEnabled;
        private readonly CheckBox _gradientColors;
        public ListBox ColoringRulesList { get; }
        public Button AddRuleButton { get; }
        public Button EditRuleButton { get; }
        public Button RemoveRuleButton { get; }
        private readonly TextBox _customWindowTitle;

        public TabsControls(CheckBox coloring, CheckBox gradient, ListBox rulesList,
            Button btnAdd, Button btnEdit, Button btnRemove,
            TextBox title)
        {
            _coloringEnabled = coloring;
            _gradientColors = gradient;
            ColoringRulesList = rulesList;
            AddRuleButton = btnAdd;
            EditRuleButton = btnEdit;
            RemoveRuleButton = btnRemove;
            _customWindowTitle = title;
        }

        public void Load(AppSettings settings)
        {
            var t = settings.Tabs;
            _coloringEnabled.IsChecked = t.ColoringEnabled;
            _gradientColors.IsChecked = t.GradientColors;
            _customWindowTitle.Text = t.CustomWindowTitle ?? string.Empty;
            // Note: ColoringRulesList is populated by the host's
            // PopulateColoringRulesList — Tabs.ColoringRules CRUD lives there.
        }

        public void Save(AppSettings settings)
        {
            settings.Tabs.ColoringEnabled = _coloringEnabled.IsChecked == true;
            settings.Tabs.GradientColors = _gradientColors.IsChecked == true;
            settings.Tabs.CustomWindowTitle = _customWindowTitle.Text ?? string.Empty;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
