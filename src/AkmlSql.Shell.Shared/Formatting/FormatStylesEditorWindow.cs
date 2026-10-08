#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.StatusBar;
using AkmlSql.Shell.Shared.Ui.SqlPreview;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 020 US3 (T052/T053/T054/T055/T060) — Format Styles editor modal window.
    /// Three-column layout matching SQL Prompt's documented Edit Formatting Styles editor
    /// (<c>doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §8</c>):
    /// <list type="bullet">
    ///   <item>Left: style list with Built-in / Built-in · modified / Native badge.</item>
    ///   <item>Middle: settings tree built from the engine's <c>FormatSettingSchema</c>.</item>
    ///   <item>Right: controls + live preview placeholder (Tier 2b — controls panel and
    ///   preview wiring land in the follow-up commit).</item>
    /// </list>
    ///
    /// <para>
    /// Programmatic WPF only (no XAML) — the established shell-dialog pattern.
    /// Chrome flows from <see cref="ThemeRegistry"/>
    /// via <c>SetResourceReference</c>; brushes are pre-frozen at palette-build time so no
    /// per-call allocation is needed. Owner is set from the DTE HWND so the dialog
    /// centres on the host's main window.
    /// </para>
    /// </summary>
    internal sealed class FormatStylesEditorWindow : DialogWindow
    {
        private readonly FormatStylesEditorViewModel _viewModel;
        private ListBox? _styleList;
        private TreeView? _settingsTree;
        private StackPanel? _settingControlsHost;
        private TextBlock? _settingControlsEmpty;
        private TextBox? _previewTextBox;      // "Edit sample" mode only: the raw, editable sample
        private SqlPreviewView? _previewView;  // the formatted preview (spec 040 T080)
        private Border? _previewWarningBar;
        private TextBlock? _previewWarningText;
        private TextBlock? _statusText;

        // Spec 033 (T016) — editing UX state
        private Button? _saveBtn;

        /// <summary>Footer escape hatch shown when the selected style is a read-only built-in:
        /// makes an editable copy in one click, so a disabled Save is never a dead end.</summary>
        private Button? _resetButton;
        private Button? _revertButton;

        /// <summary>First-class "Set as active" affordance under the style list — activation was
        /// previously reachable only from the per-row ⋮ / right-click menu.</summary>
        private Button? _setActiveButton;

        // Header state line — the window narrates what it is editing and what Format SQL will use.
        private TextBlock? _headerSubject;
        private Border? _headerBuiltInChip;
        private TextBlock? _headerBuiltInChipText;   // "Built-in" / "Built-in · modified"
        private Border? _headerDirtyChip;      // fixed label — visibility only
        private Border? _headerActiveChip;
        private TextBlock? _headerActiveChipText;
        private TextBlock? _stylesHeader;
        private Border? _builtInHint;
        private TextBlock? _builtInHintText;

        /// <summary>Spec 040 (T187) — "Team styles unavailable — ‹folder› can't be reached", under the list.</summary>
        private TextBlock? _teamUnavailableRow;
        // SQL Prompt-parity redesign: the right pane edits a whole settings *group* (SQL Prompt's
        // "page") at once, not one setting at a time. _currentGroup is the group whose form is
        // showing; _currentGroupCategory is its parent category (for the breadcrumb title).
        private FormatStylesSchemaModel.Group? _currentGroup;
        private string? _currentGroupCategory;

        /// <summary>Rows on the current page whose option another option turns on (EnabledWhen).</summary>
        private readonly System.Collections.Generic.List<GatedRow> _gatedRows = new System.Collections.Generic.List<GatedRow>();

        private sealed class GatedRow
        {
            public GatedRow(FormatSettingNode setting, TextBlock label, FrameworkElement control, TextBlock hint)
            {
                Setting = setting;
                Label = label;
                Control = control;
                Hint = hint;
            }

            public FormatSettingNode Setting { get; }
            public TextBlock Label { get; }
            public FrameworkElement Control { get; }

            /// <summary>Spec 040 (T103): "Takes effect when …" under the row, shown while the gate is closed.</summary>
            public TextBlock Hint { get; }
        }

        // Spec 040 (T097) — option search.
        private TextBox? _searchBox;
        private TextBlock? _searchPlaceholder;
        private DispatcherTimer? _searchTimer;
        private StyleOptionSearchResult? _searchResult;
        private System.Collections.Generic.HashSet<string> _searchMatches = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        /// <summary>Page tree leaves by group id, with their changed-count and match-count badges.</summary>
        private readonly System.Collections.Generic.Dictionary<string, (TreeViewItem Leaf, TextBlock Changed, TextBlock Matches, string? Category)> _groupLeaves =
            new System.Collections.Generic.Dictionary<string, (TreeViewItem, TextBlock, TextBlock, string?)>(StringComparer.Ordinal);

        // Spec 040 (T099) — changed-from-default markers on the rows of the current page.
        private readonly System.Collections.Generic.Dictionary<string, (FormatSettingNode Setting, TextBlock Label, Button Reset)> _rowMarkers =
            new System.Collections.Generic.Dictionary<string, (FormatSettingNode, TextBlock, Button)>(StringComparer.Ordinal);
        private DispatcherTimer? _movedTimer;

        // ── 2026-10 redesign: more room for the preview, and what each value does ──────────
        /// <summary>The size the window opens at, when the screen has room for it.</summary>
        internal const double DefaultWidth = 1440;
        internal const double DefaultHeight = 900;
        private const double DefaultStylesWidth = 232;   // "Khamis Style  Modified  ACTIVE" fits
        private const double StylesMinWidth = 160;
        /// <summary>Narrower than this, the preview goes under the options instead of beside them.</summary>
        internal const double StackBelowWidth = 760;

        // Kept for the session, like SQL Prompt's editor: reopening the window keeps the layout.
        private static bool s_stylesCollapsed;
        private static double s_stylesWidth = DefaultStylesWidth;
        private static bool s_examplesOpen = true;

        private ColumnDefinition? _stylesColumn;
        private Border? _stylesCard;
        private Border? _stylesRail;
        private GridSplitter? _stylesSplitter;
        private Grid? _rightPanel;
        private Border? _formCard;
        private GridSplitter? _rightSplitter;
        private Border? _previewCard;
        private bool? _stacked;
        private ComboBox? _sourceCombo;
        private bool _suppressSourceChanged;
        private bool _userPickedSource;

        // The option examples under the preview: one card per value of the focused option.
        private RowDefinition? _previewRow;
        private RowDefinition? _examplesRow;
        private TextBlock? _examplesTitle;
        private TextBlock? _examplesSubtitle;
        private StackPanel? _examplesHost;
        private ScrollViewer? _examplesScroll;
        private Button? _examplesToggle;
        private string? _focusedSettingId;
        private string? _examplesShownFor;
        private string? _examplesSignature;
        private DispatcherTimer? _examplesTimer;
        private System.Threading.CancellationTokenSource? _examplesCts;
        /// <summary>The value whose "Use this" was pressed from the keyboard: its card gets focus back.</summary>
        private string? _refocusExampleKey;
        private Button? _stylesExpandButton;
        /// <summary>Set once the window closes: timers and late previews must not start more work.</summary>
        private bool _closed;
        private readonly System.Collections.Generic.Dictionary<string, Border> _rowBorders =
            new System.Collections.Generic.Dictionary<string, Border>(StringComparer.Ordinal);

        /// <summary>
        /// Spec 040 (T091) — test seam for the Rename prompt: given the current name and the existing
        /// style names, returns the new name, or null when cancelled. Null in the product.
        /// </summary>
        internal Func<string, System.Collections.Generic.IReadOnlyCollection<string>, string?>? RenameNameOverride { get; set; }
        private TextBlock? _breadcrumbText;
        private bool _suppressSelectionChanged;
        private bool _closeConfirmed;

        // Spec 033 (T025 / FR-014, closes spec-020 T069) — in-window preview-sample editing.
        // Editing mode is DERIVED from the toggle (no shadow flag to desync); edits commit in
        // one batch on toggle-off / close instead of per keystroke (each PreviewSample set is
        // ~5 synchronous filesystem ops on the dispatcher thread plus a discarded preview run).
        private CheckBox? _editSampleToggle;
        private bool EditingSample => _editSampleToggle?.IsChecked == true;

        /// <summary>
        /// Spec 040 (T078) — the <see cref="FrameworkElement.Tag"/> of every option label on a
        /// settings page, so tests find the labels without depending on the row's layout.
        /// </summary>
        internal const string OptionLabelTag = "akml-option-label";

        // Spec 040 (T077) — seams for the dialogs Import and Export show. Null in the product,
        // where the real file pickers, prompts and summary dialog appear.

        /// <summary>Returns the file to import, or null when the user cancels.</summary>
        internal Func<string?>? ImportFileOverride { get; set; }

        /// <summary>Given the suggested file name, returns the export path, or null when cancelled.</summary>
        internal Func<string, string?>? ExportFileOverride { get; set; }

        /// <summary>Given the suggested name and the existing style names, returns the name to import under, or null.</summary>
        internal Func<string, System.Collections.Generic.IReadOnlyCollection<string>, string?>? ImportNameOverride { get; set; }

        /// <summary>Answers the Save / Discard / Cancel prompt shown with the given message.</summary>
        internal Func<string, StyleSwitchDecision>? SaveDecisionOverride { get; set; }

        /// <summary>Replaces the import summary dialog.</summary>
        internal Action<ProfileImportResponse>? ImportSummaryOverride { get; set; }

        private static System.Windows.Media.SolidColorBrush Freeze(System.Windows.Media.SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        // Semantic invalid-input red (theme-independent per CLAUDE.md); hoisted — control
        // builders run on every tree-node click.
        private static readonly System.Windows.Media.SolidColorBrush InvalidInputBrush =
            Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x14, 0x00)));

        // The live-preview card follows the theme (spec 040 T080): its SqlPreviewView colours SQL
        // from theme tokens, which a fixed dark card would make unreadable in the light theme.
        // Only the amber warning strip keeps fixed (semantic) colours.
        private static readonly System.Windows.Media.SolidColorBrush PreviewWarnTextBrush =
            Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x1A, 0x00)));

        public FormatStylesEditorWindow(FormatStylesEditorViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            Title = AkmlSql.Core.Config.WindowTitles.For("Format styles");
            // Room for the options and a preview beside them; on a smaller screen, 92% of it.
            var work = SystemParameters.WorkArea;
            Width = Math.Min(DefaultWidth, Math.Floor(work.Width * 0.92));
            Height = Math.Min(DefaultHeight, Math.Floor(work.Height * 0.92));
            MinWidth = Math.Min(1000, Width);
            MinHeight = Math.Min(620, Height);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            HasHelpButton = true; // spec 040 (X-03): the title-bar "?" opens CurrentHelpTopic

            // Ensure theme resources are merged so SetResourceReference resolves.
            ThemeRegistry.Instance.AttachTo(this);

            BuildUi();
            DataContext = _viewModel;

            // Spec 033 — the VM asks the window what to do with unsaved edits on style switch.
            _viewModel.DirtyDecisionHandler = PromptStyleSwitchDecisionAsync;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.WorkingValueChanged += OnWorkingValueChanged;
            PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (HandleKey(key, Keyboard.Modifiers, _styleList?.IsKeyboardFocusWithin == true))
                    e.Handled = true;
            };
            Loaded += OnLoaded;

            // The title bar follows the theme: a light Windows title bar over a dark window looked broken.
            SourceInitialized += (_, _) => TitleBarTheme.Apply(this, ThemeRegistry.Instance.Current == ThemeVariant.Dark);
            ThemeRegistry.Instance.VariantChanged += OnThemeVariantChanged;
            Closed += (_, _) =>
            {
                _closed = true;
                ThemeRegistry.Instance.VariantChanged -= OnThemeVariantChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.WorkingValueChanged -= OnWorkingValueChanged;
                _examplesTimer?.Stop();
                _examplesCts?.Cancel();
            };

            // Spec 040 (X-03, FR-062): F1 anywhere in the window opens the style-editor topic.
            global::AkmlSql.Shell.Shared.Help.HelpBinding.Attach(this, () => CurrentHelpTopic);
        }

        // ── F1 help (spec 040, X-03, FR-062) ─────────────────────────────────

        /// <summary>What F1 and the title-bar "?" open (contracts/ui.md §3).</summary>
        internal string? CurrentHelpTopic => global::AkmlSql.Shell.Shared.Help.F1HelpRegistrations.FormatStylesTopic;

        /// <summary>The title-bar "?" (<see cref="DialogWindowBase.HasHelpButton"/>).</summary>
        protected override void InvokeDialogHelp() =>
            global::AkmlSql.Shell.Shared.Help.HelpBinding.Open(() => CurrentHelpTopic);

        // -----------------------------------------------------------------

        private void BuildUi()
        {
            var res = ThemeRegistry.Instance.Resources;
            var fg = (SolidColorBrush)res[ThemeTokens.TextPrimary];

            // Outer grid: header / content / footer
            var root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceCanvas);
            // Themed check boxes, radio buttons and scroll bars for everything in the window.
            FormatStylesChrome.ApplyImplicitStyles(root, this);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Header ─────────────────────────────────────────────────────
            var header = new Border
            {
                Padding = new Thickness(Spacing.Lg, Spacing.Md, Spacing.Lg, Spacing.Md),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            header.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            header.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfacePanel);
            // The header states what the window is DOING, rather than repeating its own title.
            // A user opens this dialog with two questions — "which style am I editing (and have I
            // changed it?)" and "which style will Format SQL actually use?" — and previously it
            // answered neither: the edited style was implied only by a list highlight, unsaved work
            // only by a greyed Save button, and the active style by one small badge inside a
            // scrollable row. That gap is what made "selecting a style doesn't mark it" feel broken.
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // subject
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // state chips

            var subjectStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };

            // "EDITING" labels the style name below it. (Deliberately not repeating the window's
            // own title — the title bar already says "AKML SQL – Format styles"; a header that echoes it
            // would spend the most valuable line in the window on nothing.)
            var eyebrow = new TextBlock
            {
                Text = "EDITING",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                Margin = new Thickness(0, 0, 0, 1),
            };
            eyebrow.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            subjectStack.Children.Add(eyebrow);

            _headerSubject = new TextBlock
            {
                Text = "No style selected",
                FontFamily = Typography.UiFont,
                FontSize = Typography.H3,
                FontWeight = Typography.WeightSemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _headerSubject.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            subjectStack.Children.Add(_headerSubject);

            Grid.SetColumn(subjectStack, 0);
            headerGrid.Children.Add(subjectStack);

            var chips = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _headerBuiltInChip = MakeHeaderChip("Built-in", ChipKind.Neutral, out _headerBuiltInChipText);
            _headerDirtyChip = MakeHeaderChip("Unsaved changes", ChipKind.Warning, out _);
            _headerActiveChip = MakeHeaderChip("Active: —", ChipKind.Accent, out _headerActiveChipText);
            _headerActiveChip.ToolTip = "The style Format SQL uses";
            chips.Children.Add(_headerBuiltInChip);
            chips.Children.Add(_headerDirtyChip);
            chips.Children.Add(_headerActiveChip);
            Grid.SetColumn(chips, 1);
            headerGrid.Children.Add(chips);

            header.Child = headerGrid;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Content: three cards (Styles | Style options | Settings + preview) ──
            // Column widths mirror SQL Prompt's Edit Formatting Styles editor (fixed left/middle,
            // flexible right); the two 8px gutter columns double as invisible drag splitters.
            var content = new Grid { Margin = new Thickness(Spacing.Md, Spacing.Md, Spacing.Md, Spacing.Sm) };
            // The style list can fold away to a rail (« in its header), giving its width to the preview.
            _stylesColumn = new ColumnDefinition { Width = new GridLength(s_stylesWidth), MinWidth = StylesMinWidth };
            content.ColumnDefinitions.Add(_stylesColumn);                                               // styles
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Sm) }); // gutter/splitter
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180), MinWidth = 170 }); // style options
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Sm) }); // gutter/splitter
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 }); // options + preview

            _stylesCard = (Border)BuildLeftPanel();
            content.Children.Add(_stylesCard);
            _stylesRail = BuildStylesRail();
            content.Children.Add(_stylesRail);
            _stylesSplitter = MakeColumnSplitter(1);
            content.Children.Add(_stylesSplitter);
            content.Children.Add(BuildMiddlePanel());
            content.Children.Add(MakeColumnSplitter(3));
            content.Children.Add(BuildRightPanel());
            SetStylesCollapsed(s_stylesCollapsed, focusList: false);

            Grid.SetRow(content, 1);
            root.Children.Add(content);

            // ── Footer: Import/Export (left) · status · Save/Close (right) ──
            var footer = new Border
            {
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
                BorderThickness = new Thickness(0, 1, 0, 0),
            };
            footer.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            footer.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfacePanel);

            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // import/export
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // status
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // save/close

            // Import / Export live here (off the crowded style list) — style-file I/O, not per-style edits.
            var ioButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ioButtons.Children.Add(MakeSecondaryButton("Import…", OnImportAsync));
            ioButtons.Children.Add(MakeSecondaryButton("Export…", OnExportAsync));
            Grid.SetColumn(ioButtons, 0);
            footerGrid.Children.Add(ioButtons);

            _statusText = new TextBlock
            {
                Text = string.Empty,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(Spacing.Md, 0, Spacing.Md, 0),
            };
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            Grid.SetColumn(_statusText, 1);
            footerGrid.Children.Add(_statusText);

            var footerButtons = new StackPanel { Orientation = Orientation.Horizontal };

            // Spec 033 (T016) — Save persists the loaded style via merge-save. Enabled only
            // when a loaded, editable style has unsaved edits.
            _saveBtn = new Button
            {
                Content = "Save",
                Padding = FooterButtonPadding,
                MinWidth = 84,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                IsEnabled = false,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                ToolTip = "Select a style first",   // replaced by UpdateSaveButtonState once one loads
            };
            ThemedButton.ApplyPrimary(_saveBtn);
            _saveBtn.Click += async (_, _) =>
            {
                try
                {
                    await SaveSelectedStyleAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "FormatStylesEditor: save failed");
                    SetStatus(ex.Message);
                }
            };
            footerButtons.Children.Add(_saveBtn);

            // This slot used to hold "Copy to edit" — the escape hatch from read-only built-ins.
            // Built-ins are editable now, so the escape hatch is gone and the slot carries the
            // action that replaces it: undo those edits. Copy remains on the ⋮ menu, where it is
            // a deliberate choice rather than a workaround.
            _revertButton = new Button
            {
                Content = "Revert",
                Padding = FooterButtonPadding,
                MinWidth = 84,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                Visibility = Visibility.Collapsed,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                ToolTip = "Discard the unsaved changes and go back to the saved values.",
            };
            ThemedButton.ApplySecondary(_revertButton);
            _revertButton.Click += (_, _) =>
            {
                try
                {
                    var name = _viewModel.LoadedProfileName;
                    SetStatus(_viewModel.RevertChanges()
                        ? $"Reverted unsaved changes to '{name}'."
                        : _viewModel.LastError ?? "Nothing to revert.");
                    RefreshVisibleSettingControls();
                    UpdateHeaderState();
                    UpdateSaveButtonState();
                }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: revert failed"); SetStatus(ex.Message); }
            };
            footerButtons.Children.Add(_revertButton);

            _resetButton = new Button
            {
                Content = "Reset to built-in",
                Padding = FooterButtonPadding,
                MinWidth = 84,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                Visibility = Visibility.Collapsed,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                ToolTip = "Discard your saved changes to this built-in style and restore the original.",
            };
            ThemedButton.ApplySecondary(_resetButton);
            _resetButton.Click += async (_, _) =>
            {
                try { await OnResetStyleAsync(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: reset failed"); SetStatus(ex.Message); }
            };
            footerButtons.Children.Add(_resetButton);

            var closeBtn = new Button
            {
                Content = "Close",
                Padding = FooterButtonPadding,
                MinWidth = 84,
                IsCancel = true,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            ThemedButton.ApplySecondary(closeBtn);
            closeBtn.Click += (_, _) => Close();
            footerButtons.Children.Add(closeBtn);

            Grid.SetColumn(footerButtons, 2);
            footerGrid.Children.Add(footerButtons);

            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        // Invisible drag splitter that lives in an 8px gutter column between two pane cards.
        private static GridSplitter MakeColumnSplitter(int column)
        {
            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowsPreview = false,
            };
            Grid.SetColumn(splitter, column);
            return splitter;
        }

        // Wraps a pane's content in the standard card chrome (panel fill + subtle border + radius).
        private Border MakePaneCard(int column, FrameworkElement child)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Child = child,
            };
            card.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfacePanel);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            Grid.SetColumn(card, column);
            return card;
        }

        /// <summary>What the style list folds into: a narrow strip with » to bring it back.</summary>
        private Border BuildStylesRail()
        {
            var expand = new Button
            {
                Content = "»",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                ToolTip = "Show the style list",
            };
            FormatStylesChrome.ApplyIconButton(expand);
            System.Windows.Automation.AutomationProperties.SetName(expand, "Show the style list");
            expand.Click += (_, _) => SetStylesCollapsed(false, focusList: true);
            _stylesExpandButton = expand;

            var label = new TextBlock
            {
                Text = "STYLES",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, Spacing.Sm, 0, 0),
                LayoutTransform = new RotateTransform(90),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, Spacing.Sm, 0, Spacing.Sm) };
            stack.Children.Add(expand);
            stack.Children.Add(label);
            var rail = MakePaneCard(0, stack);
            rail.Width = 36;
            rail.HorizontalAlignment = HorizontalAlignment.Left;
            rail.Visibility = Visibility.Collapsed;
            return rail;
        }

        /// <summary>
        /// Folds the style list into its rail, or opens it again at the width it had. The choice
        /// lasts for the session. The list stays in the window either way (its keys work once open).
        /// </summary>
        private void SetStylesCollapsed(bool collapsed, bool focusList)
        {
            if (_stylesColumn == null || _stylesCard == null || _stylesRail == null) return;
            // The splitter rewrites the column's width: remember what the user dragged it to.
            if (collapsed && _stylesCard.Visibility == Visibility.Visible && _stylesColumn.ActualWidth >= StylesMinWidth)
                s_stylesWidth = _stylesColumn.ActualWidth;
            // Folding the card under the keyboard (« pressed with Space) would drop focus to the
            // window: hand it to the rail's » instead.
            var focusRail = collapsed && _stylesCard.IsKeyboardFocusWithin;
            s_stylesCollapsed = collapsed;
            _stylesCard.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            _stylesRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            _stylesColumn.MinWidth = collapsed ? 0 : StylesMinWidth;
            _stylesColumn.Width = collapsed ? GridLength.Auto : new GridLength(Math.Max(StylesMinWidth, s_stylesWidth));
            if (_stylesSplitter != null) _stylesSplitter.IsEnabled = !collapsed;
            if (!collapsed && focusList) _styleList?.Focus();
            if (focusRail) _stylesExpandButton?.Focus();
        }

        // -----------------------------------------------------------------
        // Left panel — style list
        // -----------------------------------------------------------------
        private FrameworkElement BuildLeftPanel()
        {
            var panel = new Grid { Margin = new Thickness(Spacing.Sm) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // header
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // list
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // + New Style CTA

            _stylesHeader = MakeSectionHeader("STYLES");
            var collapse = new Button
            {
                Content = "«",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top,
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                ToolTip = "Hide the style list, for a wider preview",
            };
            FormatStylesChrome.ApplyIconButton(collapse);
            System.Windows.Automation.AutomationProperties.SetName(collapse, "Hide the style list");
            collapse.Click += (_, _) => SetStylesCollapsed(true, focusList: false);
            var stylesHeaderRow = new DockPanel();
            DockPanel.SetDock(collapse, Dock.Right);
            stylesHeaderRow.Children.Add(collapse);
            stylesHeaderRow.Children.Add(_stylesHeader);
            Grid.SetRow(stylesHeaderRow, 0);
            panel.Children.Add(stylesHeaderRow);

            _styleList = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                ItemTemplate = BuildStyleListItemTemplate(),
            };
            _styleList.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            ScrollViewer.SetHorizontalScrollBarVisibility(_styleList, ScrollBarVisibility.Disabled);

            // Rows share the option tree's look: a rounded tint and an accent bar on the selected
            // style. The active style is marked by its ACTIVE pill alone — a second tint for
            // "active" beside the one for "selected" made the two hard to tell apart.
            _styleList.ItemContainerStyle = FormatStylesChrome.ListItemStyle();

            // Spec 033 (T036) — sectioned list: "YOUR STYLES" first, then "BUILT-IN STYLES",
            // names A→Z within each; group headers via a code-built template (upper-cased).
            var view = new System.Windows.Data.ListCollectionView(_viewModel.Profiles);
            view.GroupDescriptions!.Add(new System.Windows.Data.PropertyGroupDescription(
                nameof(StyleListItem.Section), new UpperCaseConverter()));
            // Your own styles, then TEAM STYLES (spec 040, STY-10), then built-in — robust to section-label rewording.
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(StyleListItem.SectionOrder), System.ComponentModel.ListSortDirection.Ascending));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(StyleListItem.Name), System.ComponentModel.ListSortDirection.Ascending));
            _styleList.ItemsSource = view;

            var groupHeaderTemplate = new DataTemplate();
            var headerFactory = new FrameworkElementFactory(typeof(TextBlock));
            headerFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            headerFactory.SetValue(TextBlock.FontWeightProperty, Typography.WeightSemiBold);
            headerFactory.SetValue(TextBlock.FontSizeProperty, (double)Typography.Small);
            headerFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(Spacing.Sm + 2, Spacing.Md, 2, Spacing.Xs));
            headerFactory.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            groupHeaderTemplate.VisualTree = headerFactory;
            _styleList.GroupStyle.Add(new GroupStyle { HeaderTemplate = groupHeaderTemplate });

            // Per-style ⋮ menu — opened by right-click AND the row's visible ⋮ glyph; enablement
            // recomputed on every open so it tracks the selected row.
            var menu = new ContextMenu();
            var miSetActive = MakeMenuItem("Set Active", OnSetActiveAsync);
            var miCopy = MakeMenuItem("Copy", OnCopyStyleAsync);
            var miRename = MakeMenuItem("Rename…", OnRenameStyleAsync);
            var miDelete = MakeMenuItem("Delete", OnDeleteStyleAsync);
            var miReset = MakeMenuItem("Reset to built-in", OnResetStyleAsync);
            var miExport = MakeMenuItem("Export…", OnExportAsync);
            menu.Items.Add(miSetActive);
            menu.Items.Add(miCopy);
            menu.Items.Add(miRename);
            menu.Items.Add(miDelete);
            menu.Items.Add(miReset);
            menu.Items.Add(new Separator());
            menu.Items.Add(miExport);
            menu.Opened += (_, _) =>
            {
                if (_styleList?.SelectedItem is StyleListItem selected)
                {
                    // Shipped styles stay un-renameable and un-deletable even though they are now
                    // editable: the name is what ties an override to the style it overrides, so
                    // renaming would orphan the original rather than rename anything.
                    // A read-only team style (spec 040, STY-10) can only be copied.
                    miRename.IsEnabled = !selected.IsShipped && !selected.IsReadOnly;
                    miDelete.IsEnabled = !selected.IsShipped && !selected.IsActive && !selected.IsReadOnly;
                    miReset.IsEnabled = selected.IsCustomized && !selected.IsReadOnly;
                    miSetActive.IsEnabled = !selected.IsActive;
                }
            };
            _styleList.ContextMenu = menu;
            _styleList.ContextMenuOpening += (_, e) =>
            {
                if (_styleList?.SelectedItem is not StyleListItem) e.Handled = true; // nothing selected — no menu
            };

            _styleList.SelectionChanged += async (_, _) => await OnStyleSelectionChangedAsync();
            // Double-click ACTIVATES the style (user-requested, 2026-07-25). This replaces the
            // earlier spec-033 Redgate behaviour of copying a read-only built-in: "make this the
            // style I format with" is the action people expect from double-clicking a list row,
            // and treating a built-in differently from a custom style made the gesture
            // unpredictable. Copy remains on the ⋮ / right-click menu.
            _styleList.MouseDoubleClick += async (_, _) =>
            {
                if (_styleList?.SelectedItem is not StyleListItem item || item.IsActive) return;
                try { await OnSetActiveAsync(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: double-click activate failed"); SetStatus(ex.Message); }
            };
            Grid.SetRow(_styleList, 1);
            panel.Children.Add(_styleList);

            // Activation used to live ONLY in the per-row ⋮ / right-click menu, so selecting a style
            // (which merely highlights it) looked like it should have applied — the reported "selecting
            // Khamis Style doesn't mark it". A first-class button makes the select→activate step
            // explicit and keeps the ⋮ menu working for users who already know it.
            var listFooter = new StackPanel { Orientation = Orientation.Vertical };

            // Spec 040 (T187, STY-10): a muted row when the team style folder can't be reached —
            // the user's own and built-in styles above it keep working.
            _teamUnavailableRow = new TextBlock
            {
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Spacing.Xs, Spacing.Xs, Spacing.Xs, Spacing.Sm),
                Visibility = Visibility.Collapsed,
            };
            _teamUnavailableRow.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            listFooter.Children.Add(_teamUnavailableRow);

            _setActiveButton = new Button
            {
                Content = "Set as active style",
                Padding = FooterButtonPadding,
                Margin = new Thickness(0, Spacing.Xs, 0, 0),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                IsEnabled = false,   // enabled by OnStyleSelectionChangedAsync for a non-active row
                ToolTip = "Make the selected style the one Format SQL uses",
            };
            ThemedButton.ApplySecondary(_setActiveButton);
            _setActiveButton.Click += async (_, _) =>
            {
                try { await OnSetActiveAsync(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: set-active failed"); SetStatus(ex.Message); }
            };
            listFooter.Children.Add(_setActiveButton);

            listFooter.Children.Add(MakeAccentCtaButton("+ New style", OnNewStyleAsync));
            Grid.SetRow(listFooter, 2);
            panel.Children.Add(listFooter);

            return MakePaneCard(0, panel);
        }

        /// <summary>How a header chip reads: context, unsaved work, or the style Format SQL uses.</summary>
        private enum ChipKind { Neutral, Warning, Accent }

        /// <summary>One padding, so every footer and list button has the same height.</summary>
        private static readonly Thickness FooterButtonPadding = new Thickness(Spacing.Md + 2, 5, Spacing.Md + 2, 5);

        /// <summary>
        /// A rounded header status pill. A neutral pill is context ("Built-in"); the warning pill
        /// leads with an amber dot (unsaved work); the accent pill names the active style in the
        /// link colour on the selection tint. Starts collapsed — <see cref="UpdateHeaderState"/>
        /// shows it.
        /// </summary>
        private Border MakeHeaderChip(string text, ChipKind kind, out TextBlock label)
        {
            label = new TextBlock
            {
                Text = text,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var highContrast = FormatStylesChrome.IsHighContrast;
            label.SetResourceReference(TextBlock.ForegroundProperty,
                highContrast || kind == ChipKind.Warning ? ThemeTokens.TextPrimary
                : kind == ChipKind.Accent ? ThemeTokens.TextLink
                : ThemeTokens.TextSecondary);

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (kind == ChipKind.Warning)
            {
                var dot = new TextBlock
                {
                    Text = "\u25CF",
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                };
                dot.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.StatusWarning);
                content.Children.Add(dot);
            }
            content.Children.Add(label);

            var chip = new Border
            {
                Child = content,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(Spacing.Sm + 1, 2, Spacing.Sm + 1, 3),
                Margin = new Thickness(Spacing.Sm, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            // (High Contrast: the tints are the system highlight, so the pill is outlined instead.)
            chip.SetResourceReference(Border.BackgroundProperty,
                highContrast ? ThemeTokens.SurfacePanel
                : kind == ChipKind.Accent ? ThemeTokens.SurfaceSelection : ThemeTokens.SurfaceHover);
            chip.SetResourceReference(Border.BorderBrushProperty,
                highContrast ? ThemeTokens.BorderDefault
                : kind == ChipKind.Accent ? ThemeTokens.SurfaceSelection : ThemeTokens.BorderSubtle);
            return chip;
        }

        /// <summary>
        /// Re-states the header from live view-model state: the style being edited, whether it is a
        /// read-only built-in, whether it has unsaved edits, and which style Format SQL will use.
        /// Cheap and idempotent — called from every place that can change any of those.
        /// </summary>
        private void UpdateHeaderState()
        {
            if (_headerSubject == null) return;

            var editing = _viewModel.LoadedProfileName;
            _headerSubject.Text = string.IsNullOrEmpty(editing) ? "No style selected" : editing!;

            if (_headerBuiltInChip != null)
            {
                _headerBuiltInChip.Visibility = _viewModel.IsSelectedBuiltIn && !string.IsNullOrEmpty(editing)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                // "modified" is the part that matters: it is the difference between looking at the
                // style as shipped and looking at your own edited version of it, and nothing else
                // on screen distinguishes them.
                if (_headerBuiltInChipText != null)
                    _headerBuiltInChipText.Text = _viewModel.IsSelectedCustomized
                        ? "Built-in \u00b7 modified"
                        : "Built-in";
            }

            // Built-ins are editable now, so a built-in with unsaved edits is an ordinary state and
            // the dirty chip belongs there too.
            if (_headerDirtyChip != null)
                _headerDirtyChip.Visibility = _viewModel.IsDirty
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // Read the active style from the list the user is looking at (IsActive is computed at
            // list-load time from Formatter.ActiveProfile) rather than re-reading config here, so
            // the header can never disagree with the ACTIVE badge in the list.
            var active = _viewModel.Profiles.FirstOrDefault(p => p.IsActive)?.Name;
            if (_headerActiveChip != null && _headerActiveChipText != null)
            {
                if (string.IsNullOrEmpty(active))
                {
                    _headerActiveChip.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _headerActiveChipText.Text = "Active: " + active;
                    _headerActiveChip.Visibility = Visibility.Visible;
                }
            }

            // "STYLES · 8" — the count is real information (did my new style land? did the engine
            // return anything at all?), which a static label cannot convey.
            if (_stylesHeader != null)
            {
                var count = _viewModel.Profiles.Count;
                _stylesHeader.Inlines.Clear();
                _stylesHeader.Inlines.Add(new System.Windows.Documents.Run("STYLES"));
                if (count > 0)
                {
                    var countRun = new System.Windows.Documents.Run("  " + count);
                    countRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, ThemeTokens.TextDisabled);
                    _stylesHeader.Inlines.Add(countRun);
                }
            }
        }

        /// <summary>A small all-caps, muted section divider ("STYLES", "STYLE OPTIONS", "LIVE PREVIEW").</summary>
        private TextBlock MakeSectionHeader(string text)
        {
            var t = new TextBlock
            {
                Text = text,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                Margin = new Thickness(Spacing.Xs, Spacing.Xs, Spacing.Xs, Spacing.Sm),
            };
            t.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            return t;
        }

        /// <summary>Small footer/secondary button (Import…, Export…).</summary>
        private Button MakeSecondaryButton(string content, Func<System.Threading.Tasks.Task> onClick)
        {
            var btn = new Button
            {
                Content = content,
                Padding = FooterButtonPadding,
                MinWidth = 76,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            ThemedButton.ApplySecondary(btn);
            // async-void click handler is the WPF event idiom; guarded so a faulted task can't crash the host.
            btn.Click += async (_, _) =>
            {
                try { await onClick(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: action '{Action}' failed", content); SetStatus(ex.Message); }
            };
            return btn;
        }

        /// <summary>
        /// Outlined accent call-to-action ("+ New style"): a real button — keyboard, screen readers
        /// and UI Automation reach it by name — on the shared themed template, with the accent for
        /// its border and the link colour for its text.
        /// </summary>
        private Button MakeAccentCtaButton(string content, Func<System.Threading.Tasks.Task> onClick)
        {
            var btn = new Button
            {
                Content = content,
                Padding = FooterButtonPadding,
                Margin = new Thickness(0, Spacing.Sm, 0, 0),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                FontWeight = Typography.WeightSemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            ThemedButton.ApplySecondary(btn);
            btn.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.AccentPrimary);
            btn.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextLink);
            btn.Click += async (_, _) =>
            {
                try { await onClick(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: action '{Action}' failed", content); SetStatus(ex.Message); }
            };
            return btn;
        }

        /// <summary>The row's ⋮ glyph opens the shared style context menu against its own row.</summary>
        private void OnRowMenuGlyphClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is not FrameworkElement fe || fe.DataContext is not StyleListItem item || _styleList == null) return;
            if (!ReferenceEquals(_styleList.SelectedItem, item))
                _styleList.SelectedItem = item; // acts on its own row — selecting loads the style, same as a row click
            if (_styleList.ContextMenu is { } menu)
            {
                menu.PlacementTarget = fe;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }

        /// <summary>The currently-selected style name, or null when nothing is selected.</summary>
        private string? SelectedStyle()
            => (_styleList?.SelectedItem as StyleListItem)?.Name ?? _viewModel.SelectedProfileName;

        /// <summary>
        /// Spec 033 (T016) — guarded load-on-select. Delegates to
        /// <see cref="FormatStylesEditorViewModel.SelectProfileAsync"/> (dirty prompt +
        /// ProfileGet + working-value overlay); on cancel/failure the previous visual
        /// selection is restored so the list never lies about what is loaded.
        /// </summary>
        private async System.Threading.Tasks.Task OnStyleSelectionChangedAsync()
        {
            if (_suppressSelectionChanged || _styleList?.SelectedItem is not StyleListItem item) return;

            // "Set as active" tracks the selection: pointless on the style that is already active.
            SyncSetActiveButton(item.Name, item.IsActive);

            var previous = _viewModel.LoadedProfileName;
            bool ok;
            try
            {
                ok = await _viewModel.SelectProfileAsync(item.Name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: style selection failed");
                ok = false;
            }

            if (!ok)
            {
                SetStatus(_viewModel.LastError ?? $"Could not load '{item.Name}'.");
                RestoreListSelection(previous);
                return;
            }

            // (Save button + built-in visuals sync via the IsDirty/IsSelectedBuiltIn
            // PropertyChanged handler — no direct calls needed here.)
            RefreshVisibleSettingControls();
            UpdateHeaderState();   // the header names the style now being edited
            // Explicitly re-synced (not left to the INPC handler): the flags only raise
            // PropertyChanged when the VALUE changes, so selecting one built-in after another would
            // otherwise leave the Save tooltip naming the previously-selected style.
            UpdateSaveButtonState();
            SetStatus(
                _viewModel.IsSelectedCustomized
                    ? $"Loaded '{item.Name}' — your edited version of the built-in style."
                : _viewModel.IsSelectedBuiltIn
                    ? $"Loaded '{item.Name}' — editing it saves your own copy; the original is kept."
                    : $"Loaded '{item.Name}'.");
        }

        /// <summary>"Set as active style" is disabled, with a reason, on the style that is already active.</summary>
        private void SyncSetActiveButton(string name, bool isActive)
        {
            if (_setActiveButton == null) return;
            _setActiveButton.IsEnabled = !isActive;
            _setActiveButton.ToolTip = isActive
                ? $"'{name}' is already the active style"
                : $"Make '{name}' the style Format SQL uses";
        }

        /// <summary>Re-points the list selection at <paramref name="name"/> (or clears it) without re-triggering the load.</summary>
        private void RestoreListSelection(string? name)
        {
            if (_styleList == null) return;
            _suppressSelectionChanged = true;
            try
            {
                _styleList.SelectedItem = name == null
                    ? null
                    : _viewModel.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _suppressSelectionChanged = false;
            }
        }

        /// <summary>Re-renders the current group's form so it shows the freshly-loaded style's values.</summary>
        private void RefreshVisibleSettingControls()
        {
            if (_currentGroup != null) UpdateRightForGroup(_currentGroup, _currentGroupCategory);
        }

        private void UpdateSaveButtonState()
        {
            var nothingLoaded = string.IsNullOrEmpty(_viewModel.LoadedProfileName);

            if (_saveBtn != null)
            {
                _saveBtn.IsEnabled = _viewModel.IsDirty && !_viewModel.IsSelectedReadOnly;
                // A disabled button with no explanation reads as broken, so name the actual reason.
                // Saving a built-in says where the change goes: it writes your own copy rather than
                // altering the shipped file, which is why it can always be undone.
                _saveBtn.ToolTip =
                    nothingLoaded ? "Select a style first"
                    : _viewModel.IsSelectedReadOnly ? FormatStylesEditorViewModel.TeamReadOnlyText(_viewModel.LoadedProfileName!)
                    : !_viewModel.IsDirty ? "No changes to save"
                    : _viewModel.IsSelectedBuiltIn && !_viewModel.IsSelectedCustomized
                        ? $"Save your own copy of the built-in '{_viewModel.LoadedProfileName}' (the original is kept)"
                        : $"Save your changes to '{_viewModel.LoadedProfileName}'";
            }

            // Revert undoes UNSAVED edits, so it appears exactly when there are some.
            if (_revertButton != null)
                _revertButton.Visibility = _viewModel.IsDirty && !nothingLoaded
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // Reset undoes SAVED edits, so it appears only once an override exists.
            if (_resetButton != null)
                _resetButton.Visibility = _viewModel.IsSelectedCustomized && !nothingLoaded
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        /// <summary>
        /// Spec 040 (T187, STY-10) — shows "Team styles unavailable — ‹folder› can't be reached"
        /// under the list while the engine reports the team style folder unreachable.
        /// </summary>
        private void UpdateTeamUnavailableRow()
        {
            if (_teamUnavailableRow == null) return;
            var show = _viewModel.TeamFolderUnavailable;
            _teamUnavailableRow.Text = show
                ? FormatStylesEditorViewModel.TeamFolderUnavailableText(
                    string.IsNullOrWhiteSpace(_viewModel.TeamStyleFolder) ? "the team style folder" : _viewModel.TeamStyleFolder)
                : string.Empty;
            _teamUnavailableRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The hint over a built-in style's options.</summary>
        private const string BuiltInHint = "Built-in style: your edits are saved as your own copy, and the original is kept.";

        private void UpdateReadOnlyState()
        {
            // The settings form is disabled only for a read-only team style (spec 040, STY-10) —
            // built-ins are editable (an edit saves the user's own copy), and disabling them is what
            // made the editor look broken on a fresh install where every style is built-in.
            if (_settingControlsHost != null)
                _settingControlsHost.IsEnabled = !_viewModel.IsSelectedReadOnly;

            if (_builtInHint != null)
            {
                var loaded = !string.IsNullOrEmpty(_viewModel.LoadedProfileName);
                _builtInHint.Visibility = loaded && (_viewModel.IsSelectedBuiltIn || _viewModel.IsSelectedClassic || _viewModel.IsSelectedReadOnly)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (_builtInHintText != null)
                {
                    var text = _viewModel.IsSelectedCustomized
                        ? "Your edited copy of a built-in style. Reset to built-in restores the original."
                        : _viewModel.IsSelectedBuiltIn
                            ? BuiltInHint
                            : string.Empty;
                    if (_viewModel.IsSelectedClassic && !_viewModel.IsSelectedReadOnly)
                        text = (text.Length > 0 ? text + " " : string.Empty)
                               + "Written in AKML's own model and shown in SQL Prompt's terms; saving makes it a SQL Prompt style, formatted as the preview shows.";
                    if (_viewModel.IsSelectedReadOnly)   // spec 040 (T187)
                        text = "Read-only team style — its folder can't be written to. Copy it to edit a style of your own.";
                    _builtInHintText.Text = text;
                }
            }
        }

        /// <summary>The ONE Save / Discard / Cancel prompt (style switch + window close share it).</summary>
        private StyleSwitchDecision PromptSaveDecision(string message)
        {
            if (SaveDecisionOverride != null) return SaveDecisionOverride(message);

            var result = MessageBox.Show(
                this,
                message,
                AkmlSql.Core.Config.WindowTitles.For("Format styles"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            return result switch
            {
                MessageBoxResult.Yes => StyleSwitchDecision.Save,
                MessageBoxResult.No => StyleSwitchDecision.Discard,
                _ => StyleSwitchDecision.Cancel,
            };
        }

        private System.Threading.Tasks.Task<StyleSwitchDecision> PromptStyleSwitchDecisionAsync() =>
            System.Threading.Tasks.Task.FromResult(
                PromptSaveDecision($"Save changes to '{_viewModel.LoadedProfileName ?? "this style"}'?"));

        /// <summary>Spec 040 (T080) — "Edit sample" swaps the formatted preview for the raw, editable sample.</summary>
        private void ShowSampleEditor(bool editing)
        {
            if (_previewTextBox != null) _previewTextBox.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (_previewView != null) _previewView.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Persists the in-box sample text if the Edit-sample toggle is active.</summary>
        private void CommitSampleEdit()
        {
            if (_previewTextBox != null)
                _viewModel.PreviewSample = _previewTextBox.Text; // setter persists atomically + queues one preview
        }

        /// <summary>Spec 033 — closing over unsaved edits prompts; Save defers the close until the write lands.</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (EditingSample) CommitSampleEdit(); // sample edits survive close without per-keystroke writes

            if (!_closeConfirmed && _viewModel.IsDirty)
            {
                switch (PromptSaveDecision($"Save changes to '{_viewModel.LoadedProfileName ?? "this style"}' before closing?"))
                {
                    case StyleSwitchDecision.Cancel:
                        e.Cancel = true;
                        return;
                    case StyleSwitchDecision.Save:
                        e.Cancel = true;
                        _ = SaveThenCloseAsync();
                        return;
                }
            }
            base.OnClosing(e);
        }

        private async System.Threading.Tasks.Task SaveThenCloseAsync()
        {
            try
            {
                if (await _viewModel.SaveAsync())
                {
                    _closeConfirmed = true;
                    Close();
                }
                else
                {
                    SetStatus(_viewModel.LastError ?? "Save failed — the window stays open.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: save-then-close failed");
                SetStatus(ex.Message);
            }
        }

        private MenuItem MakeMenuItem(string header, Func<System.Threading.Tasks.Task> onClick)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) =>
            {
                try { await onClick(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: menu action '{Action}' failed", header); SetStatus(ex.Message); }
            };
            return item;
        }

        private async System.Threading.Tasks.Task OnNewStyleAsync()
        {
            // Spec 033 (T035) — New Style… with a chosen name + based-on style.
            var candidates = _viewModel.Profiles.Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (candidates.Count == 0) candidates.Add("Default");
            var (accepted, name, basedOn) = StyleNameDialog.ShowNewStyle(this, candidates, SelectedStyle() ?? "Default", ExistingStyleNames());
            if (!accepted) return;

            var created = await _viewModel.CreateStyleAsync(name, basedOn);
            AfterCreate(created, $"Created '{created}' based on '{basedOn}'.");
        }

        private async System.Threading.Tasks.Task OnRenameStyleAsync()
        {
            var current = SelectedStyle();
            if (string.IsNullOrEmpty(current)) { SetStatus("Select a style to rename."); return; }
            var item = _viewModel.Profiles.FirstOrDefault(p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase));
            if (item?.IsShipped == true) { SetStatus("Built-in styles cannot be renamed — use Copy to make one you can name."); return; }
            if (item?.IsReadOnly == true) { SetStatus(FormatStylesEditorViewModel.TeamReadOnlyText(current!)); return; }   // spec 040 (T187)

            string? newName;
            if (RenameNameOverride != null)
            {
                newName = RenameNameOverride(current!, ExistingStyleNames());
            }
            else
            {
                var (accepted, typed) = StyleNameDialog.ShowRename(this, current!, ExistingStyleNames());
                newName = accepted ? typed : null;
            }
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, current, StringComparison.Ordinal)) return;

            var wasActive = item?.IsActive == true;
            var finalName = await _viewModel.RenameSelectedAsync(newName!);
            if (finalName == null)
            {
                SetStatus(_viewModel.LastError ?? "Rename failed.");
                return;
            }

            RestoreListSelection(finalName);
            if (wasActive) UpdateStatusBarActiveStyle(finalName);
            SetStatus($"Renamed '{current}' to '{finalName}'.");
        }

        private async System.Threading.Tasks.Task OnDeleteStyleAsync()
        {
            var current = SelectedStyle();
            if (string.IsNullOrEmpty(current)) { SetStatus("Select a style to delete."); return; }

            // Spec 040 (T102): say why, before asking "Delete?", for a style that cannot be deleted.
            var item = _viewModel.Profiles.FirstOrDefault(p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase));
            if (item?.IsShipped == true)
            {
                SetStatus("Built-in styles cannot be deleted. Use Copy to make one of your own.");
                return;
            }
            if (item?.IsReadOnly == true)   // spec 040 (T187): a read-only team style
            {
                SetStatus(FormatStylesEditorViewModel.TeamReadOnlyText(current!));
                return;
            }
            if (item?.IsActive == true)
            {
                SetStatus($"'{current}' is the active style. Make another style active before deleting it.");
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"Delete style '{current}'? This cannot be undone.",
                AkmlSql.Core.Config.WindowTitles.For("Format styles"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            SetStatus(await _viewModel.DeleteSelectedAsync()
                ? $"Deleted '{current}'."
                : _viewModel.LastError ?? "Delete failed.");
        }

        /// <summary>
        /// Saves the loaded style. The first save of a shipped style creates the override that
        /// shadows it; the view model marks its list item "Built-in · modified" (the ⋮ menu's Reset
        /// and <see cref="OnResetStyleAsync"/> read that item), whichever way the save came about.
        /// </summary>
        private async System.Threading.Tasks.Task SaveSelectedStyleAsync()
        {
            var name = _viewModel.LoadedProfileName;
            if (!await _viewModel.SaveAsync())
            {
                SetStatus(_viewModel.LastError ?? "Save failed.");
                return;
            }
            UpdateHeaderState();
            SetStatus($"Saved '{name}'.");
        }

        /// <summary>
        /// "Reset to built-in" — discards SAVED edits to a shipped style and restores the original.
        /// Confirmed first: unlike Revert, this cannot be undone from inside the editor.
        /// </summary>
        private async System.Threading.Tasks.Task OnResetStyleAsync()
        {
            var current = SelectedStyle() ?? _viewModel.LoadedProfileName;
            if (string.IsNullOrEmpty(current)) { SetStatus("Select a style to reset."); return; }

            var item = _viewModel.Profiles.FirstOrDefault(
                p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase));
            if (item?.IsShipped != true)
            {
                SetStatus($"'{current}' is your own style, so there is no built-in version to reset to.");
                return;
            }
            if (item.IsCustomized != true)
            {
                SetStatus($"'{current}' is already the built-in style — nothing to reset.");
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"Reset '{current}' to the built-in style?\n\nYour saved changes to it will be discarded. This cannot be undone.",
                AkmlSql.Core.Config.WindowTitles.For("Format styles"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            if (!await _viewModel.ResetToBuiltInAsync())
            {
                SetStatus(_viewModel.LastError ?? "Reset failed.");
                return;
            }

            // The list badge goes from "Built-in · modified" back to "Built-in", so the list has to
            // be rebuilt; RestoreListSelection suppresses SelectionChanged, so the style stays
            // loaded and is not re-fetched.
            await _viewModel.RefreshProfilesAsync();
            RestoreListSelection(current);
            RefreshVisibleSettingControls();
            UpdateHeaderState();
            UpdateSaveButtonState();
            SetStatus($"Reset '{current}' to the built-in style.");
        }

        private async System.Threading.Tasks.Task OnCopyStyleAsync()
        {
            var source = SelectedStyle();
            if (string.IsNullOrEmpty(source)) { SetStatus("Select a style to copy."); return; }
            // Spec 040 (T101): Copy asks for the copy's name, suggesting "‹name› copy".
            var name = StyleNameDialog.ShowCopyStyle(this, source!, ExistingStyleNames(), _viewModel.UniqueName($"{source} copy"));
            if (string.IsNullOrWhiteSpace(name)) return;
            var created = await _viewModel.CopyProfileAsync(source!, name!);
            AfterCreate(created, $"Copied '{source}' to '{created}'.");
        }

        private System.Collections.Generic.IReadOnlyCollection<string> ExistingStyleNames() =>
            _viewModel.Profiles.Select(p => p.Name).ToList();

        /// <summary>
        /// Spec 040 (T102, STY-07) — the window's keys. Anywhere: Ctrl+S saves (when there is
        /// something to save), Ctrl+F goes to the option search. On the style list: F2 renames,
        /// Delete deletes, Enter makes the style active. Returns true when the key was handled.
        /// </summary>
        internal bool HandleKey(Key key, ModifierKeys modifiers, bool listFocused)
        {
            if (modifiers == ModifierKeys.Control && key == Key.S)
            {
                if (_saveBtn?.IsEnabled == true) _ = RunKeyActionAsync(SaveSelectedStyleAsync, "Save");
                return true;
            }
            if (modifiers == ModifierKeys.Control && key == Key.F)
            {
                FocusSearch();
                return true;
            }
            if (!listFocused || modifiers != ModifierKeys.None) return false;

            switch (key)
            {
                case Key.F2:
                    _ = RunKeyActionAsync(OnRenameStyleAsync, "Rename");
                    return true;
                case Key.Delete:
                    _ = RunKeyActionAsync(OnDeleteStyleAsync, "Delete");
                    return true;
                case Key.Enter:
                    _ = RunKeyActionAsync(OnSetActiveAsync, "Set active");
                    return true;
                default:
                    return false;
            }
        }

        private async System.Threading.Tasks.Task RunKeyActionAsync(Func<System.Threading.Tasks.Task> action, string name)
        {
            try { await action(); }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: key action '{Action}' failed", name);
                SetStatus(ex.Message);
            }
        }

        private async System.Threading.Tasks.Task OnSetActiveAsync()
        {
            var name = SelectedStyle();
            if (string.IsNullOrEmpty(name)) { SetStatus("Select a style to make active."); return; }
            if (_viewModel.SetActiveProfile(name!))
            {
                SetStatus($"'{name}' is now the active style — Format SQL will use it.");
                UpdateStatusBarActiveStyle(name!);
                // Spec 033 (T036) — the ACTIVE badge is computed at list-load time; refresh so it moves.
                await _viewModel.RefreshProfilesAsync();
                RestoreListSelection(name);

                // RestoreListSelection suppresses SelectionChanged, so sync the button here or it
                // would stay enabled on the style that just became active.
                SyncSetActiveButton(name!, isActive: true);

                UpdateHeaderState();   // "Active: <name>" follows immediately
            }
            else
            {
                SetStatus(_viewModel.LastError ?? "Could not set active style.");
            }
        }

        internal async System.Threading.Tasks.Task OnExportAsync()
        {
            var name = SelectedStyle();
            if (string.IsNullOrEmpty(name)) { SetStatus("Select a style to export."); return; }

            // Spec 040 (STY-03, FR-023): Export writes the SAVED style, so unsaved edits to it are
            // offered for saving first rather than silently left out of the file.
            if (_viewModel.IsDirty && string.Equals(name, _viewModel.LoadedProfileName, StringComparison.OrdinalIgnoreCase))
            {
                switch (PromptSaveDecision($"Save changes to '{name}' before exporting?"))
                {
                    case StyleSwitchDecision.Cancel:
                        SetStatus("Export cancelled.");
                        return;
                    case StyleSwitchDecision.Save:
                        await SaveSelectedStyleAsync();
                        if (_viewModel.IsDirty) return; // the save failed; its error is in the status bar
                        break;
                }
            }

            var fileName = name + (_viewModel.IsSqlPromptModel ? ".json" : ".sqlpromptstylev2");
            var path = ExportFileOverride != null ? ExportFileOverride(fileName) : PickExportFile(fileName);
            if (path == null) return;
            if (await _viewModel.ExportProfileAsync(name!, path))
                SetStatus($"Exported '{name}'");
            else
                SetStatus(_viewModel.LastError ?? "Export failed.");
        }

        private string? PickExportFile(string fileName)
        {
            // SQL Prompt 10.5+ reads and writes one .json per style; the engine writes the style's
            // SQL Prompt document there. .sqlpromptstylev2 stays available for older SQL Prompts.
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export formatting style",
                FileName = fileName,
                Filter = _viewModel.IsSqlPromptModel
                    ? "SQL Prompt style (*.json)|*.json|SQL Prompt 9 style (*.sqlpromptstylev2)|*.sqlpromptstylev2|All files (*.*)|*.*"
                    : "SQL Prompt style (*.sqlpromptstylev2)|*.sqlpromptstylev2|All files (*.*)|*.*",
                DefaultExt = _viewModel.IsSqlPromptModel ? ".json" : ".sqlpromptstylev2",
                OverwritePrompt = true,
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private string? PickImportFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import SQL Prompt style",
                Filter = "SQL Prompt style (*.json;*.sqlpromptstylev2)|*.json;*.sqlpromptstylev2|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        /// <summary>
        /// Spec 031 FR-010/FR-011/FR-012 — imports a SQL Prompt style file (JSON or legacy XML)
        /// via <see cref="FormatStylesEditorViewModel.ImportProfileAsync"/>, selects + activates
        /// the resulting style, and shows a per-option summary dialog. Spec 040 (STY-03): unsaved
        /// edits are settled before the file is picked, a taken name is never overwritten, and the
        /// list, header and "Set as active style" show the new active style at once (FR-022).
        /// </summary>
        internal async System.Threading.Tasks.Task OnImportAsync()
        {
            // The import selects the new style, so settle unsaved edits first — asked here, before
            // the file picker, instead of halfway through the import.
            if (_viewModel.IsDirty)
            {
                var decision = _viewModel.DirtyDecisionHandler != null
                    ? await _viewModel.DirtyDecisionHandler()
                    : StyleSwitchDecision.Discard;
                if (decision == StyleSwitchDecision.Cancel) { SetStatus("Import cancelled."); return; }
                if (decision == StyleSwitchDecision.Save)
                {
                    await SaveSelectedStyleAsync();
                    if (_viewModel.IsDirty) return; // the save failed; its error is in the status bar
                }
                else
                {
                    _viewModel.RevertChanges();
                    RefreshVisibleSettingControls();
                }
            }

            var file = ImportFileOverride != null ? ImportFileOverride() : PickImportFile();
            if (file == null) return;

            var stem = System.IO.Path.GetFileNameWithoutExtension(file);
            var peekedName = TryPeekStyleName(file, out var kind);

            // Three-way naming rule (the engine falls back to a hardcoded name whenever it
            // can't derive one, so consecutive fallback imports would silently overwrite each
            // other while the collision check sees nothing):
            //  - JSON WITH metadata.name  → no targetName; the internal metadata.name must win
            //    (the Task 8 handler overrides metadata.name with TargetProfileName when
            //    present, which would break JSON naming).
            //  - JSON WITHOUT metadata.name → targetName = file stem; otherwise the engine
            //    fallback-names it "Imported style" and every unnamed JSON import collides.
            //  - XML (never has an internal name) → targetName = file stem; otherwise
            //    SqlPromptImporter hardcodes "Imported from SQL Prompt" with the same problem.
            string? targetName = kind switch
            {
                StyleFileKind.Xml => stem,
                StyleFileKind.Json when string.IsNullOrWhiteSpace(peekedName) => stem,
                _ => null,
            };

            // FR-008 — collision check against the client-side list before sending.
            // JSON: the peeked metadata.name (the engine derives the profile name from it),
            // falling back to the stem exactly when the stem is what we pass as targetName.
            // XML: the stem we just chose as the target name. Unrecognized/malformed content:
            // skip the check — the engine rejects it with a clear error, nothing saved.
            // Spec 040 (STY-03): a taken name — a built-in or one of your own styles — is never
            // overwritten by an import; the user picks another, "‹name› (imported)" suggested.
            string? collisionName = kind == StyleFileKind.Unknown ? null : (peekedName ?? stem);
            var existingNames = _viewModel.Profiles.Select(p => p.Name).ToList();
            if (collisionName != null && existingNames.Contains(collisionName, StringComparer.OrdinalIgnoreCase))
            {
                var suggested = collisionName + " (imported)";
                for (var n = 2; existingNames.Contains(suggested, StringComparer.OrdinalIgnoreCase); n++)
                    suggested = $"{collisionName} (imported {n})";

                var chosen = ImportNameOverride != null
                    ? ImportNameOverride(suggested, existingNames)
                    : StyleNameDialog.ShowImportName(this, collisionName, suggested, existingNames);
                if (string.IsNullOrWhiteSpace(chosen))
                {
                    SetStatus("Import cancelled.");
                    return;
                }
                targetName = chosen!.Trim();
            }

            var response = await _viewModel.ImportProfileAsync(file, targetName);
            if (response == null || !response.Success || response.ProfileName == null)
            {
                SetStatus(_viewModel.LastError ?? "Import failed.");
                return;
            }

            // FR-011 — an imported style becomes the active one. Then the same refresh as "Set as
            // active" (spec 040 FR-022): the list's ACTIVE pill, the header chip and the button
            // show it at once, without reopening the window.
            var imported = response.ProfileName;
            var activated = _viewModel.SetActiveProfile(imported);
            await _viewModel.RefreshProfilesAsync();
            AfterCreate(imported, BuildImportSummary(response)); // selects the style, which loads it
            var importedItem = _viewModel.Profiles.FirstOrDefault(
                p => string.Equals(p.Name, imported, StringComparison.OrdinalIgnoreCase));
            SyncSetActiveButton(imported, importedItem?.IsActive == true);
            UpdateHeaderState();
            if (activated)
                UpdateStatusBarActiveStyle(imported);
            else
                SetStatus(_viewModel.LastError ?? "Imported, but could not set active style.");
            ShowImportSummaryDialog(response); // FR-012 — the import itself succeeded
        }

        private static string BuildImportSummary(ProfileImportResponse r)
        {
            var reports = r.OptionReports ?? Array.Empty<ProfileImportOptionReport>();
            int mapped = reports.Count(x => x.Status == "mapped");
            int pending = reports.Count(x => x.Status == "mapped-pending-render");
            int unsupported = reports.Count(x => x.Status == "unsupported");
            int unknown = reports.Count(x => x.Status == "unknown");
            return $"Imported '{r.ProfileName}' — {mapped} mapped, {pending} pending render, {unsupported} unsupported, {unknown} unknown";
        }

        /// <summary>Sniffed content kind of a style file, from its first non-whitespace char.</summary>
        private enum StyleFileKind
        {
            /// <summary>Neither JSON nor XML (or unreadable/oversized/malformed) — the engine rejects it.</summary>
            Unknown,
            /// <summary>Modern Redgate JSON style (<c>{</c>).</summary>
            Json,
            /// <summary>Legacy XML style (<c>&lt;</c>) — has no internal name; caller supplies one.</summary>
            Xml,
        }

        /// <summary>
        /// Best-effort client-side peek at a SQL Prompt JSON style file's <c>metadata.name</c> —
        /// mirrors <c>RedgateJsonStyleImporter</c>'s name derivation (engine-side, spec 031 Task 8)
        /// closely enough to predict the resulting profile name before sending the import over
        /// IPC. <paramref name="kind"/> reports the sniffed content kind (same first-char sniff
        /// the engine's HandleProfileImport uses) so the caller can name legacy XML imports and
        /// skip the overwrite confirmation for content the engine will reject anyway. Returns
        /// null for anything that isn't JSON with a <c>metadata.name</c> string. Never throws;
        /// a real parse failure is surfaced by
        /// <see cref="FormatStylesEditorViewModel.ImportProfileAsync"/> once the file is sent
        /// (<paramref name="kind"/> resets to <see cref="StyleFileKind.Unknown"/> on failure so
        /// malformed content never triggers a pointless confirmation).
        /// </summary>
        private static string? TryPeekStyleName(string filePath, out StyleFileKind kind)
        {
            kind = StyleFileKind.Unknown;
            try
            {
                var bytes = System.IO.File.ReadAllBytes(filePath);
                if (bytes.Length == 0 || bytes.Length > 1024 * 1024) return null;

                var text = System.Text.Encoding.UTF8.GetString(bytes)
                    .TrimStart((char)0xFEFF, ' ', '\t', '\r', '\n');
                if (text.Length == 0) return null;
                if (text[0] == '<') { kind = StyleFileKind.Xml; return null; }
                if (text[0] != '{') return null; // unrecognized — engine rejects with a clear error

                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                kind = StyleFileKind.Json;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, "metadata", StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.Value.ValueKind != JsonValueKind.Object) return null;

                    foreach (var metaProp in prop.Value.EnumerateObject())
                    {
                        if (!string.Equals(metaProp.Name, "name", StringComparison.OrdinalIgnoreCase)) continue;
                        if (metaProp.Value.ValueKind != JsonValueKind.String) return null;
                        var name = metaProp.Value.GetString();
                        return string.IsNullOrWhiteSpace(name) ? null : name;
                    }
                    return null;
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FormatStylesEditor: TryPeekStyleName failed for {Path}", filePath);
                kind = StyleFileKind.Unknown; // malformed — engine will reject; no confirmation
                return null;
            }
        }

        /// <summary>
        /// Spec 031 FR-012 — shows the per-option import summary. Owner is set explicitly to
        /// this window (not via the usual DTE-HWND pattern) because this dialog is nested inside
        /// an already-open AKML modal: WPF only disables/centres-over the actual <see
        /// cref="Window.Owner"/>, and the DTE main window is one level too far out for that.
        /// </summary>
        private void ShowImportSummaryDialog(ProfileImportResponse response)
        {
            if (ImportSummaryOverride != null) { ImportSummaryOverride(response); return; }

            var dialog = new ImportSummaryDialog(
                response.ProfileName ?? "(unknown)",
                BuildImportSummary(response),
                response.OptionReports)
            {
                Owner = this,
            };
            dialog.ShowDialog();
        }

        /// <summary>Selects the newly created style in the list + reports status.</summary>
        private void AfterCreate(string? created, string okMessage)
        {
            if (string.IsNullOrEmpty(created)) { SetStatus(_viewModel.LastError ?? "Operation failed."); return; }
            if (_styleList != null)
            {
                foreach (var obj in _styleList.Items)
                {
                    if (obj is StyleListItem item && string.Equals(item.Name, created, StringComparison.OrdinalIgnoreCase))
                    {
                        _styleList.SelectedItem = item;
                        _styleList.ScrollIntoView(item);
                        break;
                    }
                }
            }
            SetStatus(okMessage);
        }

        private void SetStatus(string text)
        {
            if (_statusText != null) _statusText.Text = text;
        }

        private static void UpdateStatusBarActiveStyle(string name)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var statusBar = (IVsStatusbar?)Package.GetGlobalService(typeof(SVsStatusbar));
                if (statusBar != null) StatusBarManager.SetActiveProfile(statusBar, name);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FormatStylesEditor: status-bar active-style update failed (best-effort)");
            }
        }

        private DataTemplate BuildStyleListItemTemplate()
        {
            // Row:  Name ................ [Modified] [ACTIVE]  ⋮
            // The badges sit in one right-hand column, so every row's ACTIVE pill and ⋮ line up.
            // "Built-in" / "Team" is not repeated on each row — the section header above says it;
            // only what the section can't tell you gets a badge (Modified, Read-only). The row's
            // tooltip still gives the full kind.
            // Under High Contrast the selected row is the system highlight: the badges and ⋮ then
            // take the row's own text colour, and ACTIVE is outlined rather than filled.
            var highContrast = FormatStylesChrome.IsHighContrast;
            var boolToVis = new System.Windows.Controls.BooleanToVisibilityConverter();

            var template = new DataTemplate(typeof(StyleListItem));

            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MinHeightProperty, 22.0);
            grid.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(StyleListItem.Kind)));
            var nameCol = new FrameworkElementFactory(typeof(ColumnDefinition));
            nameCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var badgeCol = new FrameworkElementFactory(typeof(ColumnDefinition));
            badgeCol.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var menuCol = new FrameworkElementFactory(typeof(ColumnDefinition));
            menuCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(20));
            grid.AppendChild(nameCol);
            grid.AppendChild(badgeCol);
            grid.AppendChild(menuCol);

            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(StyleListItem.Name)));
            name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            grid.AppendChild(name);

            var badges = new FrameworkElementFactory(typeof(StackPanel));
            badges.SetValue(Grid.ColumnProperty, 1);
            badges.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            badges.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            // "Modified" / "Read-only": an outlined, muted pill.
            var kindBadge = new FrameworkElementFactory(typeof(Border));
            kindBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            kindBadge.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            kindBadge.SetValue(Border.PaddingProperty, new Thickness(6, 0, 6, 1));
            kindBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            kindBadge.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            kindBadge.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(StyleListItem.Kind))
            {
                Converter = KindBadgeConverter.Instance,
                ConverterParameter = KindBadgeConverter.VisibilityParameter,
            });
            var kindText = new FrameworkElementFactory(typeof(TextBlock));
            kindText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(StyleListItem.Kind)) { Converter = KindBadgeConverter.Instance });
            kindText.SetValue(TextBlock.FontSizeProperty, 10.0);
            if (!highContrast) kindText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            kindBadge.AppendChild(kindText);
            badges.AppendChild(kindBadge);

            // The active style reads as an explicit, filled "ACTIVE" pill: a bare check glyph was
            // easy to miss and gave no hint that it means "Format SQL uses this style" — the
            // confusion behind "selecting a style doesn't mark it" (selecting only highlights a
            // row; activating is a separate action).
            var activeBadge = new FrameworkElementFactory(typeof(Border));
            activeBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            activeBadge.SetValue(Border.PaddingProperty, new Thickness(6, 1, 6, 1));
            activeBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            activeBadge.SetValue(FrameworkElement.ToolTipProperty, "Format SQL uses this style");
            if (highContrast)
            {
                activeBadge.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                activeBadge.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            }
            else
            {
                activeBadge.SetResourceReference(Border.BackgroundProperty, ThemeTokens.AccentPrimary);
            }
            activeBadge.SetBinding(UIElement.VisibilityProperty,
                new System.Windows.Data.Binding(nameof(StyleListItem.IsActive)) { Converter = boolToVis });
            var activeText = new FrameworkElementFactory(typeof(TextBlock));
            activeText.SetValue(TextBlock.TextProperty, "ACTIVE");
            activeText.SetValue(TextBlock.FontSizeProperty, 9.5);
            activeText.SetValue(TextBlock.FontWeightProperty, Typography.WeightSemiBold);
            if (!highContrast) activeText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextOnAccent);
            activeBadge.AppendChild(activeText);
            badges.AppendChild(activeBadge);
            grid.AppendChild(badges);

            // ⋮ opens the shared per-style context menu against its own row.
            var menuGlyph = new FrameworkElementFactory(typeof(TextBlock));
            menuGlyph.SetValue(Grid.ColumnProperty, 2);
            menuGlyph.SetValue(TextBlock.TextProperty, "⋮");
            menuGlyph.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            menuGlyph.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            menuGlyph.SetValue(TextBlock.FontSizeProperty, (double)Typography.H4);
            menuGlyph.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
            menuGlyph.SetValue(FrameworkElement.ToolTipProperty, "Style actions");
            if (!highContrast) menuGlyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            // Spec 040 (T185): screen readers announce the glyph by name, not as "⋮".
            menuGlyph.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Style actions");
            menuGlyph.AddHandler(UIElement.MouseLeftButtonUpEvent,
                new System.Windows.Input.MouseButtonEventHandler(OnRowMenuGlyphClick));
            grid.AppendChild(menuGlyph);

            template.VisualTree = grid;
            return template;
        }

        /// <summary>
        /// The style list's kind badge: only what the row's section header doesn't already say —
        /// "Modified" for an edited built-in, "Read-only" for a team style that can't be written.
        /// With <see cref="VisibilityParameter"/> it answers whether the badge shows at all.
        /// </summary>
        private sealed class KindBadgeConverter : System.Windows.Data.IValueConverter
        {
            internal static readonly KindBadgeConverter Instance = new KindBadgeConverter();
            internal const string VisibilityParameter = "visibility";

            internal static string Badge(string? kind) =>
                kind == null ? string.Empty
                : kind.EndsWith("modified", StringComparison.Ordinal) ? "Modified"
                : kind.EndsWith("read-only", StringComparison.Ordinal) ? "Read-only"
                : string.Empty;

            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var badge = Badge(value as string);
                return Equals(parameter, VisibilityParameter)
                    ? (badge.Length == 0 ? Visibility.Collapsed : Visibility.Visible)
                    : badge;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => throw new NotSupportedException();
        }

        /// <summary>Upper-cases the style-list section label ("Your styles" → "YOUR STYLES").</summary>
        private sealed class UpperCaseConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => (value as string)?.ToUpperInvariant() ?? value;
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => throw new NotSupportedException();
        }

        // -----------------------------------------------------------------
        // Middle panel — settings tree built from schema JSON
        // -----------------------------------------------------------------
        private FrameworkElement BuildMiddlePanel()
        {
            var panel = new Grid { Margin = new Thickness(Spacing.Sm) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // What the "● N" beside a page means — users read it as "N options on the page".
            var legend = new TextBlock
            {
                Text = "● N  options that differ from SQL Prompt's defaults",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Spacing.Xs, Spacing.Sm, Spacing.Xs, 0),
            };
            legend.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            Grid.SetRow(legend, 3);
            panel.Children.Add(legend);

            var header = MakeSectionHeader("STYLE OPTIONS");
            Grid.SetRow(header, 0);
            panel.Children.Add(header);

            var search = BuildSearchBox();
            Grid.SetRow(search, 1);
            panel.Children.Add(search);

            _settingsTree = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            _settingsTree.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            ScrollViewer.SetHorizontalScrollBarVisibility(_settingsTree, ScrollBarVisibility.Disabled);

            // Chevrons on the categories; the selected page gets a soft rounded tint and an accent
            // bar instead of the stock solid highlight block. Implicit, so it reaches every depth.
            _settingsTree.Resources[typeof(TreeViewItem)] = FormatStylesChrome.TreeItemStyle();
            Grid.SetRow(_settingsTree, 2);
            panel.Children.Add(_settingsTree);

            return MakePaneCard(2, panel);
        }

        /// <summary>
        /// Spec 040 (T097, STY-04, FR-030) — "Search for options…": filters the page tree to pages with
        /// matching options (with counts) and highlights the matching rows, 150 ms after typing
        /// stops. Enter opens the first match; Esc clears the search (and, when it is already
        /// empty, closes the window as usual).
        /// </summary>
        private FrameworkElement BuildSearchBox()
        {
            // A rounded field with a search glyph; its border turns the focus colour while typing.
            var frame = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(Spacing.Xs, 0, Spacing.Xs, Spacing.Sm),
                SnapsToDevicePixels = true,
            };
            frame.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceInput);
            frame.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);

            var host = new Grid();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glyph = new TextBlock
            {
                Text = FormatStylesChrome.SearchGlyph,
                FontFamily = FormatStylesChrome.IconFont,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Spacing.Sm, 0, 0, 0),
                IsHitTestVisible = false,
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPlaceholder);
            host.Children.Add(glyph);

            _searchBox = new TextBox
            {
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(Spacing.Xs + 2, 5, Spacing.Sm, 5),
                ToolTip = "Search option names, descriptions and choices (Ctrl+F)",
            };
            System.Windows.Automation.AutomationProperties.SetName(_searchBox, "Search for options");
            _searchBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            _searchBox.SetResourceReference(TextBoxBase.CaretBrushProperty, ThemeTokens.TextPrimary);
            _searchBox.GotKeyboardFocus += (_, _) => frame.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderFocus);
            _searchBox.LostKeyboardFocus += (_, _) => frame.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            Grid.SetColumn(_searchBox, 1);

            _searchPlaceholder = new TextBlock
            {
                Text = "Search for options…",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                Margin = new Thickness(Spacing.Sm, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            _searchPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPlaceholder);
            Grid.SetColumn(_searchPlaceholder, 1);

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplySearch(); };
            _searchBox.TextChanged += (_, _) =>
            {
                _searchPlaceholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
                _searchTimer.Stop();
                _searchTimer.Start();
            };
            _searchBox.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && !string.IsNullOrEmpty(_searchBox.Text))
                {
                    _searchBox.Clear();
                    _searchTimer.Stop();
                    ApplySearch();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    _searchTimer.Stop();
                    ApplySearch();
                    OpenFirstMatch();
                    e.Handled = true;
                }
            };

            host.Children.Add(_searchBox);
            host.Children.Add(_searchPlaceholder);
            frame.Child = host;
            return frame;
        }

        private void FocusSearch()
        {
            if (_searchBox == null) return;
            _searchBox.Focus();
            FocusManager.SetFocusedElement(this, _searchBox);
            _searchBox.SelectAll();
        }

        /// <summary>Filters the page tree and re-highlights the current page for the search box's text.</summary>
        private void ApplySearch()
        {
            var query = _searchBox?.Text?.Trim() ?? string.Empty;
            var result = _viewModel.Search(query);
            _searchResult = result;
            _searchMatches = new System.Collections.Generic.HashSet<string>(result.OptionIds, StringComparer.Ordinal);

            var visible = new System.Collections.Generic.HashSet<string>(result.GroupIds, StringComparer.Ordinal);
            foreach (var entry in _groupLeaves)
            {
                var (leaf, _, matches, _) = entry.Value;
                leaf.Visibility = visible.Contains(entry.Key) ? Visibility.Visible : Visibility.Collapsed;
                if (result.Counts.TryGetValue(entry.Key, out var count))
                {
                    matches.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    matches.Visibility = Visibility.Visible;
                }
                else
                {
                    matches.Visibility = Visibility.Collapsed;
                }
            }
            if (_settingsTree != null)
            {
                foreach (var node in _settingsTree.Items.OfType<TreeViewItem>())
                {
                    if (node.Tag is FormatStylesSchemaModel.Group) continue; // a v1 top-level leaf
                    node.Visibility = node.Items.OfType<TreeViewItem>().Any(l => l.Visibility == Visibility.Visible)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }

            if (_currentGroup != null) UpdateRightForGroup(_currentGroup, _currentGroupCategory);
            if (query.Length > 0)
                SetStatus(result.OptionIds.Count == 0
                    ? $"No options match '{query}'."
                    : $"{result.OptionIds.Count} option{(result.OptionIds.Count == 1 ? "" : "s")} match '{query}'. Press Enter to go to the first.");
        }

        /// <summary>Enter in the search box: opens the first matching page and scrolls to its first match.</summary>
        private void OpenFirstMatch()
        {
            var first = _searchResult;
            if (first?.FirstGroupId == null || !_groupLeaves.TryGetValue(first.FirstGroupId, out var entry)) return;
            if (!entry.Leaf.IsSelected) entry.Leaf.IsSelected = true;
            else if (entry.Leaf.Tag is FormatStylesSchemaModel.Group group) UpdateRightForGroup(group, entry.Category);

            var target = first.FirstOptionId;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var row = _settingControlsHost?.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, target));
                row?.BringIntoView();
            }));
        }

        /// <summary>Spec 040 (T099) — "● N" on each page leaf: its options that differ from SQL Prompt's default.</summary>
        private void UpdateLeafBadges()
        {
            foreach (var entry in _groupLeaves)
            {
                var changed = _viewModel.ChangedCount(entry.Key);
                entry.Value.Changed.Text = changed > 0 ? "● " + changed.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
                entry.Value.Changed.Visibility = changed > 0 ? Visibility.Visible : Visibility.Collapsed;
                ((FrameworkElement)entry.Value.Changed.Parent).ToolTip = changed > 0
                    ? $"{changed} option{(changed == 1 ? "" : "s")} on this page differ from SQL Prompt's default"
                    : null;
            }
        }

        /// <summary>Spec 040 (T099) — an option edit updates that row's marker and the page counts in place.</summary>
        private void OnWorkingValueChanged(string settingId)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnWorkingValueChanged(settingId)));
                return;
            }
            if (_rowMarkers.TryGetValue(settingId, out var marker))
                ApplyChangeMarker(marker.Setting, marker.Label, marker.Reset);
            UpdateLeafBadges();
        }

        private void ApplyChangeMarker(FormatSettingNode setting, TextBlock label, Button reset)
        {
            var changed = _viewModel.IsChanged(setting.Id);
            label.FontWeight = changed ? Typography.WeightSemiBold : FontWeights.Normal;
            reset.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The option's SQL Prompt default, as its control shows it.</summary>
        private static string DefaultLabel(FormatSettingNode setting)
        {
            var raw = setting.DefaultJson.Trim().Trim('"');
            if (raw == "true") return "on";
            if (raw == "false") return "off";
            return setting.EnumLabels != null ? setting.LabelFor(raw) : raw;
        }

        // -----------------------------------------------------------------
        // Right panel — settings form for the selected group (top) + live preview (bottom)
        // -----------------------------------------------------------------
        private FrameworkElement BuildRightPanel()
        {
            // The options and the preview side by side, both full height; under the preview, what
            // each value of the focused option does. On a narrow window the preview goes under the
            // options instead (ApplyRightLayout).
            var panel = new Grid();
            Grid.SetColumn(panel, 4);
            _rightPanel = panel;

            // ── Settings form card ─────────────────────────────────────────
            var formCard = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            formCard.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfacePanel);
            formCard.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);

            var formGrid = new Grid { Margin = new Thickness(Spacing.Md, Spacing.Sm, Spacing.Md, Spacing.Md) };
            formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // breadcrumb title
            formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // read-only hint
            formGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // scrolling form

            // Breadcrumb page title ("Global › Lists") — set by UpdateRightForGroup.
            _breadcrumbText = new TextBlock
            {
                Text = "Select a category",
                FontFamily = Typography.UiFont,
                FontSize = Typography.H4,
                FontWeight = Typography.WeightSemiBold,
                Margin = new Thickness(Spacing.Xs, Spacing.Xs, 0, Spacing.Sm),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _breadcrumbText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetRow(_breadcrumbText, 0);
            formGrid.Children.Add(_breadcrumbText);

            // Shown while a built-in style is loaded: says where an edit goes and that it is
            // reversible. Built-ins used to be read-only and this said so.
            _builtInHint = new Border
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(Spacing.Xs, 0, 0, Spacing.Sm),
            };
            var hintInfo = new TextBlock
            {
                Text = FormatStylesChrome.InfoGlyph,
                FontFamily = FormatStylesChrome.IconFont,
                FontSize = 12,
                Margin = new Thickness(0, 1, Spacing.Sm - 2, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            hintInfo.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextLink);
            _builtInHintText = new TextBlock
            {
                Text = BuiltInHint,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
            };
            _builtInHintText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            var hintRow = new DockPanel();
            DockPanel.SetDock(hintInfo, Dock.Left);
            hintRow.Children.Add(hintInfo);
            hintRow.Children.Add(_builtInHintText);
            _builtInHint.Child = hintRow;
            Grid.SetRow(_builtInHint, 1);
            formGrid.Children.Add(_builtInHint);

            var formScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            _settingControlsHost = new StackPanel { Orientation = Orientation.Vertical };
            // Spec 040 (T078): every row's label column is one SharedSizeGroup, so a page's labels
            // line up at the width of its longest label.
            Grid.SetIsSharedSizeScope(_settingControlsHost, true);
            _settingControlsEmpty = new TextBlock
            {
                Text = "Select a category on the left to edit its settings.",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                Margin = new Thickness(0, Spacing.Sm, 0, 0),
            };
            _settingControlsEmpty.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _settingControlsHost.Children.Add(_settingControlsEmpty);
            formScroll.Content = _settingControlsHost;
            Grid.SetRow(formScroll, 2);
            formGrid.Children.Add(formScroll);

            formCard.Child = formGrid;
            _formCard = formCard;
            panel.Children.Add(formCard);

            // ── Splitter between the options and the preview (invisible, in the 8px gutter) ──
            _rightSplitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, // fill the gutter so it stays draggable
                ShowsPreview = false,
                Background = System.Windows.Media.Brushes.Transparent,
            };
            panel.Children.Add(_rightSplitter);

            // ── Live preview card ──
            var previewCard = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            previewCard.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            previewCard.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceInput);

            var previewGrid = new Grid();
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // header + source
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // warning bar
            _previewRow = new RowDefinition { Height = new GridLength(1.25, GridUnitType.Star), MinHeight = 120 };
            previewGrid.RowDefinitions.Add(_previewRow);                                                          // preview text
            _examplesRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
            previewGrid.RowDefinitions.Add(_examplesRow);                                                         // option examples

            // Header: LIVE PREVIEW (left) + what it formats (right).
            var previewHeader = new Grid { Margin = new Thickness(Spacing.Md, Spacing.Sm, Spacing.Md, Spacing.Xs) };
            previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            previewHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var previewLabel = new TextBlock
            {
                Text = "LIVE PREVIEW",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Md, 0),
            };
            previewLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            Grid.SetColumn(previewLabel, 0);
            previewHeader.Children.Add(previewLabel);

            // One drop-down for what the preview formats: this page's sample, a SELECT example, your
            // own sample, or the query that was open (spec 030 T019 / FR-008). It replaced a row of
            // radio buttons that had no room for the SELECT examples.
            _sourceCombo = new ComboBox
            {
                MinWidth = 180,
                MaxWidth = 340,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                ToolTip = "What the preview formats",
            };
            ComboBoxTheming.Apply(_sourceCombo);
            System.Windows.Automation.AutomationProperties.SetName(_sourceCombo, "Preview source");
            _sourceCombo.SelectionChanged += (_, _) =>
            {
                if (!_suppressSourceChanged) _userPickedSource = true;
                OnPreviewSourceChanged();
            };

            // Spec 033 (T025 / FR-014) — edit the persisted preview sample in place. While
            // checked, the preview box shows the RAW sample (editable, persisted atomically
            // via the PreviewSample setter on every change); unchecking restores the live
            // formatted preview. Only for "My sample".
            _editSampleToggle = new CheckBox
            {
                Content = "Edit sample",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Spacing.Md, 0, 0, 0),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                IsEnabled = false,
                ToolTip = "Edit your own sample SQL (\"My sample\"). Changes persist across sessions.",
            };
            _editSampleToggle.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            _editSampleToggle.Checked += (_, _) =>
            {
                if (_previewTextBox == null) return;
                _previewTextBox.Text = _viewModel.PreviewSample;
                ShowSampleEditor(true);
            };
            _editSampleToggle.Unchecked += (_, _) =>
            {
                if (_previewTextBox == null) return;
                CommitSampleEdit(); // one persist + one preview refresh for the whole edit session
                ShowSampleEditor(false);
            };

            var sourceStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            sourceStack.Children.Add(_sourceCombo);
            sourceStack.Children.Add(_editSampleToggle);
            Grid.SetColumn(sourceStack, 1);
            previewHeader.Children.Add(sourceStack);
            Grid.SetRow(previewHeader, 0);
            previewGrid.Children.Add(previewHeader);
            PopulateSourceCombo();

            _previewWarningBar = new Border
            {
                Padding = new Thickness(Spacing.Md, Spacing.Sm, Spacing.Md, Spacing.Sm),
                Visibility = Visibility.Collapsed,
                BorderThickness = new Thickness(0, 1, 0, 1),
            };
            // Amber/yellow is a semantic colour per CLAUDE.md's allow-list. Near-solid fill + fixed
            // dark text so the strip reads the same in every theme.
            _previewWarningBar.Background = Freeze(new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xF2, 0xFB, 0xBF, 0x24)));
            _previewWarningBar.BorderBrush = Freeze(new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)));
            _previewWarningText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                Foreground = PreviewWarnTextBrush,
            };
            _previewWarningBar.Child = _previewWarningText;
            Grid.SetRow(_previewWarningBar, 1);
            previewGrid.Children.Add(_previewWarningBar);

            // The formatted preview: selectable, syntax-coloured, with tabs at the style's own width
            // (spec 040 STY-02) — a TextBox always used 8.
            _previewView = new SqlPreviewView
            {
                ShowLineNumbers = true,
                TabSize = _viewModel.PreviewTabSize,
                Margin = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Xs, Spacing.Sm),
                Text = "-- The live preview appears once the schema loads and a style is selected.",
            };
            System.Windows.Automation.AutomationProperties.SetName(_previewView, "Live preview");
            // Its own style, not the window's implicit one: inside SSMS the shell's scroll bar
            // styles reach the preview control's resources and would win (see FormatStylesChrome).
            _previewView.Scroller.Style = FormatStylesChrome.ScrollViewerStyle;
            Grid.SetRow(_previewView, 2);
            previewGrid.Children.Add(_previewView);

            // "Edit sample" swaps the preview for this box holding the raw, editable sample.
            _previewTextBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = Typography.MonoFont,
                FontSize = Typography.Body,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed,
            };
            _previewTextBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetRow(_previewTextBox, 2);
            previewGrid.Children.Add(_previewTextBox);

            var examples = BuildExamplesPanel();
            Grid.SetRow(examples, 3);
            previewGrid.Children.Add(examples);

            previewCard.Child = previewGrid;
            _previewCard = previewCard;
            panel.Children.Add(previewCard);

            ApplyRightLayout(stacked: false);
            panel.SizeChanged += (_, e) => ApplyRightLayout(e.NewSize.Width < StackBelowWidth);
            SetExamplesOpen(s_examplesOpen);
            return panel;
        }

        /// <summary>
        /// The options beside the preview (side by side, both full height) — or, on a narrow
        /// window, the preview under the options.
        /// </summary>
        private void ApplyRightLayout(bool stacked)
        {
            if (_rightPanel == null || _formCard == null || _previewCard == null || _rightSplitter == null) return;
            if (_stacked == stacked) return;
            _stacked = stacked;
            _rightPanel.RowDefinitions.Clear();
            _rightPanel.ColumnDefinitions.Clear();
            if (stacked)
            {
                _rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45, GridUnitType.Star), MinHeight = 150 });
                _rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Spacing.Sm) });
                _rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(55, GridUnitType.Star), MinHeight = 200 });
            }
            else
            {
                _rightPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 330 });
                _rightPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Sm) });
                _rightPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 340 });
            }
            var i = 0;
            foreach (var part in new FrameworkElement[] { _formCard, _rightSplitter, _previewCard })
            {
                Grid.SetRow(part, stacked ? i : 0);
                Grid.SetColumn(part, stacked ? 0 : i);
                i++;
            }
            _rightSplitter.ResizeDirection = stacked ? GridResizeDirection.Rows : GridResizeDirection.Columns;
        }

        // -----------------------------------------------------------------
        // What the preview formats
        // -----------------------------------------------------------------

        private const string SourcePage = "page";
        private const string SourceMine = "mine";
        private const string SourceCurrent = "current";
        private const string SourceSelectPrefix = "select:";

        /// <summary>
        /// Fills the preview's source drop-down: this page's sample (SQL Prompt model), the engine's
        /// SELECT examples, your own sample, and the query that was open. Keeps a choice the user
        /// made when it is still offered; otherwise picks this page's sample, or your own sample.
        /// (The first fill runs before the schema loads, when only "My sample" can be picked: that
        /// pick must not stick once "This page's sample" is offered.)
        /// </summary>
        private void PopulateSourceCombo()
        {
            if (_sourceCombo == null) return;
            var previous = _userPickedSource ? (_sourceCombo.SelectedItem as ComboBoxItem)?.Tag as string : null;
            _suppressSourceChanged = true;
            try
            {
                _sourceCombo.Items.Clear();
                var model = _viewModel.SchemaModel;
                if (_viewModel.IsSqlPromptModel)
                    _sourceCombo.Items.Add(SourceItem("This page's sample", SourcePage, "Code this page's options act on."));
                foreach (var q in model?.SelectExamples ?? new System.Collections.Generic.List<FormatStylesSchemaModel.ExampleQuery>())
                    _sourceCombo.Items.Add(SourceItem("SELECT: " + q.Name, SourceSelectPrefix + q.Id, q.Sql));
                _sourceCombo.Items.Add(SourceItem("My sample", SourceMine, "Your own sample SQL. Tick \"Edit sample\" to change it."));
                var current = SourceItem("Current query", SourceCurrent,
                    _viewModel.HasCurrentQuery ? "The query in the editor that was open when this window opened." : "No SQL editor was open when this window opened.");
                current.IsEnabled = _viewModel.HasCurrentQuery;
                _sourceCombo.Items.Add(current);

                var pick = _sourceCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.IsEnabled && Equals(i.Tag, previous))
                           ?? _sourceCombo.Items.OfType<ComboBoxItem>().First(i => Equals(i.Tag, _viewModel.IsSqlPromptModel ? SourcePage : SourceMine));
                _sourceCombo.SelectedItem = pick;
            }
            finally
            {
                _suppressSourceChanged = false;
            }
            OnPreviewSourceChanged();
        }

        private static ComboBoxItem SourceItem(string text, string tag, string tooltip) =>
            new ComboBoxItem { Content = text, Tag = tag, ToolTip = tooltip };

        private void OnPreviewSourceChanged()
        {
            if (_suppressSourceChanged || _sourceCombo?.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
            if (tag.StartsWith(SourceSelectPrefix, StringComparison.Ordinal))
            {
                var id = tag.Substring(SourceSelectPrefix.Length);
                _viewModel.SelectExampleSql = _viewModel.SchemaModel?.ExampleQueries.TryGetValue(id, out var q) == true ? q.Sql : null;
                _viewModel.PreviewSourceMode = FormatPreviewSource.SelectExample;
            }
            else
            {
                _viewModel.PreviewSourceMode = tag switch
                {
                    SourcePage => FormatPreviewSource.PageSample,
                    SourceCurrent => FormatPreviewSource.CurrentQuery,
                    _ => FormatPreviewSource.Sample,
                };
            }

            // Sample editing only applies to "My sample".
            if (_editSampleToggle != null)
            {
                var mine = tag == SourceMine;
                if (!mine) _editSampleToggle.IsChecked = false;
                _editSampleToggle.IsEnabled = mine;
            }
        }

        // -----------------------------------------------------------------
        // What each value of the focused option does
        // -----------------------------------------------------------------

        /// <summary>
        /// The panel under the preview: for the option last clicked or tabbed to, one card per value
        /// (on, off, each choice, or a few numbers), each the example formatted with the working style
        /// and that value, its changed lines marked. "Use this" sets the value. ▾ folds it away.
        /// </summary>
        private FrameworkElement BuildExamplesPanel()
        {
            var panel = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
            panel.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            panel.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceSidebar);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(Spacing.Md, Spacing.Sm, Spacing.Sm, Spacing.Xs) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel { Orientation = Orientation.Vertical };
            _examplesTitle = new TextBlock
            {
                Text = "WHAT EACH VALUE DOES",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _examplesTitle.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _examplesSubtitle = new TextBlock
            {
                Text = "Click an option to see what each of its values does.",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            };
            _examplesSubtitle.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            titles.Children.Add(_examplesTitle);
            titles.Children.Add(_examplesSubtitle);
            header.Children.Add(titles);

            _examplesToggle = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            FormatStylesChrome.ApplyIconButton(_examplesToggle);
            _examplesToggle.Click += (_, _) => SetExamplesOpen(!s_examplesOpen);
            Grid.SetColumn(_examplesToggle, 1);
            header.Children.Add(_examplesToggle);
            grid.Children.Add(header);

            _examplesHost = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Sm) };
            _examplesScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _examplesHost,
                Style = FormatStylesChrome.ScrollViewerStyle,
            };
            System.Windows.Automation.AutomationProperties.SetName(_examplesScroll, "Option examples");
            Grid.SetRow(_examplesScroll, 1);
            grid.Children.Add(_examplesScroll);

            panel.Child = grid;
            return panel;
        }

        /// <summary>Opens or folds the option examples; the preview takes the room they free.</summary>
        private void SetExamplesOpen(bool open)
        {
            s_examplesOpen = open;
            if (_examplesScroll != null) _examplesScroll.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (_examplesRow != null) _examplesRow.Height = open ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            if (_examplesToggle != null)
            {
                _examplesToggle.Content = open ? "▾" : "▸";
                _examplesToggle.ToolTip = open ? "Hide the option examples" : "Show what each value of the option does";
                System.Windows.Automation.AutomationProperties.SetName(_examplesToggle, open ? "Hide the option examples" : "Show the option examples");
            }
            if (open) ScheduleExamples();
        }

        /// <summary>Makes <paramref name="settingId"/> the option the examples are about, and marks its row.</summary>
        private void FocusOption(string settingId)
        {
            if (string.Equals(_focusedSettingId, settingId, StringComparison.Ordinal)) return;
            if (_focusedSettingId != null && _rowBorders.TryGetValue(_focusedSettingId, out var previous))
                previous.BorderBrush = System.Windows.Media.Brushes.Transparent;
            _focusedSettingId = settingId;
            if (_rowBorders.TryGetValue(settingId, out var row))
                row.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.AccentPrimary);
            ScheduleExamples();
        }

        /// <summary>Refreshes the examples shortly — after a click, a key, or a new live preview settles.</summary>
        private void ScheduleExamples()
        {
            if (_examplesHost == null || _closed) return;
            if (_examplesTimer == null)
            {
                _examplesTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                _examplesTimer.Tick += async (_, _) =>
                {
                    _examplesTimer.Stop();
                    if (_closed) return;
                    try { await RefreshExamplesAsync(); }
                    catch (Exception ex) { Log.Debug(ex, "FormatStylesEditor: option examples failed"); }
                };
            }
            _examplesTimer.Stop();
            _examplesTimer.Start();
        }

        private FormatSettingNode? FocusedSetting =>
            _focusedSettingId == null ? null : _currentGroup?.Settings.FirstOrDefault(s => s.Id == _focusedSettingId);

        /// <summary>
        /// Formats the focused option's example once per value with the working style, then shows
        /// a card per value. Skipped when nothing it depends on changed since the last run.
        /// </summary>
        private async System.Threading.Tasks.Task RefreshExamplesAsync()
        {
            if (_examplesHost == null || _examplesTitle == null || _examplesSubtitle == null) return;
            var setting = FocusedSetting;
            var model = _viewModel.SchemaModel;
            _examplesTitle.Text = setting == null ? "WHAT EACH VALUE DOES" : $"WHAT “{setting.DisplayName.ToUpperInvariant()}” DOES";
            if (!s_examplesOpen) return;

            if (setting == null || model == null)
            {
                ShowExamplesMessage("Click an option to see what each of its values does.", null);
                return;
            }
            var plan = FormatStylesExamples.For(model, setting);
            if (plan == null)
            {
                ShowExamplesMessage(setting.Example == null && model.ExampleQueries.Count == 0
                    ? "This engine sends no examples. Update AKML SQL to see what each value does."
                    : "There is no example for this option.", setting.Id);
                return;
            }

            var signature = setting.Id + "\n" + _viewModel.BuildProfileJson() + "\n" + _viewModel.PreviewTabSize
                            + "\n" + (model.GroupOf(setting.Id)?.Sample?.GetHashCode() ?? 0);
            if (signature == _examplesSignature && _examplesShownFor == setting.Id) return;

            _examplesCts?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _examplesCts = cts;
            if (_examplesShownFor != setting.Id) ShowExamplesMessage("Formatting the examples…", setting.Id);

            var outputs = await _viewModel.FormatExamplesAsync(plan.Requests, cts.Token).ConfigureAwait(true);
            // Cancelled: a newer refresh, or a live preview queued (its result or failure asks again).
            if (outputs == null || cts.IsCancellationRequested || !ReferenceEquals(cts, _examplesCts) || _closed) return;
            if (FocusedSetting?.Id != setting.Id) return;
            if (outputs.All(o => o == null))
            {
                ShowExamplesMessage(_viewModel.IsEngineConnected
                    ? "The examples could not be formatted. Try another option, or reopen the window."
                    : "Examples need the AKML SQL engine, which is not connected.", setting.Id);
                return;
            }

            // A card whose request failed (a timeout, a format the engine refused) is left out; keep
            // the run unfinished so the next refresh — the next live preview — tries it again.
            _examplesSignature = outputs.All(o => o != null) ? signature : null;
            _examplesShownFor = setting.Id;
            RenderExampleCards(setting, plan, outputs);
        }

        private void ShowExamplesMessage(string text, string? settingId)
        {
            if (_examplesHost == null) return;
            _examplesHost.Children.Clear();
            _examplesHost.Children.Add(RowNote(text));
            _examplesShownFor = settingId == null ? null : "message:" + settingId;
            _examplesSignature = null;
            if (_examplesSubtitle != null)
                _examplesSubtitle.Text = settingId == null
                    ? "Click an option to see what each of its values does."
                    : "Each value of this option, formatted with this style's other settings.";
        }

        private void RenderExampleCards(FormatSettingNode setting, FormatStylesExamples.Plan plan, System.Collections.Generic.IReadOnlyList<string?> outputs)
        {
            if (_examplesHost == null) return;
            var offset = _examplesScroll?.VerticalOffset ?? 0;
            _examplesHost.Children.Clear();

            var gateOpen = IsGateOpen(setting);
            var with = plan.With.Select(w => $"{OptionLabel(w.Key)} = {ValueLabel(w.Key, w.Value)}").ToList();
            if (_examplesSubtitle != null)
            {
                _examplesSubtitle.Text = (gateOpen ? "Each value on " : $"{GateText(setting)} Each value on ")
                    + $"“{plan.QueryTitle}”, with this style's other settings"
                    + (with.Count > 0 ? $" and {string.Join(", ", with)}" : string.Empty)
                    + ". Marked lines differ from the default.";
            }

            var current = _viewModel.GetWorkingValue(setting.Id);
            var canApply = gateOpen && !_viewModel.IsSelectedReadOnly
                           && !string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase);
            var applyBlockedBy = !gateOpen ? GateText(setting)
                : _viewModel.IsSelectedReadOnly ? FormatStylesEditorViewModel.TeamReadOnlyText(_viewModel.LoadedProfileName ?? string.Empty)
                : null;
            foreach (var card in plan.Cards)
            {
                var text = outputs[card.Output];
                if (text == null) continue;
                var baseline = card.Baseline >= 0 ? outputs[card.Baseline] : null;
                var isCurrent = FormatStylesEditorViewModel.ValuesEqual(Normalize(setting, current), Normalize(setting, card.Value))
                                || (current is string s && card.Value is string v && string.Equals(s, v, StringComparison.OrdinalIgnoreCase));
                var tabSize = FormatStylesExamples.TabSizeOf(plan.Requests[card.Output].Settings, FormatStylesEditorViewModel.SqlPromptTabSizeId, _viewModel.PreviewTabSize);
                _examplesHost.Children.Add(BuildExampleCard(setting, card, text, card.IsDefault ? null : baseline, isCurrent, canApply, applyBlockedBy, tabSize));
            }
            if (_examplesHost.Children.Count == 0)
                _examplesHost.Children.Add(RowNote("The examples could not be formatted."));
            if (_examplesScroll != null) _examplesScroll.ScrollToVerticalOffset(offset);

            // "Use this" rebuilt the cards under the keyboard: put focus back on that value's card.
            if (_refocusExampleKey != null)
            {
                var key = _refocusExampleKey;
                _refocusExampleKey = null;
                _examplesHost.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, key))?.Focus();
            }
        }

        /// <summary>One value's card: its name, Default / Current, "Use this", and the formatted example.</summary>
        private FrameworkElement BuildExampleCard(FormatSettingNode setting, FormatStylesExamples.Card card, string text, string? baseline,
            bool isCurrent, bool canApply, string? applyBlockedBy, int tabSize)
        {
            var label = ValueLabel(setting.Id, card.Value);
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
                Tag = FormatStylesSchemaModel.ValueKey(card.Value),
                // Focusable so "Use this" can hand the keyboard back to the card it rebuilt.
                Focusable = true,
            };
            border.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceInput);
            border.SetResourceReference(Border.BorderBrushProperty, isCurrent ? ThemeTokens.AccentPrimary : ThemeTokens.BorderDefault);
            System.Windows.Automation.AutomationProperties.SetName(border, $"{setting.DisplayName}: {label}");

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            var head = new Border { Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Xs, Spacing.Xs), BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(5, 5, 0, 0) };
            head.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfacePanel);
            head.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            var headRow = new DockPanel { LastChildFill = true };

            var tags = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (card.IsDefault)
            {
                var d = new TextBlock { Text = "Default", FontSize = 10.5, FontWeight = Typography.WeightSemiBold };
                var pill = FormatStylesChrome.Pill(d, ThemeTokens.SurfaceElevated, null, ThemeTokens.TextSecondary);
                pill.Margin = new Thickness(Spacing.Xs, 0, 0, 0);
                tags.Children.Add(pill);
            }
            if (isCurrent)
            {
                var c = new TextBlock { Text = "Current", FontSize = 10.5, FontWeight = Typography.WeightSemiBold };
                var pill = FormatStylesChrome.Pill(c, ThemeTokens.AccentPrimary, null, ThemeTokens.TextOnAccent);
                pill.Margin = new Thickness(Spacing.Xs, 0, 0, 0);
                tags.Children.Add(pill);
            }
            else
            {
                var use = new Button
                {
                    Content = "Use this",
                    Padding = new Thickness(Spacing.Sm, 1, Spacing.Sm, 1),
                    Margin = new Thickness(Spacing.Sm, 0, 0, 0),
                    FontFamily = Typography.UiFont,
                    FontSize = Typography.Small,
                    IsEnabled = canApply,
                    ToolTip = applyBlockedBy ?? $"Set “{setting.DisplayName}” to {label}",
                };
                // A disabled button shows no tooltip unless asked: the reason matters most then.
                ToolTipService.SetShowOnDisabled(use, true);
                ThemedButton.ApplySecondary(use);
                System.Windows.Automation.AutomationProperties.SetName(use, $"Use {label} for {setting.DisplayName}");
                use.Click += (_, _) =>
                {
                    _refocusExampleKey = use.IsKeyboardFocused ? FormatStylesSchemaModel.ValueKey(card.Value) : null;
                    ApplyExampleValue(setting, card.Value);
                };
                tags.Children.Add(use);
            }
            DockPanel.SetDock(tags, Dock.Right);
            headRow.Children.Add(tags);

            var name = new TextBlock
            {
                Text = label,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            headRow.Children.Add(name);
            head.Child = headRow;
            stack.Children.Add(head);

            var ownWith = card.OwnWith.Select(w => $"{OptionLabel(w.Key)} = {ValueLabel(w.Key, w.Value)}").ToList();
            if (card.OwnQueryTitle != null || ownWith.Count > 0)
            {
                var where = card.OwnQueryTitle != null ? $"Shown on “{card.OwnQueryTitle}”" : "Shown";
                var with = ownWith.Count > 0 ? $" with {string.Join(", ", ownWith)}" : string.Empty;
                var own = RowNote($"{where}{with}: the main example does not show this value.");
                own.Margin = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, 0);
                stack.Children.Add(own);
            }

            var view = new SqlPreviewView
            {
                ShowLineNumbers = false,
                TabSize = tabSize,   // the width this value was formatted with ("Spaces per tab" cards)
                MaxHeight = 260,
                Margin = new Thickness(Spacing.Xs, Spacing.Xs, Spacing.Xs, Spacing.Xs),
                Text = text,
                // Bars, not a fill: a fill behind the text drops the syntax colours below 4.5:1.
                MarkedLines = FormatStylesExamples.ChangedLines(baseline, text),
            };
            view.Scroller.Style = FormatStylesChrome.ScrollViewerStyle;
            System.Windows.Automation.AutomationProperties.SetName(view, $"{label} example");
            stack.Children.Add(view);

            if (!card.IsDefault && baseline != null && string.Equals(baseline, text, StringComparison.Ordinal))
            {
                var same = RowNote("Looks the same as the default here, with this style's other settings.");
                same.Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, Spacing.Xs);
                stack.Children.Add(same);
            }

            border.Child = stack;
            return border;
        }

        /// <summary>A card's "Use this": sets the value as the control would, and shows it in the form.</summary>
        private void ApplyExampleValue(FormatSettingNode setting, object? value)
        {
            if (_viewModel.IsSelectedReadOnly)   // a team style: the form is disabled, so are its examples
            {
                SetStatus(FormatStylesEditorViewModel.TeamReadOnlyText(_viewModel.LoadedProfileName ?? string.Empty));
                return;
            }
            if (!IsGateOpen(setting))
            {
                SetStatus(GateText(setting) ?? $"{setting.DisplayName} is not in effect.");
                return;
            }
            _viewModel.SetWorkingValue(setting.Id, Normalize(setting, value));
            RefreshVisibleSettingControls(); // the row's control shows the new value; the focus stays on it
            SetStatus($"{setting.DisplayName}: {ValueLabel(setting.Id, value)}.");
        }

        /// <summary>A value as the setting's control records it: a number option as an int.</summary>
        private static object? Normalize(FormatSettingNode setting, object? value)
        {
            if (setting.Type == "Int" && value is IConvertible c && !(value is string) && !(value is bool))
            {
                try { return Convert.ToInt32(c, System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException) { return value; }
            }
            return value;
        }

        /// <summary>An option's label by setting id (any page), or the id.</summary>
        private string OptionLabel(string settingId) =>
            _viewModel.SchemaModel?.GroupOf(settingId)?.Settings.FirstOrDefault(s => s.Id == settingId)?.DisplayName ?? settingId;

        /// <summary>How a value reads in the editor: on / off, the choice's label, or the number.</summary>
        private string ValueLabel(string settingId, object? value)
        {
            if (value is bool b) return b ? "On" : "Off";
            var setting = _viewModel.SchemaModel?.GroupOf(settingId)?.Settings.FirstOrDefault(s => s.Id == settingId);
            var text = FormatStylesSchemaModel.ValueKey(value);
            return setting?.EnumLabels != null ? setting.LabelFor(text) : text;
        }

        private void OnThemeVariantChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnThemeVariantChanged(sender, e)));
                return;
            }
            TitleBarTheme.Apply(this, ThemeRegistry.Instance.Current == ThemeVariant.Dark);
        }

        // -----------------------------------------------------------------
        // Schema → TreeView
        // -----------------------------------------------------------------
        private void RebuildSettingsTreeFromSchema(string schemaJson)
        {
            if (_settingsTree == null) return;

            _settingsTree.Items.Clear();
            _groupLeaves.Clear();
            TreeViewItem? firstLeaf = null;

            try
            {
                // Spec 033 (T022) — parsing lives in the testable FormatStylesSchemaModel;
                // this method only renders WPF nodes from the model. Spec 040 (T096): the view
                // model keeps the parsed model, which option search reads too.
                var model = _viewModel.SchemaModel ?? FormatStylesSchemaModel.Parse(schemaJson);

                if (model.Categorized)
                {
                    // v2 — SQL Prompt's category → page hierarchy: categories expand; each group
                    // (page) is a selectable leaf whose whole settings list edits on the right.
                    foreach (var category in model.Categories)
                    {
                        var categoryNode = new TreeViewItem
                        {
                            Header = category.DisplayName,
                            IsExpanded = true,
                            FontWeight = Typography.WeightSemiBold,
                            Padding = new Thickness(Spacing.Xs, 0, Spacing.Sm, 0),
                        };
                        categoryNode.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                        foreach (var group in category.Groups)
                        {
                            var leaf = BuildGroupLeaf(group, category.DisplayName);
                            categoryNode.Items.Add(leaf);
                            firstLeaf ??= leaf;
                        }
                        _settingsTree.Items.Add(categoryNode);
                    }
                }
                else
                {
                    // v1 schema (older engine) — flat: each group is a top-level leaf.
                    foreach (var group in model.FlatGroups)
                    {
                        var leaf = BuildGroupLeaf(group, null);
                        _settingsTree.Items.Add(leaf);
                        firstLeaf ??= leaf;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: failed to parse schema JSON");
            }

            // Open on the first page so the form is never empty (SQL Prompt selects a page by default).
            if (firstLeaf != null) firstLeaf.IsSelected = true;
            UpdateLeafBadges();
            if (!string.IsNullOrWhiteSpace(_searchBox?.Text)) ApplySearch();
        }

        /// <summary>A selectable settings *group* (SQL Prompt "page"); selecting it renders the
        /// group's whole settings list as a form on the right, under a "Category › Group" title.</summary>
        private TreeViewItem BuildGroupLeaf(FormatStylesSchemaModel.Group group, string? categoryDisplay)
        {
            // Header: the page name, then two small pills — how many of its options differ from
            // the default (muted) and, while searching, how many match (accent). Each pill shows
            // and hides with its number, which UpdateLeafBadges / ApplySearch set.
            var changed = new TextBlock { FontSize = 10.5, FontWeight = Typography.WeightSemiBold, Visibility = Visibility.Collapsed };
            var matches = new TextBlock { FontSize = 10.5, FontWeight = Typography.WeightSemiBold, Visibility = Visibility.Collapsed };
            var changedPill = FormatStylesChrome.Pill(changed, ThemeTokens.SurfaceElevated, null, ThemeTokens.TextSecondary);
            var matchesPill = FormatStylesChrome.Pill(matches, ThemeTokens.AccentPrimary, null, ThemeTokens.TextOnAccent);
            foreach (var (pill, text) in new[] { (changedPill, changed), (matchesPill, matches) })
            {
                pill.Margin = new Thickness(Spacing.Sm, 0, 0, 0);
                pill.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = text });
            }
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = group.DisplayName, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(changedPill);
            header.Children.Add(matchesPill);

            var leaf = new TreeViewItem
            {
                Header = header,
                FontWeight = FontWeights.Normal, // counteract the inherited semi-bold category weight
                Tag = group,
                // Under a category the name lines up with the category's (past its chevron).
                Padding = new Thickness(categoryDisplay != null ? Spacing.Xs + 20 : Spacing.Sm, 0, Spacing.Sm, 0),
            };
            System.Windows.Automation.AutomationProperties.SetName(leaf, group.DisplayName);
            _groupLeaves[group.Id] = (leaf, changed, matches, categoryDisplay);
            leaf.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            leaf.Selected += (_, e) =>
            {
                UpdateRightForGroup(group, categoryDisplay);
                e.Handled = true;
            };
            return leaf;
        }

        /// <summary>
        /// T060 — small pill rendered for settings flagged <c>Unsupported</c> (FR-023). Tooltip
        /// explains the FR-023 contract: value is preserved on round-trip, but AKML's formatter
        /// doesn't honour the setting yet.
        /// </summary>
        private static Border BuildUnsupportedBadge()
        {
            var badge = new Border
            {
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(6, 1, 6, 1),
                CornerRadius = new CornerRadius(8),
                ToolTip = "Not yet supported by AKML's formatter. The imported value is preserved " +
                          "and will round-trip on export.",
            };
            badge.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceHover);
            badge.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            badge.BorderThickness = new Thickness(1);

            var text = new TextBlock
            {
                Text = "Unsupported",
                FontFamily = Typography.UiFont,
                FontSize = 10,
                FontWeight = Typography.WeightSemiBold,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            badge.Child = text;
            return badge;
        }

        /// <summary>
        /// Renders the selected group as a settings form (SQL Prompt "page"): a breadcrumb title
        /// ("Category › Group") plus one label-left / control-right row per setting. Reused after
        /// every style load so the controls reflect the freshly-loaded values.
        /// </summary>
        private void UpdateRightForGroup(FormatStylesSchemaModel.Group group, string? categoryDisplay)
        {
            if (_settingControlsHost == null) return;

            _currentGroup = group;
            _currentGroupCategory = categoryDisplay;
            _viewModel.PageSample = group.Sample;

            if (_breadcrumbText != null)
            {
                // "Clauses › Join": the category muted, the page itself in the heading weight.
                _breadcrumbText.Inlines.Clear();
                if (categoryDisplay != null)
                {
                    var trail = new System.Windows.Documents.Run(categoryDisplay + "  ›  ") { FontWeight = FontWeights.Normal };
                    trail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, ThemeTokens.TextSecondary);
                    _breadcrumbText.Inlines.Add(trail);
                }
                _breadcrumbText.Inlines.Add(new System.Windows.Documents.Run(group.DisplayName));
            }

            _settingControlsHost.Children.Clear();
            _gatedRows.Clear();
            _rowMarkers.Clear();
            _rowBorders.Clear();
            // A new page: the examples follow its first option until another one is clicked.
            if (_focusedSettingId == null || !group.Settings.Any(s => s.Id == _focusedSettingId))
                _focusedSettingId = group.Settings.FirstOrDefault()?.Id;
            ScheduleExamples();

            if (group.Settings.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "This category has no editable settings.",
                    FontFamily = Typography.UiFont,
                    FontSize = Typography.Body,
                    Margin = new Thickness(0, Spacing.Sm, 0, 0),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                _settingControlsHost.Children.Add(empty);
                return;
            }

            var index = 0;
            string? subgroup = null;
            foreach (var setting in group.Settings)
            {
                // SQL Prompt pages group their options under small headings ("New lines", "ON").
                if (setting.Subgroup != null && setting.Subgroup != subgroup)
                {
                    subgroup = setting.Subgroup;
                    _settingControlsHost.Children.Add(SubgroupHeading(subgroup, first: index == 0));
                }
                _settingControlsHost.Children.Add(BuildSettingRow(setting, index++));
            }
            UpdateLeafBadges();
        }

        /// <summary>A page's small section heading ("JOIN", "ON") with a hairline running to the right edge.</summary>
        private static Grid SubgroupHeading(string text, bool first)
        {
            var heading = new Grid { Margin = new Thickness(Spacing.Sm, first ? 0 : Spacing.Md, Spacing.Sm, Spacing.Xs) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            var rule = new System.Windows.Shapes.Rectangle { Height = 1, Margin = new Thickness(Spacing.Sm, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
            rule.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ThemeTokens.BorderSubtle);
            Grid.SetColumn(rule, 1);
            heading.Children.Add(label);
            heading.Children.Add(rule);
            return heading;
        }

        /// <summary>
        /// False while the option that turns this one on is off (a collapse threshold under its
        /// collapse switch) — shown disabled with a "takes effect when…" hint, as SQL Prompt does.
        /// </summary>
        private bool IsGateOpen(FormatSettingNode setting)
        {
            if (setting.EnabledWhenId == null) return true;
            var current = _viewModel.GetWorkingValue(setting.EnabledWhenId);
            return current is null || Equals(current, setting.EnabledWhenValue)
                   || string.Equals(current.ToString(), setting.EnabledWhenValue?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Enables or disables the rows the changed setting turns on, in place. Rebuilding the page
        /// instead destroyed the control being toggled, so keyboard focus fell out of the form and
        /// Tab started again from the top.
        /// </summary>
        private void RefreshIfGate(FormatSettingNode changed)
        {
            foreach (var row in _gatedRows)
            {
                if (!string.Equals(row.Setting.EnabledWhenId, changed.Id, StringComparison.Ordinal)) continue;
                var open = IsGateOpen(row.Setting);
                row.Control.IsEnabled = open;
                row.Label.SetResourceReference(TextBlock.ForegroundProperty, open ? ThemeTokens.TextPrimary : ThemeTokens.TextDisabled);
                row.Label.ToolTip = RowTooltip(row.Setting, open);
                row.Hint.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>
        /// One form row. An on/off option is a checkbox whose own content is its label, across the
        /// whole row, so clicking the words toggles it (as in SQL Prompt). Any other option has its
        /// label on the left — in a column the page's rows share, at least 160 px and at most 38% of
        /// the page — and its control on the right, left-aligned in a column at most 280 px wide.
        /// Labels wrap instead of being cut off (spec 040 STY-01, FR-020); the tooltip repeats the
        /// label and adds the description. Every row ends in the same fixed column for its ↺, so
        /// the controls and reset buttons line up down the page; a row tints under the mouse.
        /// </summary>
        private FrameworkElement BuildSettingRow(FormatSettingNode setting, int index)
        {
            var gateOpen = IsGateOpen(setting);
            var unsupported = string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase);
            var isDisabled = unsupported || !gateOpen;
            var currentValue = _viewModel.GetWorkingValue(setting.Id);

            // Spec 040 (T103): Tag = the option id (tests and "go to first match" find rows by it);
            // an option another option turns on is indented under it.
            var rowBorder = new Border
            {
                Padding = new Thickness(Spacing.Sm, 5, Spacing.Xs, 5),
                CornerRadius = new CornerRadius(4),
                Tag = setting.Id,
                Margin = new Thickness(setting.EnabledWhenId != null ? Spacing.Lg : 0, 0, 0, 1),
                Background = System.Windows.Media.Brushes.Transparent,
                // The accent bar of the option the examples under the preview are about.
                BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
            };
            _rowBorders[setting.Id] = rowBorder;
            if (string.Equals(setting.Id, _focusedSettingId, StringComparison.Ordinal))
                rowBorder.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.AccentPrimary);
            // Clicking anywhere on the row, or tabbing into its control, shows what its values do.
            rowBorder.PreviewMouseLeftButtonDown += (_, _) => FocusOption(setting.Id);
            rowBorder.IsKeyboardFocusWithinChanged += (_, e) => { if (e.NewValue is true) FocusOption(setting.Id); };
            var isMatch = _searchMatches.Contains(setting.Id);
            void Rest()
            {
                if (isMatch) rowBorder.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceSelection); // search match
                else rowBorder.Background = System.Windows.Media.Brushes.Transparent;
            }
            Rest();
            rowBorder.MouseEnter += (_, _) => { if (!isMatch) rowBorder.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceHover); };
            rowBorder.MouseLeave += (_, _) => Rest();

            var label = new TextBlock
            {
                Text = setting.DisplayName,
                Tag = OptionLabelTag,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,
                ToolTip = RowTooltip(setting, gateOpen),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, isDisabled ? ThemeTokens.TextDisabled : ThemeTokens.TextPrimary);

            // Wired whenever the option is supported: a closed gate only disables the control, so
            // RefreshIfGate can turn it back on without rebuilding the row.
            var control = BuildControlForSetting(setting, currentValue, unsupported);
            if (!gateOpen) control.IsEnabled = false;
            control.VerticalAlignment = VerticalAlignment.Center;
            var badge = isDisabled && gateOpen ? BuildUnsupportedBadge() : null;

            // Spec 040 (T099, STY-05): an option that differs from SQL Prompt's default has a bold
            // label and a ↺ that puts it back.
            var reset = new Button
            {
                Content = "\u21BA",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                ToolTip = $"Back to SQL Prompt's default ({DefaultLabel(setting)})",
                Visibility = Visibility.Collapsed,
                IsEnabled = !unsupported,
            };
            FormatStylesChrome.ApplyIconButton(reset);
            System.Windows.Automation.AutomationProperties.SetName(reset, $"Reset {setting.DisplayName} to SQL Prompt's default");
            reset.Click += (_, _) =>
            {
                _viewModel.ResetOption(setting.Id);
                RefreshVisibleSettingControls(); // the control shows the default again
            };
            _rowMarkers[setting.Id] = (setting, label, reset);
            ApplyChangeMarker(setting, label, reset);

            var row = new Grid();
            if (control is CheckBox checkBox)
            {
                checkBox.Content = label;
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ResetColumnWidth) });
                Grid.SetColumn(checkBox, 0);
                row.Children.Add(checkBox);
                if (badge != null)
                {
                    badge.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(badge, 1);
                    row.Children.Add(badge);
                }
                Grid.SetColumn(reset, 2);
                row.Children.Add(reset);
            }
            else if (control is ComboBox && NeedsOwnLine(setting))
            {
                // A drop-down whose longest choice can't fit beside its label (the parenthesis styles:
                // "Compact: closing parenthesis right-aligned") goes on its own line under the label,
                // so no choice is ever cut off.
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ResetColumnWidth) });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                FrameworkElement labelCell = label;
                if (badge != null)
                {
                    badge.HorizontalAlignment = HorizontalAlignment.Left;
                    badge.Margin = new Thickness(0, 2, 0, 0);
                    labelCell = new StackPanel { Orientation = Orientation.Vertical, Children = { label, badge } };
                }
                row.Children.Add(labelCell);
                Grid.SetColumn(reset, 1);
                row.Children.Add(reset);
                var cell = new Grid { Margin = new Thickness(0, Spacing.Xs, 0, 0) };
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 340 });
                cell.Children.Add(control);
                Grid.SetRow(cell, 1);
                row.Children.Add(cell);
            }
            else
            {
                // 160 and 38%, not 200 and 45%: beside the preview the form is narrower, and a wider label column cut
                // the choices off ("Expanded: to stateme…"); labels wrap instead.
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "lbl", MinWidth = 160 });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ResetColumnWidth) });
                Grid.SetColumn(reset, 2);
                row.Children.Add(reset);

                if (_settingControlsHost != null)
                {
                    label.SetBinding(FrameworkElement.MaxWidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth))
                    {
                        Source = _settingControlsHost,
                        Converter = LabelMaxWidthConverter.Instance,
                    });
                }

                FrameworkElement labelCell = label;
                if (badge != null)
                {
                    badge.HorizontalAlignment = HorizontalAlignment.Left;
                    badge.Margin = new Thickness(0, 2, 0, 0);
                    labelCell = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { label, badge },
                    };
                }
                labelCell.Margin = new Thickness(0, 0, Spacing.Sm, 0);
                Grid.SetColumn(labelCell, 0);
                row.Children.Add(labelCell);

                // The control fills a cell at most 280 px wide that starts at the column's left edge,
                // so every control on the page begins at the same x. (A capped Stretch control
                // would be centred in the column instead.)
                var cell = new Grid();
                cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 280 });
                cell.Children.Add(control);
                Grid.SetColumn(cell, 1);
                row.Children.Add(cell);
            }

            // Spec 040 (T103, STY-07): the note, and why a dependent option is disabled, read under
            // the row instead of only in its tooltip.
            var body = new StackPanel { Orientation = Orientation.Vertical };
            body.Children.Add(row);
            if (!string.IsNullOrWhiteSpace(setting.Note))
                body.Children.Add(RowNote(setting.Note!));
            var hint = RowNote(GateText(setting) ?? string.Empty);
            hint.Visibility = gateOpen || setting.EnabledWhenId == null ? Visibility.Collapsed : Visibility.Visible;
            body.Children.Add(hint);
            if (!unsupported && setting.EnabledWhenId != null) _gatedRows.Add(new GatedRow(setting, label, control, hint));

            rowBorder.Child = body;
            return rowBorder;
        }

        /// <summary>
        /// The widest choice text a drop-down beside its label can show whole at the default size
        /// (its cell is about 217 px, less the arrow and padding).
        /// </summary>
        private const double OwnLineChoiceWidth = 190;

        /// <summary>True when the option's longest choice is too wide for a drop-down beside its label.</summary>
        private bool NeedsOwnLine(FormatSettingNode setting)
        {
            var choices = setting.EnumLabels ?? setting.AllowedEnumValues;
            if (choices == null) return false;
            double pixelsPerDip = 1.0;
            try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch (Exception) { /* not in a visual tree yet */ }
            var typeface = new Typeface(Typography.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            foreach (var choice in choices)
            {
                var text = new FormattedText(choice, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, Typography.Body, System.Windows.Media.Brushes.Black, pixelsPerDip);
                if (text.WidthIncludingTrailingWhitespace > OwnLineChoiceWidth) return true;
            }
            return false;
        }

        /// <summary>The ↺ column every row ends in, shown or not, so controls end at one edge.</summary>
        private const double ResetColumnWidth = 30;

        private static TextBlock RowNote(string text)
        {
            var note = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                Margin = new Thickness(0, 2, 0, 0),
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            return note;
        }

        /// <summary>"Takes effect when "X" is on." for an option another option turns on; otherwise null.</summary>
        private string? GateText(FormatSettingNode setting)
        {
            if (setting.EnabledWhenId == null) return null;
            var gate = _currentGroup?.Settings.FirstOrDefault(x => x.Id == setting.EnabledWhenId);
            var value = setting.EnabledWhenValue is bool b ? (b ? "on" : "off") : setting.EnabledWhenValue?.ToString();
            return $"Takes effect when \"{gate?.DisplayName ?? setting.EnabledWhenId}\" is {value}.";
        }

        /// <summary>Spec 040 (T078) — an option label takes at most 38% of the page's width (45% before the preview moved beside the form).</summary>
        private sealed class LabelMaxWidthConverter : System.Windows.Data.IValueConverter
        {
            internal static readonly LabelMaxWidthConverter Instance = new LabelMaxWidthConverter();

            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => value is double width && width > 0 ? width * 0.38 : double.PositiveInfinity;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => throw new NotSupportedException();
        }

        /// <summary>The label in full, then the description, the option's "shows when…" note, and why it is disabled.</summary>
        private string? RowTooltip(FormatSettingNode setting, bool gateOpen)
        {
            var parts = new System.Collections.Generic.List<string> { setting.DisplayName };
            if (!string.IsNullOrWhiteSpace(setting.Description)) parts.Add(setting.Description!);
            if (!string.IsNullOrWhiteSpace(setting.Note)) parts.Add(setting.Note!);
            if (!gateOpen && GateText(setting) is string gateText) parts.Add(gateText);
            return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        /// <summary>
        /// Spec 040 (T103) — ▲/▼ beside a number option: steps by 1 within its range. Setting the
        /// text runs the box's own validation, which records the value.
        /// </summary>
        private static FrameworkElement BuildStepper(FormatSettingNode setting, TextBox textBox, bool isDisabled)
        {
            var up = StepButton("\u25B2", "Increase");
            var down = StepButton("\u25BC", "Decrease");
            up.IsEnabled = down.IsEnabled = !isDisabled;

            void Step(int delta)
            {
                var start = int.TryParse(textBox.Text, out var v) ? v
                    : int.TryParse(setting.DefaultJson.Trim('"'), out var d) ? d : setting.Min ?? 0;
                var next = start + delta;
                if (setting.Min is int min && next < min) next = min;
                if (setting.Max is int max && next > max) next = max;
                var text = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (textBox.Text != text) textBox.Text = text;
            }
            up.Click += (_, _) => Step(1);
            down.Click += (_, _) => Step(-1);

            var stepper = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            stepper.Children.Add(up);
            stepper.Children.Add(down);
            return stepper;
        }

        private static RepeatButton StepButton(string glyph, string name)
        {
            var button = new RepeatButton
            {
                Content = glyph,
                FontSize = 7,
                Width = 18,
                Height = 13,
                Padding = new Thickness(0),
                Focusable = false,
                ToolTip = name,
            };
            FormatStylesChrome.ApplyIconButton(button);
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            return button;
        }

        /// <summary>
        /// Returns the type-appropriate WPF control for one setting. The control is wired
        /// to <c>viewModel.SetWorkingValue</c> on change so the live preview refreshes
        /// (debounced 100 ms via <c>QueuePreviewAsync</c>).
        /// </summary>
        private FrameworkElement BuildControlForSetting(FormatSettingNode setting, object? currentValue, bool isDisabled)
        {
            // Control choice comes from FormatStylesSchemaModel.ControlKindFor — the single
            // source of truth the degrade tests assert against (spec 033 simplify pass: the
            // window previously mirrored the decision inline, letting the two drift).
            switch (FormatStylesSchemaModel.ControlKindFor(setting))
            {
                case FormatStylesSchemaModel.ControlKind.CheckBox:
                {
                    var initial = currentValue is bool b ? b : ParseBool(setting.DefaultJson);
                    // BuildSettingRow makes the option's label this checkbox's content (SQL Prompt layout).
                    var checkBox = new CheckBox
                    {
                        IsChecked = initial,
                        IsEnabled = !isDisabled,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        FontFamily = Typography.UiFont,
                        FontSize = Typography.Body,
                    };
                    checkBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                    if (!isDisabled)
                    {
                        checkBox.Checked += (_, _) => { _viewModel.SetWorkingValue(setting.Id, true); RefreshIfGate(setting); };
                        checkBox.Unchecked += (_, _) => { _viewModel.SetWorkingValue(setting.Id, false); RefreshIfGate(setting); };
                    }
                    return checkBox;
                }
                case FormatStylesSchemaModel.ControlKind.IntBox:
                {
                    var initial = currentValue?.ToString() ?? setting.DefaultJson.Trim('"');

                    var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
                    var textBox = new TextBox
                    {
                        Text = initial,
                        Width = 72,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        IsEnabled = !isDisabled,
                        FontFamily = Typography.UiFont,
                        FontSize = Typography.Body,
                        Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                    };
                    textBox.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
                    textBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                    textBox.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
                    row.Children.Add(textBox);
                    row.Children.Add(BuildStepper(setting, textBox, isDisabled));

                    // Spec 033 (T023) — visible range hint when the v2 schema declares one.
                    if (setting.Min != null || setting.Max != null)
                    {
                        var rangeHint = new TextBlock
                        {
                            Text = $"({setting.Min?.ToString() ?? "…"} – {setting.Max?.ToString() ?? "…"})",
                            FontFamily = Typography.UiFont,
                            FontSize = Typography.Small,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(Spacing.Sm, 0, 0, 0),
                        };
                        rangeHint.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                        row.Children.Add(rangeHint);
                    }

                    if (!isDisabled)
                    {
                        textBox.TextChanged += (_, _) =>
                        {
                            var valid = int.TryParse(textBox.Text, out var n)
                                        && (setting.Min == null || n >= setting.Min)
                                        && (setting.Max == null || n <= setting.Max);
                            if (valid)
                            {
                                textBox.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
                                textBox.ToolTip = null;
                                _viewModel.SetWorkingValue(setting.Id, int.Parse(textBox.Text));
                            }
                            else
                            {
                                // Rejected before preview/save — the last valid value stays effective.
                                textBox.BorderBrush = InvalidInputBrush;
                                textBox.ToolTip = setting.Min != null || setting.Max != null
                                    ? $"Enter a whole number between {setting.Min?.ToString() ?? "-∞"} and {setting.Max?.ToString() ?? "∞"}."
                                    : "Enter a whole number.";
                            }
                        };
                    }
                    return row;
                }
                case FormatStylesSchemaModel.ControlKind.EnumComboBox:
                {
                    // Spec 033 (T023) — v2 schemas carry AllowedEnumValues: themed ComboBox
                    // (plain-string items per the ComboBoxTheming contract; the selected entry
                    // persists verbatim, exact spelling).
                    var initial = currentValue?.ToString() ?? setting.DefaultJson.Trim('"');
                    var allowed = setting.AllowedEnumValues!;

                    var combo = new ComboBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        IsEnabled = !isDisabled,
                        FontFamily = Typography.UiFont,
                        FontSize = Typography.Body,
                    };
                    // Items are the display labels (plain strings, per the ComboBoxTheming contract);
                    // the stored value is mapped back on change, keeping Redgate's exact spelling.
                    foreach (var v in allowed) combo.Items.Add(setting.LabelFor(v));
                    // An imported profile may hold a value outside the declared set —
                    // surface it as a selectable extra rather than lying about the state.
                    if (!allowed.Contains(initial, StringComparer.Ordinal)) combo.Items.Insert(0, initial);
                    combo.SelectedItem = setting.LabelFor(initial);
                    Ui.Theme.ComboBoxTheming.Apply(combo);
                    if (!isDisabled)
                    {
                        combo.SelectionChanged += (_, _) =>
                        {
                            if (combo.SelectedItem is string s)
                            {
                                _viewModel.SetWorkingValue(setting.Id, setting.ValueFor(s));
                                RefreshIfGate(setting);
                            }
                        };
                    }
                    return combo;
                }
                case FormatStylesSchemaModel.ControlKind.EnumTextBox:
                {
                    // v1-schema degrade — no AllowedEnumValues: legacy free-text box.
                    var initial = currentValue?.ToString() ?? setting.DefaultJson.Trim('"');
                    var textBox = new TextBox
                    {
                        Text = initial,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        IsEnabled = !isDisabled,
                        FontFamily = Typography.UiFont,
                        FontSize = Typography.Body,
                        Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                    };
                    textBox.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
                    textBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                    textBox.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
                    if (!isDisabled)
                    {
                        textBox.TextChanged += (_, _) =>
                        {
                            _viewModel.SetWorkingValue(setting.Id, textBox.Text);
                        };
                    }
                    return textBox;
                }
                default:
                {
                    var readonlyText = new TextBlock
                    {
                        Text = $"({setting.Type}) {currentValue ?? setting.DefaultJson}",
                        FontFamily = Typography.MonoFont,
                        FontSize = Typography.Body,
                    };
                    readonlyText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                    return readonlyText;
                }
            }
        }

        private static bool ParseBool(string defaultJson)
        {
            return defaultJson.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------
        // Lifecycle / wiring
        // -----------------------------------------------------------------

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Set Owner to the DTE main window so the dialog centres correctly.
            // Failure here is non-fatal (e.g. running outside VS during a test).
            TrySetDteOwner();
            // Centred on a maximised SSMS, the 900 px window could open with its buttons under the
            // taskbar: keep it on its monitor's work area once it has its place.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => AkmlSql.Shell.Shared.Ui.WindowBounds.KeepInWorkArea(this)));

            UpdateStatus("Loading…");
            await _viewModel.LoadAsync().ConfigureAwait(true);

            if (!string.IsNullOrEmpty(_viewModel.SchemaJson))
            {
                RebuildSettingsTreeFromSchema(_viewModel.SchemaJson!);
            }

            // SQL Prompt model: preview each page's own sample by default, and offer the engine's
            // SELECT examples next to it.
            PopulateSourceCombo();
            ScheduleExamples();

            // The view-model auto-selects the ACTIVE style at open; reflect that in the list.
            // Assigning SelectedItem fires the normal selection-changed flow (SelectProfileAsync
            // short-circuits on the already-loaded style) so the controls render its values.
            if (_styleList != null && _styleList.SelectedItem == null && _viewModel.LoadedProfileName != null)
            {
                _styleList.SelectedItem = _viewModel.Profiles.FirstOrDefault(
                    p => string.Equals(p.Name, _viewModel.LoadedProfileName, StringComparison.OrdinalIgnoreCase));
            }

            UpdateStatus(_viewModel.LastError ?? $"Loaded {_viewModel.Profiles.Count} style(s).");
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // The VM raises INotifyPropertyChanged from whatever thread it happens to be on:
            // LoadAsync resumes on a thread-pool thread after its ConfigureAwait(false) awaits, so
            // its `finally { IsLoading = false; }` fires off-dispatcher; the debounced preview
            // refresh (PreviewText) fires from a background Task. Every branch below mutates
            // thread-affine WPF controls, so marshal the whole handler to the window's own
            // dispatcher once here rather than guarding each branch individually. (Was: the
            // IsLoading branch wrote _statusText.Text from the pool thread, crashing editor open
            // with "The calling thread cannot access this object because a different thread owns it".)
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnViewModelPropertyChanged(sender, e)));
                return;
            }

            if (e.PropertyName == nameof(FormatStylesEditorViewModel.IsLoading) && _statusText != null)
            {
                UpdateStatus(_viewModel.IsLoading ? "Loading…" : (_viewModel.LastError ?? $"{_viewModel.Profiles.Count} style(s)."));
                // The list (and therefore the active style + count) is settled once loading ends.
                if (!_viewModel.IsLoading) UpdateHeaderState();
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.PreviewText) && _previewView != null)
            {
                // The sample being edited is in its own box (spec 033 T025), so a formatted-preview
                // refresh never clobbers the user's typing.
                _previewView.Text = _viewModel.PreviewText;

                // Spec 040 (T099, STY-06): after an option edit, the lines it moved light up for 2 s.
                var moved = _viewModel.MovedLines;
                _previewView.HighlightLines = moved;
                if (moved.Count > 0)
                {
                    _movedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    _movedTimer.Stop();
                    _movedTimer.Tick -= OnMovedTimerTick;
                    _movedTimer.Tick += OnMovedTimerTick;
                    _movedTimer.Start();
                    // The flash used to happen below the fold: bring the first moved line into view.
                    var first = moved.Min();
                    var view = _previewView;
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => view.ScrollLineIntoView(first)));
                }
                // The option examples format with the working style: refresh them once it settles.
                ScheduleExamples();
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.PreviewTabSize) && _previewView != null)
            {
                // Spec 040 (STY-02): tabs line up at the selected style's own width.
                _previewView.TabSize = _viewModel.PreviewTabSize;
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.TeamFolderUnavailable))
            {
                UpdateTeamUnavailableRow();   // spec 040 (T187)
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.PreviewValidationError))
            {
                // T070 — toggle the warning bar above the preview pane.
                UpdatePreviewWarningBar();
                // A failed live preview still ends the wait: examples it cancelled can run again.
                ScheduleExamples();
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.IsDirty)
                     || e.PropertyName == nameof(FormatStylesEditorViewModel.IsSelectedBuiltIn)
                     || e.PropertyName == nameof(FormatStylesEditorViewModel.IsSelectedCustomized)
                     || e.PropertyName == nameof(FormatStylesEditorViewModel.IsSelectedClassic)
                     || e.PropertyName == nameof(FormatStylesEditorViewModel.IsSelectedReadOnly))
            {
                // Spec 033 — both flip on the UI thread (SetWorkingValue / SelectProfileAsync).
                UpdateSaveButtonState();
                UpdateReadOnlyState();
                UpdateHeaderState();   // dirty / read-only are reported in the header too
                if (e.PropertyName == nameof(FormatStylesEditorViewModel.IsSelectedReadOnly))
                {
                    _examplesSignature = null;   // "Use this" follows the style's read-only state
                    ScheduleExamples();
                }
            }
        }

        private void OnMovedTimerTick(object? sender, EventArgs e)
        {
            _movedTimer?.Stop();
            if (_previewView != null) _previewView.HighlightLines = new int[0];
        }

        private void UpdatePreviewWarningBar()
        {
            if (_previewWarningBar == null || _previewWarningText == null) return;
            var msg = _viewModel.PreviewValidationError;
            if (string.IsNullOrEmpty(msg))
            {
                _previewWarningBar.Visibility = Visibility.Collapsed;
                _previewWarningText.Text = string.Empty;
            }
            else
            {
                _previewWarningText.Text = msg;
                _previewWarningBar.Visibility = Visibility.Visible;
            }
        }

        private void UpdateStatus(string text)
        {
            if (_statusText != null) _statusText.Text = text;
        }

        private void TrySetDteOwner()
        {
            try
            {
                var dte = (EnvDTE.DTE?)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                var hwnd = (IntPtr)dte.MainWindow.HWnd;
                if (hwnd != IntPtr.Zero)
                {
                    new System.Windows.Interop.WindowInteropHelper(this).Owner = hwnd;
                }
            }
            catch
            {
                // Owner is best-effort; CenterOwner falls back to CenterScreen if Owner can't be set.
            }
        }

        // -----------------------------------------------------------------
        // Public launch helper — callers (Options Format page button,
        // ExternalTools menu, debug pad, tests) hit this to open the editor.
        // -----------------------------------------------------------------

        /// <summary>
        /// Opens a fresh editor on the current process. Returns when the user closes it.
        /// </summary>
        public static void Launch()
        {
            try
            {
                var vm = new FormatStylesEditorViewModel();
                // Spec 030 T019 / FR-008 — capture the active editor's SQL so the preview can run
                // against it (set before constructing the window so the toggle reflects availability).
                vm.CurrentQueryText = TryGetActiveDocumentText() ?? string.Empty;
                var window = new FormatStylesEditorWindow(vm);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "FormatStylesEditor: launch failed");
                MessageBox.Show(
                    "Failed to open Format Styles Editor: " + ex.Message,
                    "AKML SQL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Best-effort capture of the active editor's full text via DTE (spec 030 T019 / FR-008).
        /// Works in SSMS 22 (Pattern B — DTE.ActiveDocument, not IVsTextManager
        /// which is unreliable outside a command Execute). Returns null when there is no active
        /// SQL document.
        /// </summary>
        private static string? TryGetActiveDocumentText()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is not EnvDTE.DTE dte) return null;
                var doc = dte.ActiveDocument;
                if (doc?.Object("TextDocument") is not EnvDTE.TextDocument td) return null;
                var text = td.StartPoint.CreateEditPoint().GetText(td.EndPoint);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FormatStylesEditor: capturing active document text failed (sample preview used)");
                return null;
            }
        }
    }

    /// <summary>Internal DTO bound to each settings-tree leaf.</summary>
    internal sealed class FormatSettingNode
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Type { get; set; } = "Other";
        public string Status { get; set; } = "Implemented";
        public string? SqlPromptKey { get; set; }
        public string DefaultJson { get; set; } = "null";

        // Spec 033 (T022) — schema-v2 enrichment; all null when talking to a v1 engine.
        public string? Description { get; set; }
        public System.Collections.Generic.List<string>? AllowedEnumValues { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }

        // SQL Prompt model — null / empty on the AKML settings schema.
        /// <summary>Display text for each entry of <see cref="AllowedEnumValues"/> (same order).</summary>
        public System.Collections.Generic.List<string>? EnumLabels { get; set; }
        /// <summary>"Shows when…" note: when the option's effect depends on other settings.</summary>
        public string? Note { get; set; }
        /// <summary>Sub-heading on the page ("New lines", "ON"…).</summary>
        public string? Subgroup { get; set; }
        /// <summary>The setting that turns this one on, and the value that does.</summary>
        public string? EnabledWhenId { get; set; }
        public object? EnabledWhenValue { get; set; }
        /// <summary>SQL Prompt model (schema 2002+): what each value does, as example SQL to format.</summary>
        public FormatStylesSchemaModel.OptionExample? Example { get; set; }

        /// <summary>Label shown for a stored value (the value itself when there is no label).</summary>
        public string LabelFor(string value)
        {
            if (AllowedEnumValues == null || EnumLabels == null || EnumLabels.Count != AllowedEnumValues.Count) return value;
            var i = AllowedEnumValues.IndexOf(value);
            return i >= 0 ? EnumLabels[i] : value;
        }

        /// <summary>Stored value for a displayed label (the label itself when it is not one).</summary>
        public string ValueFor(string label)
        {
            if (AllowedEnumValues == null || EnumLabels == null || EnumLabels.Count != AllowedEnumValues.Count) return label;
            var i = EnumLabels.IndexOf(label);
            return i >= 0 ? AllowedEnumValues[i] : label;
        }
    }
}
