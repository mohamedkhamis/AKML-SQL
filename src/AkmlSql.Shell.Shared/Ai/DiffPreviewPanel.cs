using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// Preview dialog for AI-generated SQL. Shows the generated SQL with Accept, Edit, and Reject buttons.
    /// <list type="bullet">
    ///   <item><b>Accept</b> (<see cref="DialogResult.OK"/>): caller inserts SQL into the active editor.</item>
    ///   <item><b>Edit</b> (<see cref="DialogResult.Yes"/>): caller opens SQL in a new editor tab.</item>
    ///   <item><b>Reject</b> (<see cref="DialogResult.Cancel"/>): no action taken.</item>
    /// </list>
    /// </summary>
    internal sealed class DiffPreviewPanel : Form
    {
        private readonly TextBox _sqlTextBox;
        private readonly Button _acceptButton;
        private readonly Button _editButton;
        private readonly Button _rejectButton;
        private readonly Label _infoLabel;
        private readonly Label _promptLabel;

        /// <summary>Gets the (possibly edited) generated SQL text.</summary>
        public string GeneratedSql { get; private set; } = string.Empty;

        /// <summary>Gets whether the user accepted the generated SQL.</summary>
        public bool Accepted { get; private set; }

        /// <summary>
        /// Creates a new <see cref="DiffPreviewPanel"/> showing the generated SQL.
        /// </summary>
        /// <param name="generatedSql">The AI-generated SQL text.</param>
        /// <param name="originalContext">The original user prompt (shown as context).</param>
        /// <param name="tokensUsed">Number of tokens consumed by the AI request.</param>
        /// <param name="latencyMs">Round-trip latency in milliseconds.</param>
        public DiffPreviewPanel(string generatedSql, string originalContext, int tokensUsed, int latencyMs)
        {
            GeneratedSql = generatedSql;

            // Form setup
            Text = "AI: Generated SQL Preview";
            Width = 700;
            Height = 520;
            MinimumSize = new Size(500, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            // Prompt context label
            _promptLabel = new Label
            {
                Text = $"Prompt: {Truncate(originalContext, 120)}",
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(8, 8, 8, 0),
                ForeColor = SystemColors.GrayText,
                AutoEllipsis = true
            };
            Controls.Add(_promptLabel);

            // Info label showing generation stats
            _infoLabel = new Label
            {
                Text = $"Generated in {latencyMs:N0}ms  |  {tokensUsed:N0} tokens",
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(8, 4, 8, 0),
                ForeColor = SystemColors.ControlDarkDark
            };
            Controls.Add(_infoLabel);

            // SQL text box (read-only, monospace, with scroll)
            _sqlTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 10f, FontStyle.Regular),
                Text = generatedSql,
                Dock = DockStyle.Fill,
                BackColor = SystemColors.Window
            };
            Controls.Add(_sqlTextBox);

            // Button panel
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 8),
                AutoSize = false
            };

            _rejectButton = new Button
            {
                Text = "Reject",
                Width = 90,
                Height = 30,
                DialogResult = DialogResult.Cancel
            };
            _rejectButton.Click += OnRejectClick;
            buttonPanel.Controls.Add(_rejectButton);

            _editButton = new Button
            {
                Text = "Edit in New Tab",
                Width = 120,
                Height = 30,
                DialogResult = DialogResult.Yes
            };
            _editButton.Click += OnEditClick;
            buttonPanel.Controls.Add(_editButton);

            _acceptButton = new Button
            {
                Text = "Accept",
                Width = 90,
                Height = 30,
                DialogResult = DialogResult.OK
            };
            _acceptButton.Click += OnAcceptClick;
            buttonPanel.Controls.Add(_acceptButton);

            Controls.Add(buttonPanel);

            // Set tab order: SQL box first, then buttons
            _sqlTextBox.TabIndex = 0;
            _acceptButton.TabIndex = 1;
            _editButton.TabIndex = 2;
            _rejectButton.TabIndex = 3;

            AcceptButton = _acceptButton;
            CancelButton = _rejectButton;

            // Ensure correct Z-order (controls are stacked in reverse add order for Dock)
            buttonPanel.BringToFront();
            _sqlTextBox.BringToFront();
            _infoLabel.BringToFront();
            _promptLabel.BringToFront();
        }

        /// <summary>
        /// Overloaded constructor that accepts a list of <see cref="AnnotationDto"/> for display.
        /// Appends annotation labels to lines in the SQL text box and adds an annotations summary section.
        /// </summary>
        public DiffPreviewPanel(
            string generatedSql,
            string originalContext,
            int tokensUsed,
            int latencyMs,
            List<AnnotationDto>? annotations)
            : this(generatedSql, originalContext, tokensUsed, latencyMs)
        {
            if (annotations == null || annotations.Count == 0)
                return;

            // Build annotation summary and append to the text box
            var annotationSummary = "\n\n--- Annotations ---\n";
            foreach (var ann in annotations)
            {
                var lineRange = ann.StartLine == ann.EndLine
                    ? $"Line {ann.StartLine}"
                    : $"Lines {ann.StartLine}-{ann.EndLine}";
                var label = ann.Category.Equals("safe", StringComparison.OrdinalIgnoreCase)
                    ? "[SAFE]"
                    : "[REVIEW]";
                annotationSummary += $"{label} {lineRange}: {ann.Description}\n";
            }

            _sqlTextBox.Text = generatedSql + annotationSummary;
            GeneratedSql = generatedSql; // Keep only the SQL, not the annotations
        }

        private void OnAcceptClick(object sender, EventArgs e)
        {
            Accepted = true;
            GeneratedSql = _sqlTextBox.Text;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnEditClick(object sender, EventArgs e)
        {
            GeneratedSql = _sqlTextBox.Text;
            DialogResult = DialogResult.Yes;
            Close();
        }

        private void OnRejectClick(object sender, EventArgs e)
        {
            Accepted = false;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Ctrl+C to copy selected text from the SQL text box
            if (e.Control && e.KeyCode == Keys.C && _sqlTextBox.Focused)
            {
                if (!string.IsNullOrEmpty(_sqlTextBox.SelectedText))
                    Clipboard.SetText(_sqlTextBox.SelectedText);
                else
                    Clipboard.SetText(_sqlTextBox.Text);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Replace newlines with spaces for single-line display
            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }
}
