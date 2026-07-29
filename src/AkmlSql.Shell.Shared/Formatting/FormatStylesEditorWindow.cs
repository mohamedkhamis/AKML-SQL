#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.StatusBar;
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
    ///   <item>Left: style list with built-in vs Native badge, IsReadOnly lock icon.</item>
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
        private TextBox? _previewTextBox;
        private Border? _previewWarningBar;
        private TextBlock? _previewWarningText;
        private TextBlock? _statusText;

        // Spec 033 (T016) — editing UX state
        private Button? _saveBtn;

        /// <summary>First-class "Set as active" affordance under the style list — activation was
        /// previously reachable only from the per-row ⋮ / right-click menu.</summary>
        private Button? _setActiveButton;

        // Header state line — the window narrates what it is editing and what Format SQL will use.
        private TextBlock? _headerSubject;
        private Border? _headerReadOnlyChip;   // fixed label — visibility only
        private Border? _headerDirtyChip;      // fixed label — visibility only
        private Border? _headerActiveChip;
        private TextBlock? _headerActiveChipText;
        private TextBlock? _stylesHeader;
        private Border? _readOnlyHint;
        // SQL Prompt-parity redesign: the right pane edits a whole settings *group* (SQL Prompt's
        // "page") at once, not one setting at a time. _currentGroup is the group whose form is
        // showing; _currentGroupCategory is its parent category (for the breadcrumb title).
        private FormatStylesSchemaModel.Group? _currentGroup;
        private string? _currentGroupCategory;
        private TextBlock? _breadcrumbText;
        private bool _suppressSelectionChanged;
        private bool _closeConfirmed;

        // Spec 033 (T025 / FR-014, closes spec-020 T069) — in-window preview-sample editing.
        // Editing mode is DERIVED from the toggle (no shadow flag to desync); edits commit in
        // one batch on toggle-off / close instead of per keystroke (each PreviewSample set is
        // ~5 synchronous filesystem ops on the dispatcher thread plus a discarded preview run).
        private CheckBox? _editSampleToggle;
        private bool EditingSample => _editSampleToggle?.IsChecked == true;

        private static System.Windows.Media.SolidColorBrush Freeze(System.Windows.Media.SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        // Semantic invalid-input red (theme-independent per CLAUDE.md); hoisted — control
        // builders run on every tree-node click.
        private static readonly System.Windows.Media.SolidColorBrush InvalidInputBrush =
            Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x14, 0x00)));

        // Fixed dark editor palette for the live-preview card — theme-independent in BOTH light and
        // dark, matching SQL Prompt (its preview renders on a dark editor panel regardless of theme).
        // CLAUDE.md allows fixed colours for a surface that must read the same in every theme; the
        // theme-token EditorPopupBackground is white in light theme, so it can't serve here.
        private static readonly System.Windows.Media.SolidColorBrush PreviewBgBrush =
            Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x22, 0x30)));
        private static readonly System.Windows.Media.SolidColorBrush PreviewTextBrush =
            Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD4, 0xD8, 0xE0)));
        private static readonly System.Windows.Media.SolidColorBrush PreviewMutedBrush =
            Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x93, 0xA5)));
        private static readonly System.Windows.Media.SolidColorBrush PreviewCaptionBrush =
            Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6A, 0xC4, 0x7A)));
        private static readonly System.Windows.Media.SolidColorBrush PreviewWarnTextBrush =
            Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x1A, 0x00)));

        public FormatStylesEditorWindow(FormatStylesEditorViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            Title = "AKML SQL — Format Styles Editor";
            Width = 1060;
            Height = 680;
            MinWidth = 920;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            HasHelpButton = false;

            // Ensure theme resources are merged so SetResourceReference resolves.
            ThemeRegistry.Instance.AttachTo(this);

            BuildUi();
            DataContext = _viewModel;

            // Spec 033 — the VM asks the window what to do with unsaved edits on style switch.
            _viewModel.DirtyDecisionHandler = PromptStyleSwitchDecisionAsync;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Loaded += OnLoaded;
        }

        // -----------------------------------------------------------------

        private void BuildUi()
        {
            var res = ThemeRegistry.Instance.Resources;
            var fg = (SolidColorBrush)res[ThemeTokens.TextPrimary];

            // Outer grid: header / content / footer
            var root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceCanvas);
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
            // own title — the title bar already says "Format Styles Editor"; a header that echoes it
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
                FontSize = Typography.H4,
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
            _headerReadOnlyChip = MakeHeaderChip("Built-in · read-only", accent: false, out _);
            _headerDirtyChip = MakeHeaderChip("Unsaved changes", accent: true, out _);
            _headerActiveChip = MakeHeaderChip("Active: —", accent: true, out _headerActiveChipText);
            _headerActiveChip.ToolTip = "The style Format SQL uses";
            chips.Children.Add(_headerReadOnlyChip);
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
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });       // styles
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Sm) }); // gutter/splitter
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });       // style options
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Sm) }); // gutter/splitter
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 }); // settings + preview

            content.Children.Add(BuildLeftPanel());
            content.Children.Add(MakeColumnSplitter(1));
            content.Children.Add(BuildMiddlePanel());
            content.Children.Add(MakeColumnSplitter(3));
            content.Children.Add(BuildRightPanel());

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
                FontSize = Typography.Body,
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
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
                MinWidth = 84,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                IsEnabled = false,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            _saveBtn.Click += async (_, _) =>
            {
                try
                {
                    SetStatus(await _viewModel.SaveAsync()
                        ? $"Saved '{_viewModel.LoadedProfileName}'."
                        : _viewModel.LastError ?? "Save failed.");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "FormatStylesEditor: save failed");
                    SetStatus(ex.Message);
                }
            };
            footerButtons.Children.Add(_saveBtn);

            var closeBtn = new Button
            {
                Content = "Close",
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
                MinWidth = 84,
                IsCancel = true,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
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

        // -----------------------------------------------------------------
        // Left panel — style list
        // -----------------------------------------------------------------
        private FrameworkElement BuildLeftPanel()
        {
            var res = ThemeRegistry.Instance.Resources;
            var accentBrush = res[ThemeTokens.AccentPrimary] as System.Windows.Media.Brush;
            var selectionBrush = res[ThemeTokens.SurfaceSelection] as System.Windows.Media.Brush;
            var selectionStrongBrush = res[ThemeTokens.SurfaceSelectionStrong] as System.Windows.Media.Brush;
            var textPrimaryBrush = res[ThemeTokens.TextPrimary] as System.Windows.Media.Brush;

            var panel = new Grid { Margin = new Thickness(Spacing.Sm) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // header
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // list
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // + New Style CTA

            _stylesHeader = MakeSectionHeader("STYLES");
            Grid.SetRow(_stylesHeader, 0);
            panel.Children.Add(_stylesHeader);

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

            // Themed selection colours; the active style additionally gets an accent-tinted card via
            // the container style below (SQL Prompt: active = tinted card + accent border + ✔).
            if (selectionStrongBrush != null) _styleList.Resources[SystemColors.HighlightBrushKey] = selectionStrongBrush;
            if (textPrimaryBrush != null) _styleList.Resources[SystemColors.HighlightTextBrushKey] = textPrimaryBrush;
            if (selectionBrush != null) _styleList.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selectionBrush;
            if (textPrimaryBrush != null) _styleList.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = textPrimaryBrush;

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Spacing.Xs, Spacing.Xs, Spacing.Xs, Spacing.Xs)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, System.Windows.Media.Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            var activeTrigger = new System.Windows.DataTrigger
            {
                Binding = new System.Windows.Data.Binding(nameof(StyleListItem.IsActive)),
                Value = true,
            };
            if (selectionBrush != null) activeTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selectionBrush));
            if (accentBrush != null) activeTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, accentBrush));
            itemStyle.Triggers.Add(activeTrigger);
            _styleList.ItemContainerStyle = itemStyle;

            // Spec 033 (T036) — sectioned list: "YOUR STYLES" first, then "BUILT-IN STYLES",
            // names A→Z within each; group headers via a code-built template (upper-cased).
            var view = new System.Windows.Data.ListCollectionView(_viewModel.Profiles);
            view.GroupDescriptions!.Add(new System.Windows.Data.PropertyGroupDescription(
                nameof(StyleListItem.Section), new UpperCaseConverter()));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(StyleListItem.IsReadOnly), System.ComponentModel.ListSortDirection.Ascending)); // editable first — robust to section-label rewording
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(StyleListItem.Name), System.ComponentModel.ListSortDirection.Ascending));
            _styleList.ItemsSource = view;

            var groupHeaderTemplate = new DataTemplate();
            var headerFactory = new FrameworkElementFactory(typeof(TextBlock));
            headerFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            headerFactory.SetValue(TextBlock.FontWeightProperty, Typography.WeightSemiBold);
            headerFactory.SetValue(TextBlock.FontSizeProperty, (double)Typography.Small);
            headerFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 8, 2, 3));
            if (res[ThemeTokens.TextSecondary] is System.Windows.Media.Brush secBrush)
                headerFactory.SetValue(TextBlock.ForegroundProperty, secBrush);
            groupHeaderTemplate.VisualTree = headerFactory;
            _styleList.GroupStyle.Add(new GroupStyle { HeaderTemplate = groupHeaderTemplate });

            // Per-style ⋮ menu — opened by right-click AND the row's visible ⋮ glyph; enablement
            // recomputed on every open so it tracks the selected row.
            var menu = new ContextMenu();
            var miSetActive = MakeMenuItem("Set Active", OnSetActiveAsync);
            var miCopy = MakeMenuItem("Copy", OnCopyStyleAsync);
            var miRename = MakeMenuItem("Rename…", OnRenameStyleAsync);
            var miDelete = MakeMenuItem("Delete", OnDeleteStyleAsync);
            var miExport = MakeMenuItem("Export…", OnExportAsync);
            menu.Items.Add(miSetActive);
            menu.Items.Add(miCopy);
            menu.Items.Add(miRename);
            menu.Items.Add(miDelete);
            menu.Items.Add(new Separator());
            menu.Items.Add(miExport);
            menu.Opened += (_, _) =>
            {
                if (_styleList?.SelectedItem is StyleListItem selected)
                {
                    miRename.IsEnabled = !selected.IsReadOnly;
                    miDelete.IsEnabled = !selected.IsReadOnly && !selected.IsActive;
                    miSetActive.IsEnabled = !selected.IsActive;
                }
            };
            _styleList.ContextMenu = menu;
            _styleList.ContextMenuOpening += (_, e) =>
            {
                if (_styleList?.SelectedItem is not StyleListItem) e.Handled = true; // nothing selected — no menu
            };

            _styleList.SelectionChanged += async (_, _) => await OnStyleSelectionChangedAsync();
            // Spec 033 — double-clicking a read-only built-in copies it (Redgate behavior).
            _styleList.MouseDoubleClick += async (_, _) =>
            {
                if (_styleList?.SelectedItem is StyleListItem { IsReadOnly: true })
                {
                    try { await OnCopyStyleAsync(); }
                    catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: double-click copy failed"); SetStatus(ex.Message); }
                }
            };
            Grid.SetRow(_styleList, 1);
            panel.Children.Add(_styleList);

            // Activation used to live ONLY in the per-row ⋮ / right-click menu, so selecting a style
            // (which merely highlights it) looked like it should have applied — the reported "selecting
            // Khamis Style doesn't mark it". A first-class button makes the select→activate step
            // explicit and keeps the ⋮ menu working for users who already know it.
            var listFooter = new StackPanel { Orientation = Orientation.Vertical };

            _setActiveButton = new Button
            {
                Content = "Set as active style",
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                Margin = new Thickness(0, 0, 0, Spacing.Xs),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                IsEnabled = false,   // enabled by OnStyleSelectionChangedAsync for a non-active row
                ToolTip = "Make the selected style the one Format SQL uses",
            };
            _setActiveButton.Click += async (_, _) =>
            {
                try { await OnSetActiveAsync(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: set-active failed"); SetStatus(ex.Message); }
            };
            listFooter.Children.Add(_setActiveButton);

            listFooter.Children.Add(MakeAccentCtaButton("+ New Style", OnNewStyleAsync));
            Grid.SetRow(listFooter, 2);
            panel.Children.Add(listFooter);

            return MakePaneCard(0, panel);
        }

        /// <summary>
        /// A compact header status chip. <paramref name="accent"/> chips carry the accent token
        /// (state the user must notice: unsaved work, which style is active); neutral chips use the
        /// muted token (context only). Starts collapsed — <see cref="UpdateHeaderState"/> shows it.
        /// </summary>
        private Border MakeHeaderChip(string text, bool accent, out TextBlock label)
        {
            label = new TextBlock
            {
                Text = text,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty,
                accent ? ThemeTokens.AccentPrimary : ThemeTokens.TextSecondary);

            var chip = new Border
            {
                Child = label,
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(Spacing.Xs, 1, Spacing.Xs, 1),
                Margin = new Thickness(Spacing.Xs, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            chip.SetResourceReference(Border.BorderBrushProperty,
                accent ? ThemeTokens.AccentPrimary : ThemeTokens.BorderSubtle);
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

            if (_headerReadOnlyChip != null)
                _headerReadOnlyChip.Visibility = _viewModel.IsSelectedReadOnly && !string.IsNullOrEmpty(editing)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (_headerDirtyChip != null)
                _headerDirtyChip.Visibility = _viewModel.IsDirty && !_viewModel.IsSelectedReadOnly
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
                _stylesHeader.Text = count > 0 ? $"STYLES · {count}" : "STYLES";
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
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            // async-void click handler is the WPF event idiom; guarded so a faulted task can't crash the host.
            btn.Click += async (_, _) =>
            {
                try { await onClick(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: action '{Action}' failed", content); SetStatus(ex.Message); }
            };
            return btn;
        }

        /// <summary>Outlined accent call-to-action ("+ New Style") — Border-based so the accent
        /// border/text survive the host's default button chrome.</summary>
        private FrameworkElement MakeAccentCtaButton(string content, Func<System.Threading.Tasks.Task> onClick)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(Spacing.Sm, Spacing.Xs + 1, Spacing.Sm, Spacing.Xs + 1),
                Margin = new Thickness(Spacing.Xs, Spacing.Sm, Spacing.Xs, Spacing.Xs),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = System.Windows.Media.Brushes.Transparent,
                Focusable = true, // keyboard-reachable — this is the only path to "New style"
            };
            border.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.AccentPrimary);
            var label = new TextBlock
            {
                Text = content,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                FontWeight = Typography.WeightSemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.AccentPrimary);
            border.Child = label;

            async System.Threading.Tasks.Task Invoke()
            {
                try { await onClick(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: action '{Action}' failed", content); SetStatus(ex.Message); }
            }
            void Tint() => border.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceSelection);
            void Clear() => border.Background = System.Windows.Media.Brushes.Transparent;
            border.MouseEnter += (_, _) => Tint();
            border.MouseLeave += (_, _) => { if (!border.IsKeyboardFocused) Clear(); };
            border.GotKeyboardFocus += (_, _) => Tint();      // visible focus state
            border.LostKeyboardFocus += (_, _) => Clear();
            border.MouseLeftButtonUp += async (_, _) => await Invoke();
            border.KeyDown += async (_, e) =>
            {
                if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space)
                {
                    e.Handled = true;
                    await Invoke();
                }
            };
            return border;
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
            if (_setActiveButton != null)
            {
                _setActiveButton.IsEnabled = !item.IsActive;
                _setActiveButton.ToolTip = item.IsActive
                    ? $"'{item.Name}' is already the active style"
                    : $"Make '{item.Name}' the style Format SQL uses";
            }

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

            // (Save-button + read-only visuals sync via the IsDirty/IsSelectedReadOnly
            // PropertyChanged handler — no direct calls needed here.)
            RefreshVisibleSettingControls();
            UpdateHeaderState();   // the header names the style now being edited
            SetStatus(_viewModel.IsSelectedReadOnly
                ? $"'{item.Name}' is built-in (read-only) — copy this style to edit it."
                : $"Loaded '{item.Name}'.");
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
            if (_saveBtn != null)
                _saveBtn.IsEnabled = _viewModel.IsDirty && !_viewModel.IsSelectedReadOnly;
        }

        private void UpdateReadOnlyState()
        {
            if (_settingControlsHost != null)
                _settingControlsHost.IsEnabled = !_viewModel.IsSelectedReadOnly;
            if (_readOnlyHint != null)
                _readOnlyHint.Visibility = _viewModel.IsSelectedReadOnly ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The ONE Save / Discard / Cancel prompt (style switch + window close share it).</summary>
        private StyleSwitchDecision PromptSaveDecision(string message)
        {
            var result = MessageBox.Show(
                this,
                message,
                "AKML SQL — Format Styles",
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

            if (!_closeConfirmed && _viewModel.IsDirty && !_viewModel.IsSelectedReadOnly)
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
            var (accepted, name, basedOn) = StyleNameDialog.ShowNewStyle(this, candidates, SelectedStyle() ?? "Default");
            if (!accepted) return;

            var created = await _viewModel.CreateStyleAsync(name, basedOn);
            AfterCreate(created, $"Created '{created}' based on '{basedOn}'.");
        }

        private async System.Threading.Tasks.Task OnRenameStyleAsync()
        {
            var current = SelectedStyle();
            if (string.IsNullOrEmpty(current)) { SetStatus("Select a style to rename."); return; }
            var item = _viewModel.Profiles.FirstOrDefault(p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase));
            if (item?.IsReadOnly == true) { SetStatus("Built-in styles cannot be renamed."); return; }

            var (accepted, newName) = StyleNameDialog.ShowRename(this, current!);
            if (!accepted || string.Equals(newName, current, StringComparison.Ordinal)) return;

            var wasActive = item?.IsActive == true;
            var finalName = await _viewModel.RenameSelectedAsync(newName);
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

            var confirm = MessageBox.Show(
                this,
                $"Delete style '{current}'? This cannot be undone.",
                "AKML SQL — Format Styles",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            SetStatus(await _viewModel.DeleteSelectedAsync()
                ? $"Deleted '{current}'."
                : _viewModel.LastError ?? "Delete failed.");
        }

        private async System.Threading.Tasks.Task OnCopyStyleAsync()
        {
            var source = SelectedStyle();
            if (string.IsNullOrEmpty(source)) { SetStatus("Select a style to copy."); return; }
            var created = await _viewModel.CopyProfileAsync(source!);
            AfterCreate(created, $"Copied '{source}'");
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
                if (_setActiveButton != null)
                {
                    _setActiveButton.IsEnabled = false;
                    _setActiveButton.ToolTip = $"'{name}' is already the active style";
                }

                UpdateHeaderState();   // "Active: <name>" follows immediately
            }
            else
            {
                SetStatus(_viewModel.LastError ?? "Could not set active style.");
            }
        }

        private async System.Threading.Tasks.Task OnExportAsync()
        {
            var name = SelectedStyle();
            if (string.IsNullOrEmpty(name)) { SetStatus("Select a style to export."); return; }
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export formatting style",
                FileName = name + ".sqlpromptstylev2",
                Filter = "SQL Prompt style (*.sqlpromptstylev2)|*.sqlpromptstylev2|All files (*.*)|*.*",
                DefaultExt = ".sqlpromptstylev2",
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(this) != true) return;
            if (await _viewModel.ExportProfileAsync(name!, dialog.FileName))
                SetStatus($"Exported '{name}'");
            else
                SetStatus(_viewModel.LastError ?? "Export failed.");
        }

        /// <summary>
        /// Spec 031 FR-010/FR-011/FR-012 — imports a SQL Prompt style file (JSON or legacy XML)
        /// via <see cref="FormatStylesEditorViewModel.ImportProfileAsync"/>, selects + activates
        /// the resulting style, and shows a per-option summary dialog.
        /// </summary>
        private async System.Threading.Tasks.Task OnImportAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import SQL Prompt style",
                Filter = "SQL Prompt style (*.json;*.sqlpromptstylev2)|*.json;*.sqlpromptstylev2|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) != true) return;

            var stem = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            var peekedName = TryPeekStyleName(dialog.FileName, out var kind);

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
            // skip the confirmation — the engine rejects it with a clear error, nothing saved.
            string? collisionName = kind == StyleFileKind.Unknown ? null : (peekedName ?? stem);
            var existing = collisionName == null
                ? null
                : _viewModel.Profiles.FirstOrDefault(p =>
                    !p.IsReadOnly && string.Equals(p.Name, collisionName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                var confirm = MessageBox.Show(
                    this,
                    $"Style '{existing.Name}' already exists. Overwrite?",
                    "AKML SQL",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);
                if (confirm != MessageBoxResult.OK)
                {
                    SetStatus("Import cancelled.");
                    return;
                }
            }

            var response = await _viewModel.ImportProfileAsync(dialog.FileName, targetName);

            // Engine rejects built-in collisions; custom collisions overwrite by ProfileManager
            // semantics, so the confirm above (against the client-side list) is the only gate.
            if (response != null && response.Success && response.ProfileName != null)
            {
                AfterCreate(response.ProfileName, BuildImportSummary(response));
                if (_viewModel.SetActiveProfile(response.ProfileName))
                    UpdateStatusBarActiveStyle(response.ProfileName); // FR-011 — import + set active
                else
                    SetStatus(_viewModel.LastError ?? "Imported, but could not set active style.");
                ShowImportSummaryDialog(response);                    // FR-012 — import itself succeeded
            }
            else
            {
                SetStatus(_viewModel.LastError ?? "Import failed.");
            }
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
            // Row:  [✔ active] Name [Kind] .......... [⋮]
            // The ✔ marks the active style (accent-coloured); the ⋮ (docked right) opens the
            // shared per-style context menu against its own row. Uses WPF's built-in
            // BooleanToVisibilityConverter — no resources needed.
            var boolToVis = new System.Windows.Controls.BooleanToVisibilityConverter();
            var accent = ThemeRegistry.Instance.Resources[ThemeTokens.AccentPrimary];

            var template = new DataTemplate(typeof(StyleListItem));

            var dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);
            dock.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 2, 0, 2));

            var menuGlyph = new FrameworkElementFactory(typeof(TextBlock));
            menuGlyph.SetValue(TextBlock.TextProperty, "⋮");
            menuGlyph.SetValue(DockPanel.DockProperty, Dock.Right);
            menuGlyph.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            menuGlyph.SetValue(TextBlock.FontSizeProperty, (double)Typography.H4);
            menuGlyph.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 4, 0));
            menuGlyph.SetValue(UIElement.OpacityProperty, 0.6);
            menuGlyph.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
            menuGlyph.SetValue(FrameworkElement.ToolTipProperty, "Style actions");
            menuGlyph.AddHandler(UIElement.MouseLeftButtonUpEvent,
                new System.Windows.Input.MouseButtonEventHandler(OnRowMenuGlyphClick));
            dock.AppendChild(menuGlyph);

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(StyleListItem.Name)));
            name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            stack.AppendChild(name);

            // The active style reads as an explicit "ACTIVE" pill rather than the previous bare "✔ "
            // prefix. A lone check glyph was easy to miss and gave no hint that it means "this is the
            // style Format SQL will use" — the exact confusion behind "selecting a style doesn't mark
            // it" (selecting only highlights a row; activating is a separate action).
            var activeBadge = new FrameworkElementFactory(typeof(Border));
            activeBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            activeBadge.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            activeBadge.SetValue(Control.PaddingProperty, new Thickness(4, 0, 4, 0));
            activeBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            activeBadge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            activeBadge.SetValue(FrameworkElement.ToolTipProperty, "Format SQL uses this style");
            if (accent is System.Windows.Media.Brush accentBorder)
                activeBadge.SetValue(Border.BorderBrushProperty, accentBorder);
            activeBadge.SetBinding(UIElement.VisibilityProperty,
                new System.Windows.Data.Binding(nameof(StyleListItem.IsActive)) { Converter = boolToVis });

            var activeText = new FrameworkElementFactory(typeof(TextBlock));
            activeText.SetValue(TextBlock.TextProperty, "ACTIVE");
            activeText.SetValue(TextBlock.FontSizeProperty, 9.0);
            activeText.SetValue(TextBlock.FontWeightProperty, Typography.WeightSemiBold);
            activeText.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (accent is System.Windows.Media.Brush accentFg)
                activeText.SetValue(TextBlock.ForegroundProperty, accentFg);
            activeBadge.AppendChild(activeText);
            stack.AppendChild(activeBadge);

            var kind = new FrameworkElementFactory(typeof(TextBlock));
            kind.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(StyleListItem.Kind)));
            kind.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 1, 0, 0));
            kind.SetValue(UIElement.OpacityProperty, 0.55);
            kind.SetValue(TextBlock.FontSizeProperty, 10.0);
            kind.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            stack.AppendChild(kind);

            dock.AppendChild(stack);
            template.VisualTree = dock;
            return template;
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
            var res = ThemeRegistry.Instance.Resources;

            var panel = new Grid { Margin = new Thickness(Spacing.Sm) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = MakeSectionHeader("STYLE OPTIONS");
            Grid.SetRow(header, 0);
            panel.Children.Add(header);

            _settingsTree = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            _settingsTree.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            ScrollViewer.SetHorizontalScrollBarVisibility(_settingsTree, ScrollBarVisibility.Disabled);

            // Themed selection: the selected group leaf gets an accent bar + on-accent text
            // (SQL Prompt look). Groups are the selectable leaves; categories only expand.
            if (res[ThemeTokens.AccentPrimary] is System.Windows.Media.Brush accent)
            {
                _settingsTree.Resources[SystemColors.HighlightBrushKey] = accent;
                _settingsTree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = accent;
            }
            if (res[ThemeTokens.TextOnAccent] is System.Windows.Media.Brush onAccent)
            {
                _settingsTree.Resources[SystemColors.HighlightTextBrushKey] = onAccent;
                _settingsTree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = onAccent;
            }
            Grid.SetRow(_settingsTree, 1);
            panel.Children.Add(_settingsTree);

            return MakePaneCard(2, panel);
        }

        // -----------------------------------------------------------------
        // Right panel — settings form for the selected group (top) + live preview (bottom)
        // -----------------------------------------------------------------
        private FrameworkElement BuildRightPanel()
        {
            var panel = new Grid();
            Grid.SetColumn(panel, 4);
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58, GridUnitType.Star) }); // form
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Spacing.Sm) });            // splitter
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42, GridUnitType.Star) }); // preview

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
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                Margin = new Thickness(0, Spacing.Xs, 0, Spacing.Sm),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _breadcrumbText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.AccentPrimary);
            Grid.SetRow(_breadcrumbText, 0);
            formGrid.Children.Add(_breadcrumbText);

            // Spec 033 (T016) — read-only hint shown while a built-in style is loaded.
            _readOnlyHint = new Border
            {
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(Spacing.Sm),
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
            };
            _readOnlyHint.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceHover);
            _readOnlyHint.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            var readOnlyHintText = new TextBlock
            {
                Text = "This built-in style is read-only — use Copy to create an editable version.",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
            };
            readOnlyHintText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _readOnlyHint.Child = readOnlyHintText;
            Grid.SetRow(_readOnlyHint, 1);
            formGrid.Children.Add(_readOnlyHint);

            var formScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            _settingControlsHost = new StackPanel { Orientation = Orientation.Vertical };
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
            Grid.SetRow(formCard, 0);
            panel.Children.Add(formCard);

            // ── Splitter (horizontal, invisible in the 8px gutter row) ─────
            var hSplitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, // fill the 8px gutter row so it stays draggable
                ResizeDirection = GridResizeDirection.Rows,
                ShowsPreview = false,
                Background = System.Windows.Media.Brushes.Transparent,
            };
            Grid.SetRow(hSplitter, 1);
            panel.Children.Add(hSplitter);

            // ── Live preview card (fixed dark editor panel in both themes, à la SQL Prompt) ──
            var previewCard = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            previewCard.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            previewCard.Background = PreviewBgBrush;

            var previewGrid = new Grid();
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // header + source controls
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // warning bar
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // preview text
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // caption

            // Header: LIVE PREVIEW (left) + preview-source controls (right).
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
            };
            previewLabel.Foreground = PreviewMutedBrush;
            Grid.SetColumn(previewLabel, 0);
            previewHeader.Children.Add(previewLabel);

            // Spec 030 T019 / FR-008 — preview the active style against the sample OR the SQL from
            // the editor that was open when this dialog launched.
            var sourceStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var rbSample = new RadioButton
            {
                Content = "Sample",
                GroupName = "akmlPreviewSource",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Md, 0),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
            };
            rbSample.Foreground = PreviewTextBrush;
            var rbCurrent = new RadioButton
            {
                Content = "Current query",
                GroupName = "akmlPreviewSource",
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                // Disabled (with a hint) when no editor query was captured at launch.
                IsEnabled = _viewModel.HasCurrentQuery,
                ToolTip = _viewModel.HasCurrentQuery ? null : "No active SQL editor when this dialog opened.",
            };
            rbCurrent.Foreground = PreviewTextBrush;
            rbSample.Checked += (_, _) =>
            {
                _viewModel.PreviewSourceMode = FormatPreviewSource.Sample;
                if (_editSampleToggle != null) _editSampleToggle.IsEnabled = true;
            };
            rbCurrent.Checked += (_, _) =>
            {
                _viewModel.PreviewSourceMode = FormatPreviewSource.CurrentQuery;
                // Sample editing only applies to the Sample source.
                if (_editSampleToggle != null)
                {
                    _editSampleToggle.IsChecked = false;
                    _editSampleToggle.IsEnabled = false;
                }
            };

            // Spec 033 (T025 / FR-014) — edit the persisted preview sample in place. While
            // checked, the preview box shows the RAW sample (editable, persisted atomically
            // via the PreviewSample setter on every change); unchecking restores the live
            // formatted preview.
            _editSampleToggle = new CheckBox
            {
                Content = "Edit sample",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Spacing.Md, 0, 0, 0),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                ToolTip = "Edit the sample SQL the preview formats. Changes persist across sessions.",
            };
            _editSampleToggle.Foreground = PreviewTextBrush;
            _editSampleToggle.Checked += (_, _) =>
            {
                if (_previewTextBox == null) return;
                _previewTextBox.IsReadOnly = false;
                _previewTextBox.Text = _viewModel.PreviewSample;
            };
            _editSampleToggle.Unchecked += (_, _) =>
            {
                if (_previewTextBox == null) return;
                CommitSampleEdit(); // one persist + one preview refresh for the whole edit session
                _previewTextBox.IsReadOnly = true;
                _previewTextBox.Text = _viewModel.PreviewText;
            };

            sourceStack.Children.Add(rbSample);
            sourceStack.Children.Add(rbCurrent);
            sourceStack.Children.Add(_editSampleToggle);
            Grid.SetColumn(sourceStack, 1);
            previewHeader.Children.Add(sourceStack);
            Grid.SetRow(previewHeader, 0);
            previewGrid.Children.Add(previewHeader);

            _previewWarningBar = new Border
            {
                Padding = new Thickness(Spacing.Md, Spacing.Sm, Spacing.Md, Spacing.Sm),
                Visibility = Visibility.Collapsed,
                BorderThickness = new Thickness(0, 1, 0, 1),
            };
            // Amber/yellow is a semantic colour per CLAUDE.md's allow-list. Near-solid fill + fixed
            // dark text so the strip reads on the dark preview panel in both themes.
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

            _previewTextBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = Typography.MonoFont,
                FontSize = Typography.Body,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "-- The live preview appears once the schema loads and a style is selected.",
            };
            _previewTextBox.Foreground = PreviewTextBrush;
            Grid.SetRow(_previewTextBox, 2);
            previewGrid.Children.Add(_previewTextBox);

            var caption = new TextBlock
            {
                Text = "Preview updates as you change settings.",
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                Margin = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Sm),
            };
            caption.Foreground = PreviewCaptionBrush;
            Grid.SetRow(caption, 3);
            previewGrid.Children.Add(caption);

            previewCard.Child = previewGrid;
            Grid.SetRow(previewCard, 2);
            panel.Children.Add(previewCard);

            return panel;
        }

        // -----------------------------------------------------------------
        // Schema → TreeView
        // -----------------------------------------------------------------
        private void RebuildSettingsTreeFromSchema(string schemaJson)
        {
            if (_settingsTree == null) return;

            _settingsTree.Items.Clear();
            TreeViewItem? firstLeaf = null;

            try
            {
                // Spec 033 (T022) — parsing lives in the testable FormatStylesSchemaModel;
                // this method only renders WPF nodes from the model.
                var model = FormatStylesSchemaModel.Parse(schemaJson);

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
        }

        /// <summary>A selectable settings *group* (SQL Prompt "page"); selecting it renders the
        /// group's whole settings list as a form on the right, under a "Category › Group" title.</summary>
        private TreeViewItem BuildGroupLeaf(FormatStylesSchemaModel.Group group, string? categoryDisplay)
        {
            var leaf = new TreeViewItem
            {
                Header = group.DisplayName,
                FontWeight = FontWeights.Normal, // counteract the inherited semi-bold category weight
                Tag = group,
            };
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

            if (_breadcrumbText != null)
                _breadcrumbText.Text = categoryDisplay != null
                    ? $"{categoryDisplay}  ›  {group.DisplayName}"
                    : group.DisplayName;

            _settingControlsHost.Children.Clear();

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
            foreach (var setting in group.Settings)
                _settingControlsHost.Children.Add(BuildSettingRow(setting, index++));
        }

        /// <summary>One form row: setting label (left; +Unsupported badge; description as a tooltip)
        /// and its type-driven control (right). Alternate rows get a subtle zebra tint.</summary>
        private FrameworkElement BuildSettingRow(FormatSettingNode setting, int index)
        {
            var isDisabled = string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase);
            var currentValue = _viewModel.GetWorkingValue(setting.Id);

            var rowBorder = new Border
            {
                Padding = new Thickness(Spacing.Sm, Spacing.Xs + 1, Spacing.Sm, Spacing.Xs + 1),
                CornerRadius = new CornerRadius(3),
            };
            if (index % 2 == 1)
                rowBorder.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceCanvas); // zebra

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
            };
            var label = new TextBlock
            {
                Text = setting.DisplayName,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                ToolTip = string.IsNullOrWhiteSpace(setting.Description) ? null : setting.Description,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, isDisabled ? ThemeTokens.TextDisabled : ThemeTokens.TextSecondary);
            labelStack.Children.Add(label);
            if (isDisabled) labelStack.Children.Add(BuildUnsupportedBadge());
            Grid.SetColumn(labelStack, 0);
            row.Children.Add(labelStack);

            // Each control sets its own horizontal alignment (checkbox left; combos/text boxes
            // stretch to fill the column up to MaxWidth); the row only caps and centres them.
            var control = BuildControlForSetting(setting, currentValue, isDisabled);
            control.VerticalAlignment = VerticalAlignment.Center;
            control.MaxWidth = 280;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);

            rowBorder.Child = row;
            return rowBorder;
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
                    // Bare checkbox — the setting name is the row label to its left (SQL Prompt layout).
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
                        checkBox.Checked += (_, _) => _viewModel.SetWorkingValue(setting.Id, true);
                        checkBox.Unchecked += (_, _) => _viewModel.SetWorkingValue(setting.Id, false);
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
                    foreach (var v in allowed) combo.Items.Add(v);
                    // An imported profile may hold a value outside the declared set —
                    // surface it as a selectable extra rather than lying about the state.
                    if (!allowed.Contains(initial, StringComparer.Ordinal)) combo.Items.Insert(0, initial);
                    combo.SelectedItem = initial;
                    Ui.Theme.ComboBoxTheming.Apply(combo);
                    if (!isDisabled)
                    {
                        combo.SelectionChanged += (_, _) =>
                        {
                            if (combo.SelectedItem is string s)
                                _viewModel.SetWorkingValue(setting.Id, s);
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

            UpdateStatus("Loading…");
            await _viewModel.LoadAsync().ConfigureAwait(true);

            if (!string.IsNullOrEmpty(_viewModel.SchemaJson))
            {
                RebuildSettingsTreeFromSchema(_viewModel.SchemaJson!);
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
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.PreviewText) && _previewTextBox != null)
            {
                // Spec 033 (T025): while the user is editing the sample, the box shows the RAW
                // sample — a formatted-preview refresh must not clobber their typing.
                if (!EditingSample) _previewTextBox.Text = _viewModel.PreviewText;
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.PreviewValidationError))
            {
                // T070 — toggle the warning bar above the preview pane.
                UpdatePreviewWarningBar();
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.IsDirty)
                     || e.PropertyName == nameof(FormatStylesEditorViewModel.IsSelectedReadOnly))
            {
                // Spec 033 — both flip on the UI thread (SetWorkingValue / SelectProfileAsync).
                UpdateSaveButtonState();
                UpdateReadOnlyState();
                UpdateHeaderState();   // dirty / read-only are reported in the header too
            }
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
        /// Works in both SSMS 22 and VS 2026 (Pattern B — DTE.ActiveDocument, not IVsTextManager
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
    }
}
