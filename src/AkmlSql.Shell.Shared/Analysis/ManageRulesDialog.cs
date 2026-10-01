#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Spec 030 T053 (FR-026) — Manage Code Analysis Rules; spec 040 T168 (OPT-09, FR-055) — ported
    /// from WinForms to a themed WPF window, "AKML SQL – Code analysis rules".
    ///
    /// Every analysis rule (loaded from the engine via <c>ListAnalysisRules</c>) is listed under an
    /// expandable header for its category. Each row has an Enabled check box, the rule id and name,
    /// a Severity combo and a ✓ when the rule has an automatic fix. The pane on the right describes
    /// the selected rule, the line below the list shows the settings file the overrides are saved to,
    /// and rules switched off for this session only are listed in a strip with <b>Restore</b>.
    ///
    /// The owning command (<see cref="ManageRulesCommand"/>) reads <see cref="GetOverrides"/> when
    /// <c>ShowDialog()</c> returns true, persists the deviations to
    /// <c>config.json codeAnalysis.ruleOverrides</c>, applies <see cref="RestoreSessionSuppressions"/>
    /// and notifies the engine. Inputs and outputs are the same as the WinForms dialog's.
    /// </summary>
    internal sealed class ManageRulesDialog : ThemeAwareWindow
    {
        internal static readonly string WindowTitle = WindowTitles.For("Code analysis rules");

        // DiagnosticSeverity: Hint=0, Information=1, Warning=2, Error=3.
        internal static readonly string[] SeverityLabels = { "Hint", "Information", "Warning", "Error" };

        // Column widths shared by the header row and every rule row (Name takes the rest).
        private const double ColEnabledWidth  = 64;
        private const double ColRuleWidth     = 76;
        private const double ColSeverityWidth = 132;
        private const double ColFixWidth      = 40;

        // Declared before GroupTemplate: static initialisers run in textual order and the
        // template's chevron trigger uses this transform.
        private static readonly RotateTransform ExpandedArrow = Freeze(new RotateTransform(90));
        private static readonly ControlTemplate GroupTemplate = BuildGroupTemplate();

        private readonly IReadOnlyList<AnalysisRuleInfoDto> _rules;
        private readonly HashSet<string> _sessionSuppressed;
        private readonly List<RuleGroup> _groups = new List<RuleGroup>();
        private readonly List<RuleRow> _rows = new List<RuleRow>();

        private TextBlock _detailTitle = null!;
        private TextBlock _detailMeta = null!;
        private TextBlock _detailText = null!;
        private TextBox _settingsFileBox = null!;
        private TextBlock? _sessionLabel;
        private RuleRow? _selected;

        /// <summary>
        /// True when the user asked to lift the session-only suppressions. The owning command
        /// applies it on Save (the same gesture that commits the rows), so Cancel backs out of
        /// this too.
        /// </summary>
        public bool RestoreSessionSuppressions { get; private set; }

        /// <summary>
        /// The session-suppressed rule ids that Save will restore — empty until <b>Restore</b> is
        /// clicked.
        /// </summary>
        public IReadOnlyList<string> RestoredSessionRules => RestoreSessionSuppressions
            ? _sessionSuppressed.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList()
            : (IReadOnlyList<string>)Array.Empty<string>();

        /// <param name="rules">The catalog, from the engine's ListAnalysisRules.</param>
        /// <param name="sessionSuppressedRules">
        /// Rules switched off for this session only (engine memory, nothing on disk). They are
        /// listed so the scope is not a one-way door — without somewhere to see and undo it, a
        /// session suppression would be invisible and irreversible until SSMS restarts.
        /// </param>
        public ManageRulesDialog(
            IReadOnlyList<AnalysisRuleInfoDto>? rules,
            IReadOnlyList<string>? sessionSuppressedRules = null)
        {
            _rules = rules ?? Array.Empty<AnalysisRuleInfoDto>();
            _sessionSuppressed = new HashSet<string>(
                sessionSuppressedRules ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            Title = WindowTitle;
            Width = 980;
            Height = 620;
            MinWidth = 760;
            MinHeight = 460;
            ShowInTaskbar = false;
            FontFamily = Typography.UiFont;
            FontSize = Typography.Body;

            Content = BuildUi();
        }

        // ─── Test seams ──────────────────────────────────────────────────────

        /// <summary>The category groups, in display order.</summary>
        internal IReadOnlyList<RuleGroup> Groups => _groups;

        /// <summary>Every rule row, in display order (group by group).</summary>
        internal IReadOnlyList<RuleRow> Rows => _rows;

        internal Button SaveButton { get; private set; } = null!;

        internal Button CancelButton { get; private set; } = null!;

        /// <summary>The session strip's Restore button; null when nothing is session-suppressed.</summary>
        internal Button? SessionRestoreButton { get; private set; }

        /// <summary>The path shown after "Settings file:".</summary>
        internal string SettingsFileText => _settingsFileBox.Text;

        /// <summary>The description pane's heading and body.</summary>
        internal string DetailTitleText => _detailTitle.Text;

        internal string DetailText => _detailText.Text;

        internal RuleRow? SelectedRow => _selected;

        // ─── Owner ───────────────────────────────────────────────────────────

        /// <summary>
        /// Parents the window before it is shown: to <paramref name="owner"/> when it is opened from
        /// another AKML window (Options), otherwise to the SSMS main window through the DTE HWND so it
        /// opens centred in front of SSMS. Silent no-op when DTE is unreachable.
        /// </summary>
        internal void AttachOwner(Window? owner)
        {
            if (owner != null)
            {
                Owner = owner;
                return;
            }

            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.MainWindow != null)
                    new WindowInteropHelper(this).Owner = (IntPtr)dte.MainWindow.HWnd;
            }
            catch
            {
                // Not critical — the base class retries on Loaded, and an unparented window still works.
            }
        }

        // ─── Layout ──────────────────────────────────────────────────────────

        private UIElement BuildUi()
        {
            var root = new DockPanel { LastChildFill = true };

            var intro = new TextBlock
            {
                Text = $"{_rules.Count} rules. Turn rules on or off and choose their severity, then Save. " +
                       "A project's .casettings file still overrides these for its folder.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Spacing.Lg, Spacing.Lg, Spacing.Lg, Spacing.Sm),
            };
            intro.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            // Docked from the bottom up: footer, then the settings-file line, then the session strip.
            var footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var settingsFile = BuildSettingsFileLine();
            DockPanel.SetDock(settingsFile, Dock.Bottom);
            root.Children.Add(settingsFile);

            var sessionStrip = BuildSessionStrip();
            if (sessionStrip != null)
            {
                DockPanel.SetDock(sessionStrip, Dock.Bottom);
                root.Children.Add(sessionStrip);
            }

            root.Children.Add(BuildBody());
            return root;
        }

        private UIElement BuildBody()
        {
            var body = new Grid { Margin = new Thickness(Spacing.Lg, 0, Spacing.Lg, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Sm) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 180 });

            var list = MakeCard(BuildRuleList());
            Grid.SetColumn(list, 0);
            body.Children.Add(list);

            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ShowsPreview = false,
            };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            var detail = MakeCard(BuildDetailPane());
            Grid.SetColumn(detail, 2);
            body.Children.Add(detail);

            return body;
        }

        private static Border MakeCard(UIElement child)
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = child,
            };
            card.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceInput);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            return card;
        }

        private UIElement BuildRuleList()
        {
            var dock = new DockPanel { LastChildFill = true };

            // Column header. The list's vertical scroll bar is always shown, so reserving its width
            // here keeps the fixed right-hand columns aligned with the rows below.
            var headerGrid = MakeColumnGrid();
            AddCell(headerGrid, 0, HeaderText("Enabled", HorizontalAlignment.Center));
            AddCell(headerGrid, 1, HeaderText("Rule", HorizontalAlignment.Left));
            AddCell(headerGrid, 2, HeaderText("Name", HorizontalAlignment.Left));
            AddCell(headerGrid, 3, HeaderText("Severity", HorizontalAlignment.Left));
            AddCell(headerGrid, 4, HeaderText("Fix", HorizontalAlignment.Center));
            var header = new Border
            {
                Child = headerGrid,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(Spacing.Sm, Spacing.Xs + 1, Spacing.Sm + SystemParameters.VerticalScrollBarWidth, Spacing.Xs + 1),
            };
            header.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfacePanel);
            header.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            var groupsPanel = new StackPanel();
            foreach (var categoryRules in _rules
                         .GroupBy(r => r.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = categoryRules.OrderBy(r => r.RuleId, StringComparer.OrdinalIgnoreCase).ToList();
                var group = BuildGroup(ordered[0].Category ?? string.Empty, ordered);
                _groups.Add(group);
                groupsPanel.Children.Add(group.Expander);
            }

            if (_rules.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "No rules were returned by the engine.",
                    Margin = new Thickness(Spacing.Md),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                groupsPanel.Children.Add(empty);
            }

            var scroll = new ScrollViewer
            {
                Content = groupsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
            };
            dock.Children.Add(scroll);
            return dock;
        }

        private RuleGroup BuildGroup(string category, IReadOnlyList<AnalysisRuleInfoDto> rules)
        {
            var name = new TextBlock
            {
                Text = string.IsNullOrEmpty(category) ? "Other" : category,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            var count = new TextBlock
            {
                Margin = new Thickness(Spacing.Sm, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            count.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var rowsPanel = new StackPanel();
            var expander = new Expander
            {
                Template = GroupTemplate,
                IsExpanded = true,
                Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, count } },
                Content = rowsPanel,
            };
            AutomationProperties.SetName(expander, name.Text);

            var group = new RuleGroup(category, expander, count);
            foreach (var dto in rules)
            {
                var row = BuildRow(dto, group);
                group.AddRow(row);
                _rows.Add(row);
                rowsPanel.Children.Add(row.Container);
            }
            group.UpdateCount();
            return group;
        }

        private RuleRow BuildRow(AnalysisRuleInfoDto dto, RuleGroup group)
        {
            var grid = MakeColumnGrid();

            var enabled = new CheckBox
            {
                IsChecked = dto.Enabled,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(enabled, $"Enable {dto.RuleId} {dto.Name}");
            enabled.Checked += (_, __) => group.UpdateCount();
            enabled.Unchecked += (_, __) => group.UpdateCount();
            AddCell(grid, 0, enabled);

            var id = new TextBlock { Text = dto.RuleId, VerticalAlignment = VerticalAlignment.Center };
            id.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            AddCell(grid, 1, id);

            var isSessionSuppressed = _sessionSuppressed.Contains(dto.RuleId);
            var nameCell = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
            if (isSessionSuppressed)
            {
                // A session-suppressed rule still shows Enabled here — that is accurate, the
                // override is elsewhere — so mark the row rather than lying about the check box,
                // which would also corrupt the changed/unchanged comparison in GetOverrides.
                var badge = new TextBlock
                {
                    Text = "off this session",
                    FontSize = Typography.Small,
                    Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                badge.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.StatusWarning);
                DockPanel.SetDock(badge, Dock.Right);
                nameCell.Children.Add(badge);
            }
            var name = new TextBlock
            {
                Text = dto.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            nameCell.Children.Add(name);
            AddCell(grid, 2, nameCell);

            var severity = new ComboBox
            {
                Height = 24,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            foreach (var label in SeverityLabels) severity.Items.Add(label); // plain strings (ComboBoxTheming contract)
            severity.SelectedIndex = SeverityIndexForDisplay(dto.EffectiveSeverity);
            ComboBoxTheming.Apply(severity);
            AutomationProperties.SetName(severity, $"Severity of {dto.RuleId}");
            AddCell(grid, 3, severity);

            if (dto.AutoFixable)
            {
                var fix = new TextBlock
                {
                    Text = "✓",
                    FontWeight = Typography.WeightSemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "This rule has an automatic fix",
                };
                fix.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.StatusSuccess);
                AutomationProperties.SetName(fix, "Automatic fix available");
                AddCell(grid, 4, fix);
            }

            var container = new Border
            {
                Child = grid,
                Padding = new Thickness(Spacing.Sm, 3, Spacing.Sm, 3),
                Background = Brushes.Transparent,
            };
            if (isSessionSuppressed)
            {
                container.ToolTip =
                    $"{dto.RuleId} is disabled for this session only. It is not saved anywhere and comes " +
                    "back when SSMS restarts, or when you restore it below.";
            }

            var row = new RuleRow(dto, enabled, severity, container, group, isSessionSuppressed);

            // Clicking anywhere on the row, or tabbing into its controls, selects it for the
            // description pane. Not marked handled, so the check box and combo still work.
            container.PreviewMouseLeftButtonDown += (_, __) => Select(row);
            container.IsKeyboardFocusWithinChanged += (_, e) =>
            {
                if ((bool)e.NewValue) Select(row);
            };
            container.MouseEnter += (_, __) => PaintRow(row);
            container.MouseLeave += (_, __) => PaintRow(row);
            return row;
        }

        private UIElement BuildDetailPane()
        {
            var stack = new StackPanel { Margin = new Thickness(Spacing.Md) };

            _detailTitle = new TextBlock
            {
                FontSize = Typography.H4,
                FontWeight = Typography.WeightSemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            _detailTitle.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            stack.Children.Add(_detailTitle);

            _detailMeta = new TextBlock
            {
                FontSize = Typography.Small,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, Spacing.Xs, 0, 0),
            };
            _detailMeta.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            stack.Children.Add(_detailMeta);

            _detailText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, Spacing.Md, 0, 0),
            };
            _detailText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            stack.Children.Add(_detailText);

            ShowDetail(null);

            return new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
            };
        }

        private UIElement BuildSettingsFileLine()
        {
            var line = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, 0),
            };

            var label = new TextBlock
            {
                Text = "Settings file:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Xs, 0),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            DockPanel.SetDock(label, Dock.Left);
            line.Children.Add(label);

            // A read-only, borderless text box rather than a TextBlock so the path can be copied.
            _settingsFileBox = new TextBox
            {
                Text = Constants.ConfigFilePath,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _settingsFileBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            _settingsFileBox.SetResourceReference(TextBoxBase.CaretBrushProperty, ThemeTokens.TextPrimary);
            AutomationProperties.SetName(_settingsFileBox, "Settings file");
            line.Children.Add(_settingsFileBox);
            return line;
        }

        /// <summary>
        /// The "disabled for this session only" strip: what is suppressed, and one button to put it
        /// back. Returns <c>null</c> when nothing is session-suppressed, so the window is unchanged
        /// for the common case.
        /// </summary>
        private UIElement? BuildSessionStrip()
        {
            if (_sessionSuppressed.Count == 0) return null;

            var strip = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(Spacing.Md, Spacing.Sm, Spacing.Sm, Spacing.Sm),
                Margin = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, 0),
            };
            strip.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);
            strip.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.StatusWarning);

            var dock = new DockPanel { LastChildFill = true };

            var restore = new Button
            {
                Content = "Restore",
                MinWidth = 90,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                Margin = new Thickness(Spacing.Md, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            ThemedButton.ApplySecondary(restore);
            DockPanel.SetDock(restore, Dock.Right);
            dock.Children.Add(restore);
            SessionRestoreButton = restore;

            var glyph = new TextBlock
            {
                Text = "⚠",
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.StatusWarning);
            DockPanel.SetDock(glyph, Dock.Left);
            dock.Children.Add(glyph);

            var ordered = _sessionSuppressed.OrderBy(r => r, StringComparer.OrdinalIgnoreCase);
            _sessionLabel = new TextBlock
            {
                Text = "Disabled for this session only: " + string.Join(", ", ordered),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _sessionLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            dock.Children.Add(_sessionLabel);

            restore.Click += (_, __) =>
            {
                RestoreSessionSuppressions = true;
                _sessionLabel.Text = "These rules will be restored when you click Save.";
                restore.IsEnabled = false;
            };

            strip.Child = dock;
            return strip;
        }

        private UIElement BuildFooter()
        {
            var footer = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(Spacing.Lg, Spacing.Md, Spacing.Lg, Spacing.Md),
                Margin = new Thickness(0, Spacing.Md, 0, 0),
            };
            footer.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfacePanel);
            footer.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            SaveButton = new Button
            {
                Content = "Save",
                MinWidth = 90,
                IsDefault = true,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            ThemedButton.ApplyPrimary(SaveButton);
            SaveButton.Click += (_, __) => DialogResult = true;

            CancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 90,
                IsCancel = true,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            ThemedButton.ApplySecondary(CancelButton);

            buttons.Children.Add(SaveButton);
            buttons.Children.Add(CancelButton);
            footer.Child = buttons;
            return footer;
        }

        // ─── Selection / description ─────────────────────────────────────────

        /// <summary>Selects <paramref name="row"/> and shows its description.</summary>
        internal void Select(RuleRow row)
        {
            if (ReferenceEquals(_selected, row)) return;
            var previous = _selected;
            _selected = row;
            if (previous != null) PaintRow(previous);
            PaintRow(row);
            ShowDetail(row.Dto);
        }

        private void PaintRow(RuleRow row)
        {
            if (ReferenceEquals(row, _selected))
                row.Container.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceSelection);
            else if (row.Container.IsMouseOver)
                row.Container.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceHover);
            else
                row.Container.Background = Brushes.Transparent;
        }

        private void ShowDetail(AnalysisRuleInfoDto? dto)
        {
            if (dto == null)
            {
                _detailTitle.Text = "No rule selected";
                _detailMeta.Text = string.Empty;
                _detailText.Text = "Select a rule to see what it checks.";
                return;
            }

            _detailTitle.Text = string.IsNullOrEmpty(dto.Name) ? dto.RuleId : $"{dto.RuleId} · {dto.Name}";

            var meta = new List<string>(4);
            if (!string.IsNullOrEmpty(dto.Category)) meta.Add(dto.Category);
            meta.Add("Default severity: " + SeverityLabels[SeverityIndexForDisplay(dto.DefaultSeverity)]);
            if (dto.AutoFixable) meta.Add("Automatic fix available");
            if (dto.RequiresSchema) meta.Add("Needs a database connection");
            _detailMeta.Text = string.Join(" · ", meta);

            var text = string.IsNullOrWhiteSpace(dto.Description) ? "No description." : dto.Description;
            if (_sessionSuppressed.Contains(dto.RuleId))
                text += Environment.NewLine + Environment.NewLine +
                        "Disabled for this session only. It comes back when SSMS restarts, or when you restore it below.";
            _detailText.Text = text;
        }

        // ─── Results ─────────────────────────────────────────────────────────

        /// <summary>
        /// Collects the per-rule global overrides to persist. The caller replaces
        /// <c>config.json codeAnalysis.ruleOverrides</c> wholesale with this result, so the method
        /// must preserve any existing global overrides the user did NOT touch in this session.
        ///
        /// Strategy (unchanged from the WinForms dialog):
        /// 1. Seed the result from the current on-disk global overrides so that untouched rules
        ///    keep their saved global setting.
        /// 2. For each row compare the current value against the effective baseline shown on open
        ///    (dto.Enabled / dto.EffectiveSeverity — the values the row was populated with):
        ///    - Unchanged → skip; the seeded value (if any) survives unchanged.
        ///    - Changed AND still differs from built-in default → write/replace the override.
        ///    - Changed AND reverted to built-in default → remove the override entry entirely
        ///      (lets the user explicitly clear a rule back to factory default).
        ///
        /// Comparing against the baseline, not dto.DefaultSeverity, keeps project-.casettings values
        /// from being baked into config.json on every Save.
        /// </summary>
        public Dictionary<string, RuleOverride> GetOverrides()
        {
            // Seed from the current global config so untouched rows preserve existing overrides.
            var existing = ConfigManager.Load().CodeAnalysis.RuleOverrides;
            var result = new Dictionary<string, RuleOverride>(existing, StringComparer.OrdinalIgnoreCase);

            foreach (var row in _rows)
            {
                var dto = row.Dto;
                bool enabled = row.EnabledBox.IsChecked == true;
                int sevIdx = row.SeverityIndex;

                // If the user left the row exactly as it was displayed on open, do nothing —
                // the seeded value (if any) is already in result.
                bool unchanged = enabled == dto.Enabled && sevIdx == dto.EffectiveSeverity;
                if (unchanged) continue;

                // User changed something. If they reverted all the way back to the rule's built-in
                // default, remove any override so the rule falls through to engine defaults.
                if (enabled && sevIdx == dto.DefaultSeverity)
                {
                    result.Remove(dto.RuleId);
                }
                else
                {
                    result[dto.RuleId] = new RuleOverride
                    {
                        Enabled = enabled,
                        Severity = SeverityToString(sevIdx),
                    };
                }
            }

            return result;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        /// <summary>The combo index a severity is shown at; out-of-range values show as Warning.</summary>
        internal static int SeverityIndexForDisplay(int severity) =>
            severity >= 0 && severity < SeverityLabels.Length ? severity : 2;

        private static string SeverityToString(int severity) => severity switch
        {
            0 => "hint",
            1 => "information",
            2 => "warning",
            3 => "error",
            _ => "warning",
        };

        private static Grid MakeColumnGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColEnabledWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColRuleWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColSeverityWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColFixWidth) });
            return grid;
        }

        private static void AddCell(Grid grid, int column, UIElement element)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        private static TextBlock HeaderText(string text, HorizontalAlignment alignment)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center,
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            return block;
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        /// <summary>
        /// Category header template: a flat themed bar (chevron + header content) over the rows. The
        /// stock Expander's toggle draws a hard-coded dark circle and arrow that vanish in the Dark
        /// theme. Every brush is a DynamicResource, resolved against each window's own resources, so
        /// one sealed template serves every theme (same reasoning as <see cref="ThemedButton"/>).
        /// </summary>
        private static ControlTemplate BuildGroupTemplate()
        {
            var dock = new FrameworkElementFactory(typeof(DockPanel));

            var toggle = new FrameworkElementFactory(typeof(ToggleButton), "HeaderSite");
            toggle.SetValue(DockPanel.DockProperty, Dock.Top);
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(Expander.IsExpanded))
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay,
            });
            toggle.SetValue(ContentControl.ContentProperty, new TemplateBindingExtension(HeaderedContentControl.HeaderProperty));
            toggle.SetValue(Control.TemplateProperty, BuildHeaderToggleTemplate());
            toggle.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
            toggle.SetValue(Control.PaddingProperty, new Thickness(Spacing.Sm, 5, Spacing.Sm, 5));
            toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            // The template draws its own themed focus ring (see BuildHeaderToggleTemplate). A Style
            // instance can't live in this shared, sealed template: sealing reads it, and WPF only
            // lets the thread that created an unsealed Style do that.
            toggle.SetValue(FrameworkElement.FocusVisualStyleProperty, null);
            toggle.SetResourceBinding(Control.BackgroundProperty, ThemeTokens.SurfacePanel);
            toggle.SetResourceBinding(Control.BorderBrushProperty, ThemeTokens.BorderSubtle);
            toggle.SetResourceBinding(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            dock.AppendChild(toggle);

            var content = new FrameworkElementFactory(typeof(ContentPresenter), "ExpandSite");
            content.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            dock.AppendChild(content);

            var template = new ControlTemplate(typeof(Expander)) { VisualTree = dock };
            var expanded = new Trigger { Property = Expander.IsExpandedProperty, Value = true };
            expanded.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "ExpandSite"));
            template.Triggers.Add(expanded);
            template.Seal();
            return template;
        }

        private static ControlTemplate BuildHeaderToggleTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var panel = new FrameworkElementFactory(typeof(DockPanel));
            panel.SetValue(DockPanel.LastChildFillProperty, true);

            // Right-pointing chevron, turned 90° (pointing down) while the group is expanded.
            var arrow = new FrameworkElementFactory(typeof(Path), "Arrow");
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0 0 L 4 4 L 0 8 Z"));
            arrow.SetResourceBinding(Shape.FillProperty, ThemeTokens.TextSecondary);
            arrow.SetValue(FrameworkElement.WidthProperty, 8.0);
            arrow.SetValue(FrameworkElement.HeightProperty, 8.0);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(Spacing.Xs, 0, Spacing.Sm, 0));
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
            arrow.SetValue(DockPanel.DockProperty, Dock.Left);
            panel.AppendChild(arrow);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(presenter);

            border.AppendChild(panel);

            // Focus ring drawn over the bar (no layout shift), in the theme's focus colour.
            var focusRing = new FrameworkElementFactory(typeof(Border), "FocusRing");
            focusRing.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            focusRing.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            focusRing.SetResourceBinding(Border.BorderBrushProperty, ThemeTokens.BorderFocus);
            focusRing.SetValue(UIElement.IsHitTestVisibleProperty, false);
            focusRing.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

            var root = new FrameworkElementFactory(typeof(Grid));
            root.AppendChild(border);
            root.AppendChild(focusRing);

            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = root };

            var isChecked = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            isChecked.Setters.Add(new Setter(UIElement.RenderTransformProperty, ExpandedArrow, "Arrow"));
            template.Triggers.Add(isChecked);

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceHover), "Bd"));
            template.Triggers.Add(hover);

            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "FocusRing"));
            template.Triggers.Add(focused);

            template.Seal();
            return template;
        }

        // ─── Row / group records ─────────────────────────────────────────────

        /// <summary>One rule's row: its DTO and the controls that edit it.</summary>
        internal sealed class RuleRow
        {
            public RuleRow(AnalysisRuleInfoDto dto, CheckBox enabledBox, ComboBox severityBox, Border container,
                RuleGroup group, bool isSessionSuppressed)
            {
                Dto = dto;
                EnabledBox = enabledBox;
                SeverityBox = severityBox;
                Container = container;
                Group = group;
                IsSessionSuppressed = isSessionSuppressed;
            }

            public AnalysisRuleInfoDto Dto { get; }
            public CheckBox EnabledBox { get; }
            public ComboBox SeverityBox { get; }
            public Border Container { get; }
            public RuleGroup Group { get; }
            public bool IsSessionSuppressed { get; }

            /// <summary>The selected severity (DiagnosticSeverity int); Warning when nothing is selected.</summary>
            public int SeverityIndex =>
                SeverityBox.SelectedIndex >= 0 && SeverityBox.SelectedIndex < SeverityLabels.Length
                    ? SeverityBox.SelectedIndex
                    : 2;
        }

        /// <summary>One category: its expandable header and rows.</summary>
        internal sealed class RuleGroup
        {
            private readonly List<RuleRow> _rows = new List<RuleRow>();
            private readonly TextBlock _countText;

            public RuleGroup(string category, Expander expander, TextBlock countText)
            {
                Category = category;
                Expander = expander;
                _countText = countText;
            }

            public string Category { get; }
            public Expander Expander { get; }
            public IReadOnlyList<RuleRow> Rows => _rows;

            /// <summary>"n of m on" next to the category name.</summary>
            public string CountText => _countText.Text;

            internal void AddRow(RuleRow row) => _rows.Add(row);

            internal void UpdateCount()
            {
                var on = _rows.Count(r => r.EnabledBox.IsChecked == true);
                _countText.Text = $"{on} of {_rows.Count} on";
            }
        }
    }
}
