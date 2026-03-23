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
    /// Shows a summary strip and a paginated DataGridView of all issues.
    /// Double-clicking a row fires <see cref="NavigateToIssue"/> for IDE navigation.
    /// </summary>
    internal sealed class BulkAnalysisResultDialog : Form
    {
        private const int PageSize = 200;

        private readonly IReadOnlyList<(string File, CodeIssueInfo Issue)> _issues;
        private readonly int _filesAnalyzed;
        private IReadOnlyList<(string File, CodeIssueInfo Issue)> _sorted = Array.Empty<(string File, CodeIssueInfo Issue)>();

        private int _page;
        private DataGridView _grid = null!;
        private Label _lblPage     = null!;
        private Button _btnPrev    = null!;
        private Button _btnNext    = null!;

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
            Size            = new Size(960, 580);
            MinimumSize     = new Size(720, 420);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor       = Color.FromArgb(250, 250, 252);

            var errors   = _issues.Count(x => x.Issue.Severity >= 3);
            var warnings = _issues.Count(x => x.Issue.Severity == 2);
            var info     = _issues.Count(x => x.Issue.Severity < 2);

            // ─── Summary strip ────────────────────────────────────────────────
            var summaryPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 50,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding   = new Padding(10, 8, 10, 8)
            };

            var chipFlow = new FlowLayoutPanel
            {
                Dock         = DockStyle.Fill,
                WrapContents = false,
                AutoScroll   = false
            };

            chipFlow.Controls.Add(MakeChip($"Files: {_filesAnalyzed}", Color.FromArgb(60, 120, 200), Color.White));
            chipFlow.Controls.Add(MakeChip($"Issues: {_issues.Count}", Color.FromArgb(80, 80, 90), Color.White));
            chipFlow.Controls.Add(MakeChip($"Errors: {errors}", errors > 0 ? Color.FromArgb(180, 40, 40) : Color.FromArgb(160, 160, 160), Color.White));
            chipFlow.Controls.Add(MakeChip($"Warnings: {warnings}", warnings > 0 ? Color.FromArgb(180, 110, 0) : Color.FromArgb(160, 160, 160), Color.White));
            chipFlow.Controls.Add(MakeChip($"Info: {info}", Color.FromArgb(50, 130, 160), Color.White));

            summaryPanel.Controls.Add(chipFlow);

            // ─── DataGridView ─────────────────────────────────────────────────
            _grid = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                ReadOnly              = true,
                RowHeadersVisible     = false,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.None,
                MultiSelect           = false,
                BorderStyle           = BorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight   = 28,
                RowTemplate           = { Height = 24 }
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 238, 245);
            _grid.ColumnHeadersDefaultCellStyle.Font      = new Font(DefaultFont, FontStyle.Bold);
            _grid.EnableHeadersVisualStyles               = false;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File",     Width = 250, Name = "File" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Line",     Width = 55,  Name = "Line" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Col",      Width = 45,  Name = "Col" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Severity", Width = 85,  Name = "Severity" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rule",     Width = 70,  Name = "Rule" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText   = "Message",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                Name         = "Message"
            });

            _grid.CellDoubleClick += OnCellDoubleClick;

            // ─── Pagination bar ───────────────────────────────────────────────
            var pageBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 46,
                BackColor = Color.FromArgb(242, 242, 245)
            };
            new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(210, 210, 215) }
                .Let(sep => pageBar.Controls.Add(sep));

            _lblPage = new Label
            {
                AutoSize  = true,
                Top       = 14,
                Left      = 10,
                ForeColor = Color.FromArgb(80, 80, 90)
            };
            _btnPrev = new Button { Text = "◀  Prev", Width = 80, Height = 28, Top = 9, Left = 200, FlatStyle = FlatStyle.System };
            _btnNext = new Button { Text = "Next  ▶", Width = 80, Height = 28, Top = 9, Left = 288, FlatStyle = FlatStyle.System };

            _btnPrev.Click += (_, __) => { if (_page > 0) { _page--; LoadPage(); } };
            _btnNext.Click += (_, __) =>
            {
                var total = (int)Math.Ceiling((double)_sorted.Count / PageSize);
                if (_page < total - 1) { _page++; LoadPage(); }
            };

            var btnClose = new Button
            {
                Text         = "Close",
                Width        = 90,
                Height       = 28,
                Top          = 9,
                FlatStyle    = FlatStyle.System,
                DialogResult = DialogResult.OK,
                Anchor       = AnchorStyles.Top | AnchorStyles.Right
            };
            pageBar.Layout += (_, __) => btnClose.Left = pageBar.Width - 100;
            btnClose.Left = 860;

            pageBar.Controls.AddRange(new Control[] { _lblPage, _btnPrev, _btnNext, btnClose });
            AcceptButton = btnClose;

            Controls.Add(_grid);
            Controls.Add(summaryPanel);
            Controls.Add(pageBar);

            // Sort and populate first page
            _sorted = _issues.OrderByDescending(x => x.Issue.Severity)
                             .ThenBy(x => x.File)
                             .ThenBy(x => x.Issue.Line)
                             .ToList();
            LoadPage();
        }

        private void LoadPage()
        {
            _grid.SuspendLayout();
            _grid.Rows.Clear();

            int total = Math.Max(1, (int)Math.Ceiling((double)_sorted.Count / PageSize));
            if (_page >= total) _page = total - 1;

            int start = _page * PageSize;
            int end   = Math.Min(start + PageSize, _sorted.Count);

            for (int i = start; i < end; i++)
            {
                var (file, issue) = _sorted[i];
                var idx = _grid.Rows.Add(
                    System.IO.Path.GetFileName(file),
                    issue.Line,
                    issue.Column,
                    SeverityName(issue.Severity),
                    issue.RuleId,
                    issue.Message);

                var row = _grid.Rows[idx];
                row.Tag = (file, issue);
                row.DefaultCellStyle.ForeColor = issue.Severity >= 3 ? Color.FromArgb(180, 40, 40)
                    : issue.Severity == 2 ? Color.FromArgb(160, 100, 0)
                    : SystemColors.WindowText;
            }

            _grid.ResumeLayout();

            _lblPage.Text      = _sorted.Count == 0
                ? "No issues found"
                : $"Page {_page + 1} of {total}  ({_sorted.Count:N0} issues)";
            _btnPrev.Enabled   = _page > 0;
            _btnNext.Enabled   = _page < total - 1;
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

        private static Label MakeChip(string text, Color back, Color fore)
        {
            return new Label
            {
                Text      = "  " + text + "  ",
                AutoSize  = true,
                BackColor = back,
                ForeColor = fore,
                Font      = new Font(DefaultFont, FontStyle.Bold),
                Padding   = new Padding(4, 3, 4, 3),
                Margin    = new Padding(0, 4, 6, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
        }
    }

    // ─── Extension helper to avoid verbosity in Build() ──────────────────────────
    internal static class ControlExtensions
    {
        internal static T Let<T>(this T value, Action<T> action) { action(value); return value; }
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
