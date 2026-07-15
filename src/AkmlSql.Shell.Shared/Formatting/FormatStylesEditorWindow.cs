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
    /// Programmatic WPF only (no XAML) — matches the established pattern in
    /// <see cref="Ui.ProfileEditorDialog"/>. Chrome flows from <see cref="ThemeRegistry"/>
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

        private static System.Windows.Media.SolidColorBrush Freeze(System.Windows.Media.SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        public FormatStylesEditorWindow(FormatStylesEditorViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            Title = "AKML SQL — Format Styles Editor";
            Width = 1000;
            Height = 680;
            MinWidth = 800;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            HasHelpButton = false;

            // Ensure theme resources are merged so SetResourceReference resolves.
            ThemeRegistry.Instance.AttachTo(this);

            BuildUi();
            DataContext = _viewModel;

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
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
            var headerTitle = new TextBlock
            {
                Text = "Edit Formatting Styles",
                FontFamily = Typography.UiFont,
                FontSize = Typography.H4,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            headerTitle.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            headerStack.Children.Add(headerTitle);
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Content (three columns + 2 splitters) ──────────────────────
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });   // splitter
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });   // splitter
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

            // Left: style list
            content.Children.Add(BuildLeftPanel());

            // Splitter
            var leftSplitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ShowsPreview = false,
            };
            leftSplitter.SetResourceReference(Control.BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetColumn(leftSplitter, 1);
            content.Children.Add(leftSplitter);

            // Middle: settings tree
            content.Children.Add(BuildMiddlePanel());

            // Splitter
            var rightSplitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ShowsPreview = false,
            };
            rightSplitter.SetResourceReference(Control.BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetColumn(rightSplitter, 3);
            content.Children.Add(rightSplitter);

            // Right: controls (top) + preview (bottom)
            content.Children.Add(BuildRightPanel());

            Grid.SetRow(content, 1);
            root.Children.Add(content);

            // ── Footer (status + buttons) ──────────────────────────────────
            var footer = new Border
            {
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
                BorderThickness = new Thickness(0, 1, 0, 0),
            };
            footer.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            footer.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfacePanel);

            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = string.Empty,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Md, 0),
            };
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            Grid.SetColumn(_statusText, 0);
            footerGrid.Children.Add(_statusText);

            var closeBtn = new Button
            {
                Content = "Close",
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
                MinWidth = 80,
                IsCancel = true,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            closeBtn.Click += (_, _) => Close();
            Grid.SetColumn(closeBtn, 1);
            footerGrid.Children.Add(closeBtn);

            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        // -----------------------------------------------------------------
        // Left panel — style list
        // -----------------------------------------------------------------
        private FrameworkElement BuildLeftPanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // toolbar (T020)
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = "Styles",
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                Margin = new Thickness(Spacing.Md, Spacing.Md, Spacing.Md, Spacing.Sm),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetRow(label, 0);
            panel.Children.Add(label);

            // Spec 030 T020 / FR-007 — New / Copy / Set Active / Export toolbar.
            var toolbar = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, Spacing.Sm),
            };
            toolbar.Children.Add(MakeToolbarButton("New", OnNewStyleAsync));
            toolbar.Children.Add(MakeToolbarButton("Copy", OnCopyStyleAsync));
            toolbar.Children.Add(MakeToolbarButton("Set Active", OnSetActiveAsync));
            toolbar.Children.Add(MakeToolbarButton("Import…", OnImportAsync));
            toolbar.Children.Add(MakeToolbarButton("Export", OnExportAsync));
            Grid.SetRow(toolbar, 1);
            panel.Children.Add(toolbar);

            _styleList = new ListBox
            {
                Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, Spacing.Sm),
                BorderThickness = new Thickness(1),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                ItemTemplate = BuildStyleListItemTemplate(),
            };
            _styleList.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
            _styleList.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            _styleList.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
            _styleList.ItemsSource = _viewModel.Profiles;
            _styleList.SelectionChanged += (_, _) =>
            {
                if (_styleList.SelectedItem is StyleListItem item)
                {
                    _viewModel.SelectedProfileName = item.Name;
                }
            };
            Grid.SetRow(_styleList, 2);
            panel.Children.Add(_styleList);

            Grid.SetColumn(panel, 0);
            return panel;
        }

        private Button MakeToolbarButton(string content, Func<System.Threading.Tasks.Task> onClick)
        {
            var btn = new Button
            {
                Content = content,
                Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
                Margin = new Thickness(0, 0, Spacing.Xs, 0),
                MinWidth = 56,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
            };
            // async-void click handler is the WPF event idiom; guarded so a faulted task can't crash the host.
            btn.Click += async (_, _) =>
            {
                try { await onClick(); }
                catch (Exception ex) { Log.Warning(ex, "FormatStylesEditor: toolbar action '{Action}' failed", content); SetStatus(ex.Message); }
            };
            return btn;
        }

        /// <summary>The currently-selected style name, or null when nothing is selected.</summary>
        private string? SelectedStyle()
            => (_styleList?.SelectedItem as StyleListItem)?.Name ?? _viewModel.SelectedProfileName;

        private async System.Threading.Tasks.Task OnNewStyleAsync()
        {
            var created = await _viewModel.NewProfileAsync();
            AfterCreate(created, "New style created");
        }

        private async System.Threading.Tasks.Task OnCopyStyleAsync()
        {
            var source = SelectedStyle();
            if (string.IsNullOrEmpty(source)) { SetStatus("Select a style to copy."); return; }
            var created = await _viewModel.CopyProfileAsync(source!);
            AfterCreate(created, $"Copied '{source}'");
        }

        private System.Threading.Tasks.Task OnSetActiveAsync()
        {
            var name = SelectedStyle();
            if (string.IsNullOrEmpty(name)) { SetStatus("Select a style to make active."); return System.Threading.Tasks.Task.CompletedTask; }
            if (_viewModel.SetActiveProfile(name!))
            {
                SetStatus($"Active style: {name}");
                UpdateStatusBarActiveStyle(name!);
            }
            else
            {
                SetStatus(_viewModel.LastError ?? "Could not set active style.");
            }
            return System.Threading.Tasks.Task.CompletedTask;
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

            // Legacy XML exports carry no internal style name — for an untargeted import the
            // engine's SqlPromptImporter hardcodes "Imported from SQL Prompt", so consecutive
            // XML imports would silently overwrite each other while a stem-based collision
            // check sees nothing. Name XML imports after the file instead (TargetProfileName
            // drives SqlPromptImporter's profile name). JSON must NOT get a target name: the
            // Task 8 handler overrides metadata.name with TargetProfileName when present,
            // which would break JSON naming (the internal metadata.name must win).
            string? targetName = kind == StyleFileKind.Xml ? stem : null;

            // FR-008 — collision check against the client-side list before sending.
            // JSON: the peeked metadata.name (the engine derives the profile name from it),
            // falling back to the stem when the file has none. XML: the stem we just chose as
            // the target name. Unrecognized/malformed content: skip the confirmation — the
            // engine rejects it with a clear error and nothing is saved.
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

        private static DataTemplate BuildStyleListItemTemplate()
        {
            // Programmatic DataTemplate. Renders: [lock-glyph if read-only]  Name  [Built-in badge]
            const string xaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Grid Margin='4,3,4,3'>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width='Auto' />
      <ColumnDefinition Width='*' />
      <ColumnDefinition Width='Auto' />
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column='0' Text='&#x1F512; ' Margin='0,0,4,0'
               Visibility='{Binding IsReadOnly, Converter={StaticResource BoolToVisibilityConverter}}' />
    <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center' />
    <TextBlock Grid.Column='2' Text='{Binding Kind}' Margin='6,0,0,0' Opacity='0.6'
               FontSize='10' VerticalAlignment='Center' />
  </Grid>
</DataTemplate>";
            try
            {
                // The {StaticResource BoolToVisibilityConverter} will fail without the converter
                // resource. Fall back to a code-built template if XAML parse fails.
                using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(xaml));
                return (DataTemplate)System.Windows.Markup.XamlReader.Load(stream);
            }
            catch
            {
                // Code-built fallback — simpler, no converter dependency.
                var template = new DataTemplate(typeof(StyleListItem));
                var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
                stackFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
                stackFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 3, 4, 3));

                var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
                nameFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
                nameFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                stackFactory.AppendChild(nameFactory);

                var kindFactory = new FrameworkElementFactory(typeof(TextBlock));
                kindFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Kind"));
                kindFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
                kindFactory.SetValue(UIElement.OpacityProperty, 0.6);
                kindFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
                kindFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                stackFactory.AppendChild(kindFactory);

                template.VisualTree = stackFactory;
                return template;
            }
        }

        // -----------------------------------------------------------------
        // Middle panel — settings tree built from schema JSON
        // -----------------------------------------------------------------
        private FrameworkElement BuildMiddlePanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = "Settings",
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                Margin = new Thickness(Spacing.Md, Spacing.Md, Spacing.Md, Spacing.Sm),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetRow(label, 0);
            panel.Children.Add(label);

            _settingsTree = new TreeView
            {
                Margin = new Thickness(Spacing.Sm, 0, Spacing.Sm, Spacing.Sm),
                BorderThickness = new Thickness(1),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            _settingsTree.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
            _settingsTree.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            _settingsTree.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
            _settingsTree.SelectedItemChanged += (_, e) =>
            {
                if (e.NewValue is FormatSettingNode setting)
                {
                    _viewModel.SelectedSettingId = setting.Id;
                    UpdateRightTopForSetting(setting);
                }
            };
            Grid.SetRow(_settingsTree, 1);
            panel.Children.Add(_settingsTree);

            Grid.SetColumn(panel, 2);
            return panel;
        }

        // -----------------------------------------------------------------
        // Right panel — controls (top, placeholder) + preview (bottom, placeholder)
        // -----------------------------------------------------------------
        private FrameworkElement BuildRightPanel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) }); // splitter
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40, GridUnitType.Star) });

            // Top: dynamic controls area — built per-selection by RenderControlsForSetting
            var topBorder = new Border
            {
                Margin = new Thickness(Spacing.Sm, Spacing.Md, Spacing.Md, Spacing.Sm),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(Spacing.Md),
            };
            topBorder.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            topBorder.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceInput);

            var topScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            _settingControlsHost = new StackPanel
            {
                Orientation = Orientation.Vertical,
            };
            _settingControlsEmpty = new TextBlock
            {
                Text = "Select a setting from the tree to edit it.",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            _settingControlsEmpty.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _settingControlsHost.Children.Add(_settingControlsEmpty);
            topScroll.Content = _settingControlsHost;
            topBorder.Child = topScroll;
            Grid.SetRow(topBorder, 0);
            panel.Children.Add(topBorder);

            // Splitter (horizontal)
            var hSplitter = new GridSplitter
            {
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                ResizeDirection = GridResizeDirection.Rows,
                ShowsPreview = false,
            };
            hSplitter.SetResourceReference(Control.BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetRow(hSplitter, 1);
            panel.Children.Add(hSplitter);

            // Bottom: live preview — read-only mono text bound to PreviewText, with an
            // optional warning bar above it (T070: shown when stage-6 SemanticValidator
            // rejects the formatted output for the current settings).
            var bottomBorder = new Border
            {
                Margin = new Thickness(Spacing.Sm, Spacing.Sm, Spacing.Md, Spacing.Md),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(0),
            };
            bottomBorder.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            bottomBorder.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            var bottomStack = new Grid();
            bottomStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // T019 source toggle
            bottomStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // warning bar
            bottomStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // preview

            // Spec 030 T019 / FR-008 — preview the active style against the sample OR the SQL from
            // the editor that was open when this dialog launched.
            var sourceBar = new Border
            {
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            sourceBar.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            var sourceStack = new StackPanel { Orientation = Orientation.Horizontal };
            var sourceLabel = new TextBlock
            {
                Text = "Preview:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
            };
            sourceLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
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
            rbSample.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
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
            rbCurrent.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            rbSample.Checked += (_, _) => _viewModel.PreviewSourceMode = FormatPreviewSource.Sample;
            rbCurrent.Checked += (_, _) => _viewModel.PreviewSourceMode = FormatPreviewSource.CurrentQuery;
            sourceStack.Children.Add(sourceLabel);
            sourceStack.Children.Add(rbSample);
            sourceStack.Children.Add(rbCurrent);
            sourceBar.Child = sourceStack;
            Grid.SetRow(sourceBar, 0);
            bottomStack.Children.Add(sourceBar);

            _previewWarningBar = new Border
            {
                Padding = new Thickness(Spacing.Md, Spacing.Sm, Spacing.Md, Spacing.Sm),
                Visibility = Visibility.Collapsed,
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            // Amber/yellow is a semantic colour per CLAUDE.md's allow-list — same in both themes.
            _previewWarningBar.Background = Freeze(new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x40, 0xFB, 0xBF, 0x24)));
            _previewWarningBar.BorderBrush = Freeze(new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24)));
            _previewWarningText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            _previewWarningText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            _previewWarningBar.Child = _previewWarningText;
            Grid.SetRow(_previewWarningBar, 1);
            bottomStack.Children.Add(_previewWarningBar);

            _previewTextBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = Typography.MonoFont,
                FontSize = Typography.Body,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(Spacing.Md),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "// Live preview will appear after the schema loads and a profile is selected.",
            };
            _previewTextBox.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetRow(_previewTextBox, 2);
            bottomStack.Children.Add(_previewTextBox);

            bottomBorder.Child = bottomStack;
            Grid.SetRow(bottomBorder, 2);
            panel.Children.Add(bottomBorder);

            Grid.SetColumn(panel, 4);
            return panel;
        }

        // -----------------------------------------------------------------
        // Schema → TreeView
        // -----------------------------------------------------------------
        private void RebuildSettingsTreeFromSchema(string schemaJson)
        {
            if (_settingsTree == null) return;

            _settingsTree.Items.Clear();

            try
            {
                using var doc = JsonDocument.Parse(schemaJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("groups", out var groupsEl) || !root.TryGetProperty("settings", out var settingsEl))
                {
                    return;
                }

                // Index settings by group for one-pass tree construction
                var settingsByGroup = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<FormatSettingNode>>(StringComparer.Ordinal);
                foreach (var s in settingsEl.EnumerateArray())
                {
                    var groupId = s.TryGetProperty("groupId", out var g) ? g.GetString() ?? string.Empty : string.Empty;
                    if (!settingsByGroup.TryGetValue(groupId, out var list))
                    {
                        list = new System.Collections.Generic.List<FormatSettingNode>();
                        settingsByGroup[groupId] = list;
                    }
                    list.Add(new FormatSettingNode
                    {
                        Id = s.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
                        DisplayName = s.TryGetProperty("displayName", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                        Type = s.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "Other" : "Other",
                        Status = s.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "Implemented" : "Implemented",
                        SqlPromptKey = s.TryGetProperty("sqlPromptKey", out var spEl) && spEl.ValueKind != JsonValueKind.Null ? spEl.GetString() : null,
                        DefaultJson = s.TryGetProperty("default", out var defEl) ? defEl.GetRawText() : "null",
                    });
                }

                foreach (var g in groupsEl.EnumerateArray())
                {
                    var groupId = g.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                    var displayName = g.TryGetProperty("displayName", out var nameEl) ? nameEl.GetString() ?? groupId : groupId;

                    var groupNode = new TreeViewItem
                    {
                        Header = displayName,
                        IsExpanded = true,
                    };
                    groupNode.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);

                    if (settingsByGroup.TryGetValue(groupId, out var children))
                    {
                        foreach (var child in children)
                        {
                            var childNode = new TreeViewItem { Header = BuildSettingNodeHeader(child) };
                            childNode.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
                            childNode.Tag = child;
                            childNode.Selected += (_, e) =>
                            {
                                // TreeView's SelectedItem is the data; but our items hold UIElements
                                // as Header. Track via Tag so SelectedItemChanged sees the data.
                                if (childNode.Tag is FormatSettingNode node)
                                {
                                    _viewModel.SelectedSettingId = node.Id;
                                    UpdateRightTopForSetting(node);
                                }
                                e.Handled = true;
                            };
                            groupNode.Items.Add(childNode);
                        }
                    }
                    _settingsTree.Items.Add(groupNode);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatStylesEditor: failed to parse schema JSON");
            }
        }

        private FrameworkElement BuildSettingNodeHeader(FormatSettingNode setting)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            var name = new TextBlock
            {
                Text = setting.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            stack.Children.Add(name);

            // FR-023: unsupported badge — small muted pill adjacent to the label
            if (string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase))
            {
                stack.Children.Add(BuildUnsupportedBadge());
            }

            return stack;
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
        /// Tier 2b — renders type-appropriate controls for the selected setting in the
        /// right-top panel. Bool → CheckBox; Int → numeric TextBox; Enum (string) → TextBox
        /// (free-form, AllowedEnumValues populated as suggestions later). Settings with
        /// <c>Status == "Unsupported"</c> render disabled per FR-023; their imported value
        /// is still visible.
        /// </summary>
        private void UpdateRightTopForSetting(FormatSettingNode setting)
        {
            if (_settingControlsHost == null) return;

            _settingControlsHost.Children.Clear();

            // Header — setting name + Unsupported badge (if applicable)
            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
            };
            var nameText = new TextBlock
            {
                Text = setting.DisplayName,
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            headerStack.Children.Add(nameText);
            if (string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase))
            {
                headerStack.Children.Add(BuildUnsupportedBadge());
            }
            _settingControlsHost.Children.Add(headerStack);

            // Metadata line — Type, ID, SQL Prompt key
            var metaText = new TextBlock
            {
                Text = $"Type: {setting.Type}    ID: {setting.Id}    " +
                       (setting.SqlPromptKey != null
                           ? $"SQL Prompt: {setting.SqlPromptKey}"
                           : "(AKML-only)"),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                Margin = new Thickness(0, 0, 0, Spacing.Md),
                TextWrapping = TextWrapping.Wrap,
            };
            metaText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _settingControlsHost.Children.Add(metaText);

            // Type-driven control
            var isDisabled = string.Equals(setting.Status, "Unsupported", StringComparison.OrdinalIgnoreCase);
            var currentValue = _viewModel.GetWorkingValue(setting.Id);
            var control = BuildControlForSetting(setting, currentValue, isDisabled);
            _settingControlsHost.Children.Add(control);
        }

        /// <summary>
        /// Returns the type-appropriate WPF control for one setting. The control is wired
        /// to <c>viewModel.SetWorkingValue</c> on change so the live preview refreshes
        /// (debounced 100 ms via <c>QueuePreviewAsync</c>).
        /// </summary>
        private FrameworkElement BuildControlForSetting(FormatSettingNode setting, object? currentValue, bool isDisabled)
        {
            switch (setting.Type)
            {
                case "Bool":
                {
                    var initial = currentValue is bool b ? b : ParseBool(setting.DefaultJson);
                    var checkBox = new CheckBox
                    {
                        Content = setting.DisplayName,
                        IsChecked = initial,
                        IsEnabled = !isDisabled,
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
                case "Int":
                {
                    var initial = currentValue?.ToString() ?? setting.DefaultJson.Trim('"');
                    var textBox = new TextBox
                    {
                        Text = initial,
                        Width = 100,
                        HorizontalAlignment = HorizontalAlignment.Left,
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
                            if (int.TryParse(textBox.Text, out var n))
                            {
                                _viewModel.SetWorkingValue(setting.Id, n);
                            }
                        };
                    }
                    return textBox;
                }
                case "Enum":
                {
                    var initial = currentValue?.ToString() ?? setting.DefaultJson.Trim('"');
                    // No AllowedEnumValues at this schema level yet — use a free-text TextBox,
                    // but mark string fields visually with a non-numeric width so they look
                    // different from Int controls.
                    var textBox = new TextBox
                    {
                        Text = initial,
                        Width = 240,
                        HorizontalAlignment = HorizontalAlignment.Left,
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
            if (e.PropertyName == nameof(FormatStylesEditorViewModel.IsLoading) && _statusText != null)
            {
                UpdateStatus(_viewModel.IsLoading ? "Loading…" : (_viewModel.LastError ?? $"{_viewModel.Profiles.Count} style(s)."));
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.PreviewText) && _previewTextBox != null)
            {
                // Marshal to UI thread — preview refresh fires from a background Task.
                if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _previewTextBox.Text = _viewModel.PreviewText;
                    }));
                }
                else
                {
                    _previewTextBox.Text = _viewModel.PreviewText;
                }
            }
            else if (e.PropertyName == nameof(FormatStylesEditorViewModel.PreviewValidationError))
            {
                // T070 — toggle the warning bar above the preview pane.
                if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(UpdatePreviewWarningBar));
                }
                else
                {
                    UpdatePreviewWarningBar();
                }
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
    }
}
