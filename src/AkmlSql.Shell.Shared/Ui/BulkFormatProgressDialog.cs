using System;
using System.Drawing;
using System.Windows.Forms;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// T108: Progress dialog shown during bulk formatting operations.
    /// Displays a progress bar, current file name, and formatted/total count.
    /// Supports cancellation via the Cancel button.
    /// </summary>
    internal sealed class BulkFormatProgressDialog : Form
    {
        private ProgressBar _progressBar;
        private Label _statusLabel;
        private Label _currentFileLabel;
        private Label _countLabel;
        private Button _cancelButton;

        /// <summary>
        /// Set to true when the user clicks Cancel. The caller should poll this
        /// and cancel the associated CancellationTokenSource.
        /// </summary>
        public bool CancelRequested { get; private set; }

        public BulkFormatProgressDialog()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = Constants.ProductName + " - Formatting...";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(500, 200);
            ShowInTaskbar = false;
            ControlBox = false; // No X button — must use Cancel

            _statusLabel = new Label
            {
                Text = "Preparing...",
                Location = new Point(12, 12),
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold)
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(12, 40),
                Size = new Size(460, 24),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            _currentFileLabel = new Label
            {
                Text = string.Empty,
                Location = new Point(12, 72),
                Size = new Size(460, 20),
                AutoEllipsis = true
            };

            _countLabel = new Label
            {
                Text = "0 / 0 files",
                Location = new Point(12, 96),
                AutoSize = true
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(380, 124),
                Size = new Size(90, 28)
            };
            _cancelButton.Click += (_, __) =>
            {
                CancelRequested = true;
                _cancelButton.Enabled = false;
                _statusLabel.Text = "Cancelling...";
            };

            Controls.AddRange([
                _statusLabel, _progressBar, _currentFileLabel, _countLabel, _cancelButton
            ]);
        }

        /// <summary>
        /// Updates the progress UI. Must be called on the UI thread.
        /// </summary>
        public void UpdateProgress(int completed, int total, string currentFile)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateProgress(completed, total, currentFile)));
                return;
            }

            if (total > 0)
            {
                _progressBar.Maximum = total;
                _progressBar.Value = Math.Min(completed, total);
            }

            _countLabel.Text = $"{completed} / {total} files";
            _currentFileLabel.Text = currentFile;
            _statusLabel.Text = CancelRequested ? "Cancelling..." : "Formatting...";
        }

        /// <summary>
        /// Shows the completion state with a summary message.
        /// </summary>
        public void ShowComplete(string summary)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ShowComplete(summary)));
                return;
            }

            _statusLabel.Text = "Complete";
            _currentFileLabel.Text = summary;
            _cancelButton.Text = "Close";
            _cancelButton.Enabled = true;
            _cancelButton.Click -= null;
            _cancelButton.Click += (_, __) => Close();
        }
    }
}
