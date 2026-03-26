using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Ipc.Messages;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Dialogs
{
    /// <summary>
    /// Displays the results of a bulk code analysis run.
    /// Shows a summary panel and a DataGridView of all issues.
    /// Double-clicking a row fires <see cref="NavigateToIssue"/> for IDE navigation.
    /// </summary>
    internal sealed class BulkAnalysisResultDialog : Form
    {
        private readonly IReadOnlyList<(string File, CodeIssueInfo Issue)> _issues;
        private readonly int _filesAnalyzed;

        public event EventHandler<NavigateEventArgs>? NavigateToIssue;

        public BulkAnalysisResultDialog(
            IReadOnlyList<(string File, CodeIssueInfo Issue)> issues,
            int filesAnalyzed)
        {
            _issues        = issues;
            _filesAnalyzed = filesAnalyzed;
            Build();
        }

        private void Build()
        {
            Text            = Constants.ProductName + " — Code Analysis Results";
            Size            = new Size(900, 560);
            MinimumSize     = new Size(700, 400);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;

            // ─── Summary panel ────────────────────────────────────────────────
            var errors   = _issues.Count(x => x.Issue.Severity >= 3);
            var warnings = _issues.Count(x => x.Issue.Severity == 2);
            var info     = _issues.Count(x => x.Issue.Severity < 2);

            var summaryPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 65,
                BackColor = Color.FromArgb(245, 245, 245),
                Padding   = new Padding(10)
            };

            var summaryText = $"Files analyzed: {_filesAnalyzed}   " +
                $"Total issues: {_issues.Count}   " +
                $"Errors: {errors}   " +
                $"Warnings: {warnings}   " +
                $"Information: {info}";

            summaryPanel.Controls.Add(new Label
            {
                Text      = summaryText,
                AutoSize  = false,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font(DefaultFont, FontStyle.Regular)
            });

            // ─── DataGridView ─────────────────────────────────────────────────
            var grid = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                ReadOnly              = true,
                RowHeadersVisible     = false,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.None,
                MultiSelect           = false
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File",     Width = 250, Name = "File" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Line",     Width = 50,  Name = "Line" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Col",      Width = 45,  Name = "Col" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Severity", Width = 80,  Name = "Severity" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rule",     Width = 65,  Name = "Rule" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Message",  AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, Name = "Message" });

            foreach (var (file, issue) in _issues.OrderByDescending(x => x.Issue.Severity)
                                                   .ThenBy(x => x.File)
                                                   .ThenBy(x => x.Issue.Line))
            {
                var row = grid.Rows[grid.Rows.Add(
                    System.IO.Path.GetFileName(file),
                    issue.Line,
                    issue.Column,
                    SeverityName(issue.Severity),
                    issue.RuleId,
                    issue.Message)];

                row.Tag = (file, issue);

                row.DefaultCellStyle.ForeColor = issue.Severity >= 3 ? Color.DarkRed
                    : issue.Severity == 2 ? Color.DarkOrange
                    : SystemColors.WindowText;
            }

            grid.CellDoubleClick += OnCellDoubleClick;

            // ─── Close button ─────────────────────────────────────────────────
            var btnClose = new Button
            {
                Text         = "Close",
                Size         = new Size(90, 28),
                Anchor       = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };

            var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            buttonPanel.Resize += (_, _) => btnClose.Location =
                new Point(buttonPanel.Width - 100, 8);
            btnClose.Location = new Point(800, 8);
            buttonPanel.Controls.Add(btnClose);
            AcceptButton = btnClose;

            Controls.Add(grid);
            Controls.Add(summaryPanel);
            Controls.Add(buttonPanel);
        }

        private void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var grid = (DataGridView)sender;
            if (grid.Rows[e.RowIndex].Tag is (string file, CodeIssueInfo issue))
                NavigateToIssue?.Invoke(this, new NavigateEventArgs(file, issue.Line, issue.Column));
        }

        private static string SeverityName(int sev) => sev switch
        {
            >= 3 => "Error",
            2    => "Warning",
            1    => "Information",
            _    => "Hint"
        };
    }

    internal sealed class NavigateEventArgs : EventArgs
    {
        public string FilePath { get; }
        public int    Line     { get; }
        public int    Column   { get; }

        public NavigateEventArgs(string filePath, int line, int column)
        {
            FilePath = filePath;
            Line     = line;
            Column   = column;
        }
    }
}
