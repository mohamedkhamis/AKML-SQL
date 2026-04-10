#nullable enable
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Safety;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Safety
{
    /// <summary>
    /// Modal dialog that presents execution safety warnings to the user before a query runs.
    /// Three display modes based on the most severe warning:
    /// <list type="bullet">
    ///   <item><b>Simple confirmation</b> — for ProductionDml/Ddl and TruncateTable warnings.</item>
    ///   <item><b>Error-level warning</b> — for DeleteWithoutWhere and UpdateWithoutWhere.</item>
    ///   <item><b>Type-to-confirm</b> — for DropTable and DropDatabase (user must type the object name).</item>
    /// </list>
    /// </summary>
    internal sealed class SafetyWarningDialog : Form
    {
        private Button? _proceedButton;
        private Button? _cancelButton;
        private TextBox? _confirmTextBox;
        private CheckBox? _suppressCheckBox;
        private string? _expectedObjectName;
        private StringComparison _confirmComparison = StringComparison.OrdinalIgnoreCase;

        /// <summary>
        /// Spec 014, US1 / FR-006 — when <c>true</c> after the dialog closes,
        /// the caller should suppress future warnings for the same rule ids in
        /// this editor session.
        /// </summary>
        public bool SuppressForSession { get; private set; }

        private SafetyWarningDialog()
        {
            // Programmatic layout — no Designer
        }

        /// <summary>
        /// Spec 014, US1 / FR-006 — factory method returning the dialog instance so
        /// callers can inspect <see cref="SuppressForSession"/> after <c>ShowDialog()</c>.
        /// The caller owns the lifetime and must dispose the dialog.
        /// </summary>
        public static SafetyWarningDialog CreateForWarnings(SafetyWarningDto[] warnings, string? serverName, string? environmentLabel, string? envColor = null)
        {
            var dialog = new SafetyWarningDialog();
            if (!string.IsNullOrEmpty(environmentLabel))
            {
                try
                {
                    var settings = AkmlSql.Core.Config.ConfigManager.Load();
                    var severity = settings.Safety.EnvironmentSeverity;
                    if (severity.TryGetValue(environmentLabel, out var level))
                    {
                        if (string.Equals(level, "TypeServerName", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(serverName))
                        {
                            dialog.BuildTypeServerNameLayout(warnings, serverName, environmentLabel, envColor);
                            return dialog;
                        }
                        if (string.Equals(level, "SimpleConfirm", StringComparison.OrdinalIgnoreCase))
                        {
                            dialog.BuildSimpleConfirmLayout(warnings);
                            return dialog;
                        }
                    }
                }
                catch { }
            }
            dialog.BuildLayout(warnings);
            return dialog;
        }

        /// <summary>
        /// Shows the safety warning dialog for the given warnings using default mode detection.
        /// </summary>
        public static DialogResult Show(SafetyWarningDto[] warnings)
        {
            return Show(warnings, serverName: null, environmentLabel: null);
        }

        /// <summary>
        /// Shows the safety warning dialog for the given warnings.
        /// When <paramref name="serverName"/> and <paramref name="environmentLabel"/> are provided,
        /// the dialog severity is determined by the <c>EnvironmentSeverity</c> setting:
        /// "TypeServerName" forces type-to-confirm with the server name, "SimpleConfirm" shows
        /// a Yes/No dialog, and "Disabled" skips the dialog entirely.
        /// </summary>
        public static DialogResult Show(SafetyWarningDto[] warnings, string? serverName, string? environmentLabel, string? envColor = null)
        {
            if (warnings == null || warnings.Length == 0)
                return DialogResult.OK;

            // Check environment severity override
            if (!string.IsNullOrEmpty(environmentLabel))
            {
                try
                {
                    var settings = AkmlSql.Core.Config.ConfigManager.Load();
                    var severity = settings.Safety.EnvironmentSeverity;
                    if (severity.TryGetValue(environmentLabel, out var level))
                    {
                        if (string.Equals(level, "Disabled", StringComparison.OrdinalIgnoreCase))
                            return DialogResult.OK; // Skip dialog for this environment

                        if (string.Equals(level, "TypeServerName", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(serverName))
                            {
                                // Force type-to-confirm mode with server name
                                using var typeDialog = new SafetyWarningDialog();
                                typeDialog.BuildTypeServerNameLayout(warnings, serverName, environmentLabel, envColor);
                                return typeDialog.ShowDialog();
                            }
                            // Server name unavailable — degrade to SimpleConfirm, not default mode
                            using var fallbackDialog = new SafetyWarningDialog();
                            fallbackDialog.BuildSimpleConfirmLayout(warnings);
                            return fallbackDialog.ShowDialog();
                        }

                        // "SimpleConfirm" or any other value falls through to default mode
                        if (string.Equals(level, "SimpleConfirm", StringComparison.OrdinalIgnoreCase))
                        {
                            using var simpleDialog = new SafetyWarningDialog();
                            simpleDialog.BuildSimpleConfirmLayout(warnings);
                            return simpleDialog.ShowDialog();
                        }
                    }
                }
                catch
                {
                    // Config load failure — fall through to default mode detection
                }
            }

            using var dialog = new SafetyWarningDialog();
            dialog.BuildLayout(warnings);
            return dialog.ShowDialog();
        }

        private void BuildLayout(SafetyWarningDto[] warnings)
        {
            // Determine the display mode from the most severe warning type
            var mode = DetermineMode(warnings);

            Text = Constants.ProductName + " - Execution Safety Warning";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;

            switch (mode)
            {
                case DisplayMode.TypeToConfirm:
                    BuildTypeToConfirmLayout(warnings);
                    break;
                case DisplayMode.ErrorLevel:
                    BuildErrorLevelLayout(warnings);
                    break;
                default:
                    BuildSimpleConfirmLayout(warnings);
                    break;
            }
        }

        /// <summary>
        /// Simple confirmation dialog for Warning-severity items (ProductionDml/Ddl, TruncateTable).
        /// </summary>
        private void BuildSimpleConfirmLayout(SafetyWarningDto[] warnings)
        {
            Size = new Size(480, 280);

            // Warning icon
            var iconBox = new PictureBox
            {
                Image = SystemIcons.Warning.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(20, 20)
            };

            // Combined message
            var messageLabel = new Label
            {
                Text = BuildCombinedMessage(warnings),
                Location = new Point(68, 20),
                Size = new Size(380, 140),
                AutoSize = false
            };

            _proceedButton = new Button
            {
                Text = "Proceed",
                Location = new Point(260, 200),
                Size = new Size(90, 30),
                DialogResult = DialogResult.OK
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(360, 200),
                Size = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = _proceedButton;
            CancelButton = _cancelButton;

            Controls.AddRange(new Control[] { iconBox, messageLabel, _proceedButton, _cancelButton });
        }

        /// <summary>
        /// Error-level warning dialog for DELETE/UPDATE without WHERE, MERGE without
        /// WHEN MATCHED, INNER JOIN without WHERE, and unsafe DML inside proc/trigger bodies.
        /// </summary>
        private void BuildErrorLevelLayout(SafetyWarningDto[] warnings)
        {
            Size = new Size(520, 330);

            // Red warning icon
            var iconBox = new PictureBox
            {
                Image = SystemIcons.Error.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(20, 20)
            };

            // Bold error header
            var headerLabel = new Label
            {
                Text = "DANGEROUS OPERATION DETECTED",
                Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
                ForeColor = Color.DarkRed,
                Location = new Point(68, 20),
                AutoSize = true
            };

            // Detailed message
            var messageLabel = new Label
            {
                Text = BuildCombinedMessage(warnings),
                Location = new Point(68, 50),
                Size = new Size(420, 140),
                AutoSize = false
            };

            // Spec 014, US1 / FR-006 — "Don't ask again for this session" checkbox
            _suppressCheckBox = new CheckBox
            {
                Text = "Don't ask again for this session",
                Location = new Point(20, 200),
                Size = new Size(280, 20),
                Checked = false
            };

            _proceedButton = new Button
            {
                Text = "I understand the risk \u2014 Execute",
                Location = new Point(200, 250),
                Size = new Size(200, 30),
                DialogResult = DialogResult.OK
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(410, 250),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            CancelButton = _cancelButton;
            // Deliberately not setting AcceptButton — user must click the explicit button (FR-005)

            _proceedButton.Click += (_, _) => SuppressForSession = _suppressCheckBox?.Checked ?? false;

            Controls.AddRange(new Control[] { iconBox, headerLabel, messageLabel, _suppressCheckBox, _proceedButton, _cancelButton });
        }

        /// <summary>
        /// Type-to-confirm dialog for DROP TABLE/DATABASE.
        /// </summary>
        private void BuildTypeToConfirmLayout(SafetyWarningDto[] warnings)
        {
            Size = new Size(520, 340);

            // Find the first DROP warning with an object name for the type-to-confirm
            var dropWarning = warnings.FirstOrDefault(w =>
                w.WarningType == (int)SafetyWarningType.DropTable ||
                w.WarningType == (int)SafetyWarningType.DropDatabase);

            _expectedObjectName = dropWarning?.ObjectName ?? string.Empty;

            // Warning icon
            var iconBox = new PictureBox
            {
                Image = SystemIcons.Warning.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(20, 20)
            };

            // Message
            var messageLabel = new Label
            {
                Text = BuildCombinedMessage(warnings),
                Location = new Point(68, 20),
                Size = new Size(420, 100),
                AutoSize = false
            };

            // Instruction
            var instructionLabel = new Label
            {
                Text = $"To confirm, type the object name exactly: {_expectedObjectName}",
                Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
                Location = new Point(20, 135),
                Size = new Size(460, 20),
                AutoSize = false
            };

            _confirmTextBox = new TextBox
            {
                Location = new Point(20, 165),
                Size = new Size(460, 24),
                Font = new Font("Consolas", 10)
            };
            _confirmTextBox.TextChanged += OnConfirmTextChanged;

            _proceedButton = new Button
            {
                Text = "Drop",
                Location = new Point(310, 260),
                Size = new Size(90, 30),
                DialogResult = DialogResult.OK,
                Enabled = false // Disabled until text matches
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(410, 260),
                Size = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };

            CancelButton = _cancelButton;
            // Deliberately not setting AcceptButton — user must type the name first

            Controls.AddRange(new Control[]
            {
                iconBox, messageLabel, instructionLabel, _confirmTextBox, _proceedButton, _cancelButton
            });

            // Focus the text box
            ActiveControl = _confirmTextBox;
        }

        /// <summary>
        /// Type-to-confirm dialog for Production environments.
        /// User must type the server name to enable the Proceed button.
        /// Background color matches the environment color for maximum visibility.
        /// </summary>
        private void BuildTypeServerNameLayout(SafetyWarningDto[] warnings, string serverName, string environmentLabel, string? envColorHex = null)
        {
            Size = new Size(540, 360);
            _expectedObjectName = serverName;
            _confirmComparison = StringComparison.Ordinal; // Case-sensitive for production server name

            // Use pre-resolved color from caller to avoid redundant EnvironmentDetector.Match call
            Color envColor = Color.DarkRed;
            try
            {
                if (!string.IsNullOrEmpty(envColorHex))
                {
                    envColor = ColorTranslator.FromHtml(envColorHex);
                }
            }
            catch { }

            // Apply environment color as a subtle background tint
            BackColor = Color.FromArgb(
                Math.Min(envColor.R + 200, 255),
                Math.Min(envColor.G + 200, 255),
                Math.Min(envColor.B + 200, 255));

            // Environment banner
            var bannerPanel = new Panel
            {
                BackColor = envColor,
                Location = new Point(0, 0),
                Size = new Size(540, 36),
                Dock = DockStyle.Top
            };
            var bannerLabel = new Label
            {
                Text = $"  {environmentLabel} — {serverName}",
                ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
                Location = new Point(4, 8),
                AutoSize = true
            };
            bannerPanel.Controls.Add(bannerLabel);

            // Warning icon + message
            var iconBox = new PictureBox
            {
                Image = SystemIcons.Warning.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(20, 50)
            };

            var messageLabel = new Label
            {
                Text = BuildCombinedMessage(warnings),
                Location = new Point(68, 50),
                Size = new Size(440, 100),
                AutoSize = false
            };

            // Instruction: type server name
            var instructionLabel = new Label
            {
                Text = $"To confirm execution on this {environmentLabel} server, type the server name exactly:",
                Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
                Location = new Point(20, 165),
                Size = new Size(490, 20),
                AutoSize = false
            };

            var expectedLabel = new Label
            {
                Text = serverName,
                Font = new Font("Consolas", 10, FontStyle.Bold),
                ForeColor = envColor,
                Location = new Point(20, 190),
                AutoSize = true
            };

            _confirmTextBox = new TextBox
            {
                Location = new Point(20, 215),
                Size = new Size(490, 24),
                Font = new Font("Consolas", 10)
            };
            _confirmTextBox.TextChanged += OnConfirmTextChanged;

            _proceedButton = new Button
            {
                Text = "Execute",
                Location = new Point(320, 280),
                Size = new Size(90, 30),
                DialogResult = DialogResult.OK,
                Enabled = false
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(420, 280),
                Size = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };

            CancelButton = _cancelButton;

            Controls.AddRange(new Control[]
            {
                bannerPanel, iconBox, messageLabel, instructionLabel,
                expectedLabel, _confirmTextBox, _proceedButton, _cancelButton
            });

            ActiveControl = _confirmTextBox;
        }

        private void OnConfirmTextChanged(object? sender, EventArgs e)
        {
            if (_proceedButton == null || _confirmTextBox == null || _expectedObjectName == null)
                return;

            _proceedButton.Enabled = string.Equals(
                _confirmTextBox.Text.Trim(),
                _expectedObjectName,
                _confirmComparison);
        }

        /// <summary>
        /// Builds a combined message from all warnings, separated by newlines.
        /// </summary>
        private static string BuildCombinedMessage(SafetyWarningDto[] warnings)
        {
            if (warnings.Length == 1)
                return warnings[0].Message;

            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < warnings.Length; i++)
            {
                if (i > 0)
                    lines.AppendLine();
                lines.Append("\u2022 ");
                lines.Append(warnings[i].Message);
            }
            return lines.ToString();
        }

        /// <summary>
        /// Determines which display mode to use based on the most severe warning type.
        /// Priority: TypeToConfirm > ErrorLevel > Simple.
        /// </summary>
        private static DisplayMode DetermineMode(SafetyWarningDto[] warnings)
        {
            bool hasTypeToConfirm = false;
            bool hasErrorLevel = false;

            foreach (var w in warnings)
            {
                var warningType = (SafetyWarningType)w.WarningType;
                switch (warningType)
                {
                    case SafetyWarningType.DropTable:
                    case SafetyWarningType.DropDatabase:
                        hasTypeToConfirm = true;
                        break;
                    case SafetyWarningType.DeleteWithoutWhere:
                    case SafetyWarningType.UpdateWithoutWhere:
                    // Spec 014, US1 — new detection patterns use Error-level mode
                    case SafetyWarningType.MergeWithoutFilter:
                    case SafetyWarningType.DmlInsideJoinWithoutWhere:
                    case SafetyWarningType.UnsafeDmlInProcOrTrigger:
                        hasErrorLevel = true;
                        break;
                }
            }

            if (hasTypeToConfirm) return DisplayMode.TypeToConfirm;
            if (hasErrorLevel) return DisplayMode.ErrorLevel;
            return DisplayMode.SimpleConfirm;
        }

        private enum DisplayMode
        {
            SimpleConfirm,
            ErrorLevel,
            TypeToConfirm
        }
    }
}
