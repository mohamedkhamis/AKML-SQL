#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// WinForms dialog that displays an AI-generated SQL explanation with four collapsible sections:
    /// Purpose, Step-by-step, Key Details, and Suggestions.
    /// Shown by <see cref="Commands.AiExplainCommand"/> after receiving an <c>AiExplainResponse</c>.
    /// </summary>
    internal sealed class ExplanationPanel : Form
    {
        private readonly Panel _scrollPanel;

        /// <summary>
        /// Creates a new ExplanationPanel with the given explanation sections and metadata.
        /// </summary>
        /// <param name="purpose">One-sentence summary of the SQL.</param>
        /// <param name="stepByStep">Numbered step-by-step breakdown.</param>
        /// <param name="keyDetails">Data types, performance, and edge case details.</param>
        /// <param name="suggestions">Optional improvement suggestions.</param>
        /// <param name="latencyMs">Round-trip latency in milliseconds.</param>
        /// <param name="tokensUsed">Number of tokens consumed by the AI request.</param>
        public ExplanationPanel(
            string? purpose,
            string? stepByStep,
            string? keyDetails,
            string? suggestions,
            int latencyMs,
            int tokensUsed)
        {
            Text = "AI: SQL Explanation";
            Size = new Size(620, 540);
            MinimumSize = new Size(400, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;

            // Scrollable container for sections
            _scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8)
            };

            // Build sections top-down
            var yOffset = 8;
            yOffset = AddSection(_scrollPanel, "Purpose", purpose, yOffset);
            yOffset = AddSection(_scrollPanel, "Step-by-Step", stepByStep, yOffset);
            yOffset = AddSection(_scrollPanel, "Key Details", keyDetails, yOffset);
            yOffset = AddSection(_scrollPanel, "Suggestions", suggestions, yOffset);

            Controls.Add(_scrollPanel);

            // Bottom panel with info label and close button
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                Padding = new Padding(8, 4, 8, 4)
            };

            var infoLabel = new Label
            {
                Text = $"Explained in {latencyMs}ms | {tokensUsed} tokens",
                AutoSize = true,
                Location = new Point(8, 10),
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 8.25f, FontStyle.Italic)
            };

            var closeButton = new Button
            {
                Text = "Close",
                Size = new Size(80, 28),
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                DialogResult = DialogResult.OK
            };
            closeButton.Location = new Point(bottomPanel.Width - closeButton.Width - 8, 6);
            closeButton.Click += (_, _) => Close();

            bottomPanel.Controls.Add(infoLabel);
            bottomPanel.Controls.Add(closeButton);
            Controls.Add(bottomPanel);

            AcceptButton = closeButton;
        }

        /// <summary>
        /// Adds a collapsible section (GroupBox with a read-only TextBox) to the container.
        /// Returns the new Y offset after the section.
        /// </summary>
        private static int AddSection(Panel container, string title, string? content, int yOffset)
        {
            if (string.IsNullOrWhiteSpace(content))
                content = "(None)";

            var groupBox = new GroupBox
            {
                Text = title,
                Location = new Point(8, yOffset),
                Width = container.Width - 40,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            var textBox = new TextBox
            {
                Text = content,
                ReadOnly = true,
                Multiline = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Location = new Point(8, 20),
                Width = groupBox.Width - 20,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Font = new Font("Consolas", 9f, FontStyle.Regular),
                BorderStyle = BorderStyle.None
            };

            // Calculate height based on content (min 40, max 180)
            var lineCount = content.Split('\n').Length;
            var textHeight = Math.Max(40, Math.Min(180, lineCount * 16 + 10));
            textBox.Height = textHeight;

            groupBox.Height = textHeight + 30;
            groupBox.Controls.Add(textBox);
            container.Controls.Add(groupBox);

            return yOffset + groupBox.Height + 8;
        }
    }
}
