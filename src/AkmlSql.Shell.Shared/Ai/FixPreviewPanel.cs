#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// WinForms dialog that shows an AI-generated fix for a failing SQL query.
    /// Displays the fixed SQL with line-level annotations (safe/review), an explanation section,
    /// and Apply Fix / Close buttons. Used by <see cref="Commands.AiFixCommand"/>.
    /// </summary>
    internal sealed class FixPreviewPanel : Form
    {
        private readonly RichTextBox _diffTextBox;
        private readonly string _fixedSql;

        /// <summary>Gets the fixed SQL text.</summary>
        public string FixedSql => _fixedSql;

        /// <summary>Gets whether the user chose to apply the fix.</summary>
        public bool ApplyFix { get; private set; }

        /// <summary>
        /// Creates a new FixPreviewPanel with annotated diff and explanation.
        /// </summary>
        public FixPreviewPanel(
            string originalSql,
            string fixedSql,
            string? explanation,
            List<AnnotationDto>? annotations,
            int latencyMs,
            int tokensUsed)
        {
            _fixedSql = fixedSql;

            Text = "AI: Fix Preview";
            Size = new Size(750, 600);
            MinimumSize = new Size(500, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;

            // Split container: top for diff, bottom for explanation
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 380,
                Panel1MinSize = 150,
                Panel2MinSize = 80
            };

            // Diff display using RichTextBox for colored annotations
            _diffTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9.5f),
                WordWrap = false,
                BackColor = SystemColors.Window,
                BorderStyle = BorderStyle.None
            };

            PopulateDiff(fixedSql, annotations);
            splitContainer.Panel1.Controls.Add(_diffTextBox);

            var diffLabel = new Label
            {
                Text = "Fixed SQL (annotated changes):",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Padding = new Padding(4, 2, 0, 0)
            };
            splitContainer.Panel1.Controls.Add(diffLabel);

            // Explanation panel
            var explanationLabel = new Label
            {
                Text = "Explanation:",
                Dock = DockStyle.Top,
                Height = 22,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Padding = new Padding(4, 2, 0, 0)
            };

            var explanationBox = new TextBox
            {
                Text = explanation ?? "(No explanation provided)",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Multiline = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 9f),
                BackColor = SystemColors.Window,
                BorderStyle = BorderStyle.None
            };

            splitContainer.Panel2.Controls.Add(explanationBox);
            splitContainer.Panel2.Controls.Add(explanationLabel);

            Controls.Add(splitContainer);

            // Bottom panel with info and buttons
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Padding = new Padding(8, 4, 8, 4)
            };

            var infoLabel = new Label
            {
                Text = $"Fixed in {latencyMs}ms | {tokensUsed} tokens",
                AutoSize = true,
                Location = new Point(8, 12),
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 8.25f, FontStyle.Italic)
            };

            var applyButton = new Button
            {
                Text = "Apply Fix",
                Size = new Size(90, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            applyButton.Location = new Point(bottomPanel.Width - applyButton.Width - 100, 6);
            applyButton.Click += (_, _) =>
            {
                ApplyFix = true;
                DialogResult = DialogResult.OK;
                Close();
            };

            var closeButton = new Button
            {
                Text = "Close",
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                DialogResult = DialogResult.Cancel
            };
            closeButton.Location = new Point(bottomPanel.Width - closeButton.Width - 8, 6);
            closeButton.Click += (_, _) => Close();

            bottomPanel.Controls.Add(infoLabel);
            bottomPanel.Controls.Add(applyButton);
            bottomPanel.Controls.Add(closeButton);
            Controls.Add(bottomPanel);

            AcceptButton = applyButton;
            CancelButton = closeButton;
        }

        /// <summary>
        /// Populates the RichTextBox with the fixed SQL, adding colored annotation markers.
        /// </summary>
        private void PopulateDiff(string fixedSql, List<AnnotationDto>? annotations)
        {
            var fixedLines = fixedSql.Split('\n');

            var annotationsByLine = new Dictionary<int, AnnotationDto>();
            if (annotations != null)
            {
                foreach (var ann in annotations)
                {
                    for (int line = ann.StartLine; line <= ann.EndLine; line++)
                    {
                        if (!annotationsByLine.ContainsKey(line))
                            annotationsByLine[line] = ann;
                    }
                }
            }

            _diffTextBox.Clear();

            for (int i = 0; i < fixedLines.Length; i++)
            {
                var lineNumber = i + 1;
                var lineText = fixedLines[i].TrimEnd('\r');

                if (annotationsByLine.TryGetValue(lineNumber, out var annotation))
                {
                    var isSafe = annotation.Category.Equals("safe", StringComparison.OrdinalIgnoreCase);
                    var label = isSafe ? "[SAFE] " : "[REVIEW] ";
                    var labelColor = isSafe ? Color.DarkGreen : Color.DarkGoldenrod;

                    var startPos = _diffTextBox.TextLength;
                    _diffTextBox.AppendText(label);
                    _diffTextBox.Select(startPos, label.Length);
                    _diffTextBox.SelectionColor = labelColor;
                    _diffTextBox.SelectionFont = new Font(_diffTextBox.Font, FontStyle.Bold);

                    startPos = _diffTextBox.TextLength;
                    _diffTextBox.AppendText(lineText);
                    _diffTextBox.Select(startPos, lineText.Length);
                    _diffTextBox.SelectionBackColor = isSafe
                        ? Color.FromArgb(230, 255, 230)
                        : Color.FromArgb(255, 255, 220);
                    _diffTextBox.SelectionColor = Color.Black;
                    _diffTextBox.SelectionFont = new Font(_diffTextBox.Font, FontStyle.Regular);
                }
                else
                {
                    _diffTextBox.AppendText(lineText);
                }

                if (i < fixedLines.Length - 1)
                    _diffTextBox.AppendText("\n");
            }

            // Append annotation summary
            if (annotations != null && annotations.Count > 0)
            {
                _diffTextBox.AppendText("\n\n--- Annotations ---\n");
                foreach (var ann in annotations)
                {
                    var lineRange = ann.StartLine == ann.EndLine
                        ? $"Line {ann.StartLine}"
                        : $"Lines {ann.StartLine}-{ann.EndLine}";
                    _diffTextBox.AppendText(
                        $"[{ann.Category.ToUpperInvariant()}] {lineRange}: {ann.Description}\n");
                }
            }

            _diffTextBox.Select(0, 0);
            _diffTextBox.ScrollToCaret();
        }
    }
}
