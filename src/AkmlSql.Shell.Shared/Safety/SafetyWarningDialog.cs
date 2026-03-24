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
        private string? _expectedObjectName;

        private SafetyWarningDialog()
        {
            // Programmatic layout — no Designer
        }

        /// <summary>
        /// Shows the safety warning dialog for the given warnings.
        /// Displays the most severe warning mode.
        /// Returns <see cref="DialogResult.OK"/> if the user confirms, or
        /// <see cref="DialogResult.Cancel"/> if the user cancels.
        /// </summary>
        /// <param name="warnings">One or more safety warnings from the engine.</param>
        /// <returns>
        /// <see cref="DialogResult.OK"/> to proceed with execution,
        /// <see cref="DialogResult.Cancel"/> to abort.
        /// </returns>
        public static DialogResult Show(SafetyWarningDto[] warnings)
        {
            if (warnings == null || warnings.Length == 0)
                return DialogResult.OK;

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
        /// Error-level warning dialog for DELETE/UPDATE without WHERE.
        /// </summary>
        private void BuildErrorLevelLayout(SafetyWarningDto[] warnings)
        {
            Size = new Size(520, 300);

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

            _proceedButton = new Button
            {
                Text = "I understand the risk \u2014 Proceed",
                Location = new Point(220, 220),
                Size = new Size(190, 30),
                DialogResult = DialogResult.OK
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(420, 220),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            CancelButton = _cancelButton;
            // Deliberately not setting AcceptButton — user must click the explicit button

            Controls.AddRange(new Control[] { iconBox, headerLabel, messageLabel, _proceedButton, _cancelButton });
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

        private void OnConfirmTextChanged(object? sender, EventArgs e)
        {
            if (_proceedButton == null || _confirmTextBox == null || _expectedObjectName == null)
                return;

            _proceedButton.Enabled = string.Equals(
                _confirmTextBox.Text.Trim(),
                _expectedObjectName,
                StringComparison.OrdinalIgnoreCase);
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
