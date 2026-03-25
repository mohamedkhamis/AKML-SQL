using System;
using System.Drawing;
using System.Windows.Forms;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// Simple input dialog for the Text-to-SQL feature. Provides a multiline text box
    /// for the user to describe the query they want generated.
    /// </summary>
    internal sealed class TextToSqlInputDialog : Form
    {
        private readonly TextBox _promptTextBox;
        private readonly Button _submitButton;
        private readonly Button _cancelButton;
        private readonly Label _titleLabel;
        private readonly Label _hintLabel;

        /// <summary>Gets the user's prompt text after the dialog closes with <see cref="DialogResult.OK"/>.</summary>
        public string Prompt { get; private set; } = string.Empty;

        public TextToSqlInputDialog()
        {
            // Form setup
            Text = "AI: Text to SQL";
            Width = 560;
            Height = 260;
            MinimumSize = new Size(400, 220);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            // Title label
            _titleLabel = new Label
            {
                Text = "Describe the query you want to generate:",
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(8, 8, 8, 0),
                Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold)
            };
            Controls.Add(_titleLabel);

            // Hint label
            _hintLabel = new Label
            {
                Text = "Tip: You can also type --ai: followed by your prompt directly in the editor.",
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(8, 0, 8, 0),
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 8f)
            };
            Controls.Add(_hintLabel);

            // Prompt text box (multiline)
            _promptTextBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = new Font("Segoe UI", 10f),
                Dock = DockStyle.Fill,
                AcceptsReturn = false // Enter submits the form
            };
            Controls.Add(_promptTextBox);

            // Button panel
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 8),
                AutoSize = false
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Width = 90,
                Height = 30,
                DialogResult = DialogResult.Cancel
            };
            buttonPanel.Controls.Add(_cancelButton);

            _submitButton = new Button
            {
                Text = "Generate SQL",
                Width = 110,
                Height = 30,
                DialogResult = DialogResult.OK
            };
            _submitButton.Click += OnSubmitClick;
            buttonPanel.Controls.Add(_submitButton);

            Controls.Add(buttonPanel);

            // Tab order
            _promptTextBox.TabIndex = 0;
            _submitButton.TabIndex = 1;
            _cancelButton.TabIndex = 2;

            AcceptButton = _submitButton;
            CancelButton = _cancelButton;

            // Ensure correct Z-order
            buttonPanel.BringToFront();
            _promptTextBox.BringToFront();
            _hintLabel.BringToFront();
            _titleLabel.BringToFront();

            // Set initial focus to the text box
            Shown += (_, _) => _promptTextBox.Focus();
        }

        private void OnSubmitClick(object sender, EventArgs e)
        {
            Prompt = _promptTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(Prompt))
            {
                MessageBox.Show(
                    "Please enter a description of the query you want to generate.",
                    "Prompt Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Ctrl+Enter submits
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                OnSubmitClick(this, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            base.OnKeyDown(e);
        }
    }
}
