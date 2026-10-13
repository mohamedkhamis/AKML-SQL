#nullable enable
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// A row of in-page tabs for an Options page (the 2026-10 AI assistance redesign, design B
    /// of <c>artifacts/ai-assistance-demo</c>): flat tab buttons with an accent underline, each
    /// owning one content <see cref="StackPanel"/>; only the selected panel is visible. The
    /// panels are appended to the page panel in order, so the host's search index, Restore
    /// Defaults and Load/Save see every row as before — a tab only changes visibility.
    /// <para>
    /// Reveal contract: <c>SettingsWindow</c> reveals a searched row (and the Command Palette's
    /// jump target) with <see cref="FrameworkElement.BringIntoView()"/>. A row on a collapsed
    /// tab raises <see cref="FrameworkElement.RequestBringIntoViewEvent"/> through its panel, so
    /// the panel's handler selects the tab and lays the tree out before the request bubbles on
    /// to the ScrollViewer, which then scrolls to the row's real slot.
    /// </para>
    /// Programmatic WPF, painted from <see cref="PageTheme"/> (CLAUDE.md WPF conventions).
    /// </summary>
    internal sealed class OptionsTabStrip
    {
        private readonly PageTheme _theme;
        private readonly StackPanel _buttons = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Border _strip;
        private readonly List<Button> _buttonList = new List<Button>();
        private readonly List<StackPanel> _panels = new List<StackPanel>();

        internal OptionsTabStrip(PageTheme theme)
        {
            _theme = theme;
            _strip = new Border
            {
                BorderBrush = theme.Sep,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 2, 0, 6),
                Child = _buttons,
            };
        }

        /// <summary>Index of the visible tab.</summary>
        internal int SelectedIndex { get; private set; }

        /// <summary>The content panels, in tab order (test seam).</summary>
        internal IReadOnlyList<StackPanel> Panels => _panels;

        /// <summary>The tab buttons, in tab order (test seam).</summary>
        internal IReadOnlyList<Button> Buttons => _buttonList;

        /// <summary>Adds a tab and returns its content panel (visible only for the first tab).</summary>
        internal StackPanel AddTab(string header)
        {
            var index = _panels.Count;

            var button = new Button
            {
                Content = header,
                FontSize = 13,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 2, 0),
                Background = _theme.Transparent,
                BorderBrush = _theme.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                Foreground = _theme.FgSecondary,
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Template = TabTemplate,
            };
            AutomationProperties.SetName(button, header + " tab");
            button.Click += (_, _) => Select(index);
            button.KeyDown += OnTabKeyDown;
            // Hover is handled here rather than by a template trigger: the selected/unselected
            // foreground is a local value, which outranks template triggers.
            button.MouseEnter += (_, _) => { if (index != SelectedIndex) button.Foreground = _theme.FgPrimary; };
            button.MouseLeave += (_, _) => { if (index != SelectedIndex) button.Foreground = _theme.FgSecondary; };
            _buttons.Children.Add(button);
            _buttonList.Add(button);

            var panel = new StackPanel { Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed };
            panel.AddHandler(FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((_, e) => OnRequestBringIntoView(index, e)));
            _panels.Add(panel);

            Paint();
            return panel;
        }

        /// <summary>Appends the strip and every content panel to the page panel, in tab order.</summary>
        internal void AddTo(StackPanel host)
        {
            host.Children.Add(_strip);
            foreach (var panel in _panels) host.Children.Add(panel);
        }

        /// <summary>Shows tab <paramref name="index"/> and hides the others.</summary>
        internal void Select(int index)
        {
            if (index < 0 || index >= _panels.Count) return;
            SelectedIndex = index;
            for (var i = 0; i < _panels.Count; i++)
                _panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            Paint();
        }

        private void Paint()
        {
            for (var i = 0; i < _buttonList.Count; i++)
            {
                var button = _buttonList[i];
                var selected = i == SelectedIndex;
                button.Foreground = selected ? _theme.FgPrimary : _theme.FgSecondary;
                button.BorderBrush = selected ? _theme.Selected : _theme.Transparent;
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                AutomationProperties.SetItemStatus(button, selected ? "selected" : string.Empty);
            }
        }

        private void OnTabKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Left && e.Key != Key.Right) return;
            var next = SelectedIndex + (e.Key == Key.Right ? 1 : -1);
            if (next < 0 || next >= _panels.Count) return;
            Select(next);
            _buttonList[next].Focus();
            e.Handled = true;
        }

        private void OnRequestBringIntoView(int index, RequestBringIntoViewEventArgs e)
        {
            if (index == SelectedIndex) return;   // visible already: let the ScrollViewer scroll
            Select(index);
            // The row had no layout slot while its tab was collapsed. Lay the tree out now, then
            // let the request keep bubbling to the ScrollViewer, which scrolls to the real slot.
            _panels[index].UpdateLayout();
        }

        // One sealed template: a Border painting the button's own Background / BorderBrush /
        // BorderThickness (the underline) around the content — no Aero chrome.
        private static readonly ControlTemplate TabTemplate = BuildTabTemplate();

        private static ControlTemplate BuildTabTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            template.Seal();
            return template;
        }
    }
}
