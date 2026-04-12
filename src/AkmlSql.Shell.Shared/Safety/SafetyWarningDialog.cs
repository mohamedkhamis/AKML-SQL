#nullable enable
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Safety;
using AkmlSql.Shell.Shared.Ui;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace AkmlSql.Shell.Shared.Safety
{
    /// <summary>
    /// WPF execution-warning dialog styled after Redgate SQL Prompt: theme-aware chrome,
    /// amber/red severity accents, per-warning cards, and inline type-to-confirm for DROP.
    /// </summary>
    internal sealed class SafetyWarningDialog : Window
    {
        // Semantic severity accents — intentionally theme-independent so warnings
        // look the same in light and dark mode.
        private static readonly Color AmberBorder = Color.FromRgb(0xFF, 0xC1, 0x07);
        private static readonly Color ErrorBorder = Color.FromRgb(0xDC, 0x35, 0x45);
        private static readonly Color BtnPrimary  = Color.FromRgb(0x00, 0x78, 0xD4);

        private static readonly FontFamily SegoeUiFont = new FontFamily("Segoe UI");
        private static readonly FontFamily ConsolasFont = new FontFamily("Consolas");

        /// <summary>When true after the dialog closes, suppress future warnings for these rule types.</summary>
        public bool SuppressForSession { get; private set; }

        private System.Windows.Controls.CheckBox? _suppressCheckBox;

        // Per-instance theme brushes, frozen once in Build().
        private SolidColorBrush _mutedBrush = null!;
        private SolidColorBrush _dividerBrush = null!;
        private SolidColorBrush _cardBgBrush = null!;
        private SolidColorBrush _chromeFgBrush = null!;
        private SolidColorBrush _accentBrush = null!;

        private SafetyWarningDialog() { }

        /// <summary>
        /// Creates a ready-to-show dialog for the given warnings. Caller owns the
        /// lifetime and should call <c>ShowDialog()</c> then inspect
        /// <see cref="SuppressForSession"/> and <see cref="Window.DialogResult"/>.
        /// Returns <c>true</c> from <see cref="Window.DialogResult"/> when the user
        /// clicks Execute; <c>false</c> (or <c>null</c>) when the user cancels.
        /// </summary>
        public static SafetyWarningDialog CreateForWarnings(
            SafetyWarningDto[] warnings,
            string? serverName,
            string? environmentLabel,
            string? envColorHex = null)
        {
            var dlg = new SafetyWarningDialog();
            dlg.Build(warnings, serverName, environmentLabel, envColorHex);
            dlg.TryAttachOwnerToHost();
            return dlg;
        }

        /// <summary>
        /// Parent the dialog to the VS/SSMS main window so it centers over the host
        /// and behaves modally against it. Silent no-op when DTE is unavailable.
        /// </summary>
        private void TryAttachOwnerToHost()
        {
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.MainWindow != null)
                {
                    new WindowInteropHelper(this).Owner = (IntPtr)dte.MainWindow.HWnd;
                }
            }
            catch
            {
                // Not critical if we can't set owner.
            }
        }

        private void Build(SafetyWarningDto[] warnings, string? serverName, string? envLabel, string? envColorHex)
        {
            var theme = ThemeManager.Instance;
            var chromeBg = Freeze(new SolidColorBrush(theme.Background));
            _chromeFgBrush = Freeze(new SolidColorBrush(theme.Foreground));
            _mutedBrush    = Freeze(new SolidColorBrush(theme.PlaceholderText));
            _dividerBrush  = Freeze(new SolidColorBrush(theme.Border));
            _cardBgBrush   = Freeze(new SolidColorBrush(theme.EditorPanelBackground));

            Title = "Execution Warning";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = chromeBg;
            Foreground = _chromeFgBrush;
            FontFamily = SegoeUiFont;
            FontSize = 13;

            // Classify warnings in a single pass.
            bool isError = false;
            bool isDrop = false;
            foreach (var w in warnings)
            {
                switch ((SafetyWarningType)w.WarningType)
                {
                    case SafetyWarningType.DropTable:
                    case SafetyWarningType.DropDatabase:
                        isDrop = true;
                        isError = true;
                        break;
                    case SafetyWarningType.DeleteWithoutWhere:
                    case SafetyWarningType.UpdateWithoutWhere:
                    case SafetyWarningType.MergeWithoutFilter:
                    case SafetyWarningType.DmlInsideJoinWithoutWhere:
                    case SafetyWarningType.UnsafeDmlInProcOrTrigger:
                        isError = true;
                        break;
                }
                if (isError && isDrop) break;
            }

            // Semantic accent — amber for confirmations, red for destructive ops.
            // Caller may override via env color for production-tinted banners.
            Color accentColor = isError ? ErrorBorder : AmberBorder;
            if (!string.IsNullOrEmpty(envColorHex))
            {
                try { accentColor = (Color)ColorConverter.ConvertFromString(envColorHex); }
                catch { /* fall back to severity accent */ }
            }
            _accentBrush = Freeze(new SolidColorBrush(accentColor));

            var root = new StackPanel();

            // Environment banner — only when the interceptor matched a known env rule.
            if (!string.IsNullOrEmpty(envLabel) &&
                !string.Equals(envLabel, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                root.Children.Add(new Border
                {
                    Background = _accentBrush,
                    Padding = new Thickness(16, 8, 16, 8),
                    Child = new TextBlock
                    {
                        Text = envLabel + (serverName != null ? $"  \u2022  {serverName}" : string.Empty),
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 13
                    }
                });
            }

            root.Children.Add(BuildHeader(isError, isDrop));
            root.Children.Add(BuildBody(warnings, serverName));

            // Inline type-to-confirm for DROP.
            System.Windows.Controls.TextBox? confirmBox = null;
            string? expectedObjectName = null;
            if (isDrop)
            {
                var dropWarning = warnings.FirstOrDefault(w =>
                    (SafetyWarningType)w.WarningType is SafetyWarningType.DropTable or SafetyWarningType.DropDatabase);
                expectedObjectName = dropWarning?.ObjectName;
                if (!string.IsNullOrEmpty(expectedObjectName))
                {
                    confirmBox = new System.Windows.Controls.TextBox
                    {
                        FontFamily = ConsolasFont,
                        FontSize = 13,
                        Padding = new Thickness(8, 6, 8, 6),
                        BorderBrush = _dividerBrush,
                        BorderThickness = new Thickness(1)
                    };
                    root.Children.Add(BuildConfirmPanel(expectedObjectName!, confirmBox));
                }
            }

            root.Children.Add(BuildFooter(isDrop, expectedObjectName, confirmBox, out var cancelBtn));

            Content = root;

            // FR-005 — Cancel is the default focus so Enter/Space defaults to "don't execute".
            Loaded += (_, _) => cancelBtn.Focus();
        }

        private Border BuildHeader(bool isError, bool isDrop)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "\u26A0",
                FontSize = 22,
                Foreground = _accentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = isDrop ? "You are about to DROP an object"
                     : isError ? "This statement may affect all rows"
                     : "Execution requires confirmation",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = _chromeFgBrush,
                VerticalAlignment = VerticalAlignment.Center
            });

            return new Border
            {
                BorderBrush = _accentBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 12, 16, 12),
                Child = stack
            };
        }

        private StackPanel BuildBody(SafetyWarningDto[] warnings, string? serverName)
        {
            var body = new StackPanel { Margin = new Thickness(16, 14, 16, 0) };

            if (!string.IsNullOrEmpty(serverName))
            {
                var ctx = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                ctx.Children.Add(new TextBlock { Text = "Target: ", Foreground = _mutedBrush, FontSize = 12 });
                ctx.Children.Add(new TextBlock
                {
                    Text = serverName,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Foreground = _chromeFgBrush
                });
                body.Children.Add(ctx);
            }

            foreach (var w in warnings)
            {
                body.Children.Add(BuildWarningCard(w));
            }

            return body;
        }

        private Border BuildWarningCard(SafetyWarningDto w)
        {
            var stack = new StackPanel();

            stack.Children.Add(new Border
            {
                Background = _accentBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
                Child = new TextBlock
                {
                    Text = GetRuleLabel((SafetyWarningType)w.WarningType),
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            });

            stack.Children.Add(new TextBlock
            {
                Text = w.Message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = _chromeFgBrush,
                LineHeight = 18
            });

            if (!string.IsNullOrEmpty(w.ObjectName))
            {
                var objLabel = new TextBlock
                {
                    FontSize = 12,
                    Foreground = _mutedBrush,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                objLabel.Inlines.Add(new Run("Object: "));
                objLabel.Inlines.Add(new Run(w.ObjectName)
                {
                    FontFamily = ConsolasFont,
                    FontWeight = FontWeights.SemiBold
                });
                stack.Children.Add(objLabel);
            }

            return new Border
            {
                Background = _cardBgBrush,
                BorderBrush = _dividerBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = stack
            };
        }

        private StackPanel BuildConfirmPanel(string expectedObjectName, System.Windows.Controls.TextBox confirmBox)
        {
            var panel = new StackPanel { Margin = new Thickness(16, 10, 16, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = "Type the object name to confirm:",
                FontSize = 12,
                Foreground = _mutedBrush,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = expectedObjectName,
                FontFamily = ConsolasFont,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = _accentBrush,
                Margin = new Thickness(0, 0, 0, 6)
            });
            panel.Children.Add(confirmBox);
            return panel;
        }

        private DockPanel BuildFooter(
            bool isDrop,
            string? expectedObjectName,
            System.Windows.Controls.TextBox? confirmBox,
            out System.Windows.Controls.Button cancelBtn)
        {
            var footer = new DockPanel
            {
                Margin = new Thickness(16, 12, 16, 16),
                LastChildFill = false
            };

            _suppressCheckBox = new System.Windows.Controls.CheckBox
            {
                Content = "Don\u2019t warn again this session",
                FontSize = 12,
                Foreground = _mutedBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_suppressCheckBox, Dock.Left);
            footer.Children.Add(_suppressCheckBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btnPanel, Dock.Right);

            cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true,
                FontSize = 13
            };
            cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

            // FR-005 — deliberately not the default button; user must click it explicitly.
            var executeBtn = new System.Windows.Controls.Button
            {
                Content = isDrop ? "Drop" : "Execute Anyway",
                MinWidth = 110,
                Height = 32,
                FontSize = 13,
                Foreground = Brushes.White,
                Background = Freeze(new SolidColorBrush(isDrop ? ErrorBorder : BtnPrimary)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 0, 16, 0),
                // DROP with a known object name starts disabled until the user types it.
                // DROP with no extractable object name falls through — dialog still required,
                // but there's nothing to type, so the button is enabled from the start.
                IsEnabled = !isDrop || string.IsNullOrEmpty(expectedObjectName)
            };
            executeBtn.Click += (_, _) =>
            {
                SuppressForSession = _suppressCheckBox?.IsChecked == true;
                DialogResult = true;
                Close();
            };

            if (confirmBox != null && !string.IsNullOrEmpty(expectedObjectName))
            {
                confirmBox.TextChanged += (_, _) =>
                {
                    executeBtn.IsEnabled = string.Equals(
                        confirmBox.Text.Trim(), expectedObjectName, StringComparison.OrdinalIgnoreCase);
                };
            }

            btnPanel.Children.Add(executeBtn);
            btnPanel.Children.Add(cancelBtn);
            footer.Children.Add(btnPanel);

            return footer;
        }

        private static string GetRuleLabel(SafetyWarningType type) => type switch
        {
            SafetyWarningType.DeleteWithoutWhere        => "DELETE without WHERE",
            SafetyWarningType.UpdateWithoutWhere        => "UPDATE without WHERE",
            SafetyWarningType.MergeWithoutFilter        => "MERGE without WHEN MATCHED",
            SafetyWarningType.DmlInsideJoinWithoutWhere => "JOIN without WHERE",
            SafetyWarningType.UnsafeDmlInProcOrTrigger  => "Unsafe DML in procedure/trigger",
            SafetyWarningType.DropTable                 => "DROP TABLE",
            SafetyWarningType.DropDatabase              => "DROP DATABASE",
            SafetyWarningType.TruncateTable             => "TRUNCATE TABLE",
            SafetyWarningType.ProductionDml             => "Production DML",
            SafetyWarningType.ProductionDdl             => "Production DDL",
            _                                           => type.ToString()
        };

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
