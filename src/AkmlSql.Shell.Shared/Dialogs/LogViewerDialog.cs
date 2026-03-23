using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Dialogs
{
    /// <summary>
    /// In-IDE log viewer: parses Serilog rolling log files, displays them in a
    /// paginated grid with level-based colouring, text search, and one-click copy
    /// of the full entry (including any attached exception / stack trace).
    /// </summary>
    internal sealed class LogViewerDialog : Form
    {
        // ─── Data model ───────────────────────────────────────────────────────

        private sealed class LogEntry
        {
            public string Timestamp { get; set; } = string.Empty;
            public string Level     { get; set; } = string.Empty;
            public string Message   { get; set; } = string.Empty;
            /// <summary>Full raw text including any attached exception / stack trace.</summary>
            public string FullText  { get; set; } = string.Empty;
        }

        // Matches the Serilog output template:
        // {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message}
        private static readonly Regex EntryPattern = new Regex(
            @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) \[([A-Z]{3})\] (.*)$",
            RegexOptions.Compiled);

        private const int PageSize = 200;

        // ─── State ────────────────────────────────────────────────────────────

        private readonly List<LogEntry> _all      = new List<LogEntry>();
        private readonly List<LogEntry> _filtered = new List<LogEntry>();
        private int _page;

        // ─── Controls ────────────────────────────────────────────────────────

        private ComboBox     _cboFile  = null!;
        private ComboBox     _cboLevel = null!;
        private TextBox      _txtSearch = null!;
        private DataGridView _grid     = null!;
        private RichTextBox  _rtDetail = null!;
        private Button       _btnCopy  = null!;
        private Label        _lblStatus = null!;
        private Label        _lblPage  = null!;
        private Button       _btnPrev  = null!;
        private Button       _btnNext  = null!;

        // ─── Constructor ─────────────────────────────────────────────────────

        public LogViewerDialog()
        {
            BuildUi();
            LoadLogFiles();
        }

        // ─── UI construction ─────────────────────────────────────────────────

        private void BuildUi()
        {
            Text            = Constants.ProductName + " — Log Viewer";
            Size            = new Size(1080, 680);
            MinimumSize     = new Size(800, 520);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor       = Color.FromArgb(250, 250, 252);

            // ── Toolbar ──────────────────────────────────────────────────────
            var toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 46,
                BackColor = Color.FromArgb(240, 242, 248),
                Padding   = new Padding(8, 7, 8, 7)
            };
            new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(210, 215, 225) }
                .Also(sep => toolbar.Controls.Add(sep));

            var lblFile   = MakeLabel("File:",   8,   14);
            _cboFile = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 220, Top = 10, Left = 44
            };
            _cboFile.SelectedIndexChanged += (_, __) => ReloadFile();

            var lblLevel  = MakeLabel("Level:",  276, 14);
            _cboLevel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 90, Top = 10, Left = 320
            };
            _cboLevel.Items.AddRange(new object[] { "All", "ERR", "WRN", "INF", "DBG" });
            _cboLevel.SelectedIndex = 0;
            _cboLevel.SelectedIndexChanged += (_, __) => ApplyFilter();

            var lblSearch = MakeLabel("Search:",  424, 14);
            _txtSearch = new TextBox { Width = 200, Top = 10, Left = 476 };
            _txtSearch.TextChanged += (_, __) => ApplyFilter();

            var btnRefresh = new Button
            {
                Text = "↻  Refresh", Width = 90, Height = 28, Top = 9, Left = 688,
                FlatStyle = FlatStyle.System
            };
            btnRefresh.Click += (_, __) => ReloadFile();

            _lblStatus = MakeLabel(string.Empty, 0, 14);
            _lblStatus.ForeColor = Color.FromArgb(120, 120, 130);
            _lblStatus.Anchor    = AnchorStyles.Top | AnchorStyles.Right;
            toolbar.Layout += (_, __) => _lblStatus.Left = toolbar.Width - 160;
            _lblStatus.Left = 800;

            toolbar.Controls.AddRange(new Control[]
                { lblFile, _cboFile, lblLevel, _cboLevel, lblSearch, _txtSearch, btnRefresh, _lblStatus });

            // ── Status/pagination bar ─────────────────────────────────────────
            var pageBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 46,
                BackColor = Color.FromArgb(240, 242, 248)
            };
            new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(210, 215, 225) }
                .Also(sep => pageBar.Controls.Add(sep));

            _lblPage  = MakeLabel(string.Empty, 10, 14);
            _lblPage.ForeColor = Color.FromArgb(80, 80, 90);

            _btnPrev = new Button { Text = "◀  Prev", Width = 80, Height = 28, Top = 9, Left = 270, FlatStyle = FlatStyle.System };
            _btnNext = new Button { Text = "Next  ▶", Width = 80, Height = 28, Top = 9, Left = 358, FlatStyle = FlatStyle.System };
            _btnPrev.Click += (_, __) => { if (_page > 0) { _page--; RefreshGrid(); } };
            _btnNext.Click += (_, __) =>
            {
                int total = TotalPages();
                if (_page < total - 1) { _page++; RefreshGrid(); }
            };

            var btnClose = new Button
            {
                Text = "Close", Width = 90, Height = 28, Top = 9,
                FlatStyle = FlatStyle.System, DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            pageBar.Layout += (_, __) => btnClose.Left = pageBar.Width - 100;
            btnClose.Left = 970;

            pageBar.Controls.AddRange(new Control[] { _lblPage, _btnPrev, _btnNext, btnClose });
            AcceptButton = btnClose;

            // ── Detail panel ──────────────────────────────────────────────────
            var detailPanel = new Panel { Dock = DockStyle.Bottom, Height = 190 };

            var detailHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 34,
                BackColor = Color.FromArgb(30, 90, 175)
            };
            var lblDetail = new Label
            {
                Text      = "  Entry Details  (full text including exception & stack trace)",
                ForeColor = Color.White,
                Font      = new Font(DefaultFont, FontStyle.Bold),
                AutoSize  = true,
                Top       = 8,
                Left      = 4
            };
            _btnCopy = new Button
            {
                Text      = "⎘  Copy",
                Width     = 80,
                Height    = 26,
                Top       = 4,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnCopy.FlatAppearance.BorderColor = Color.FromArgb(100, 150, 220);
            detailHeader.Layout += (_, __) => _btnCopy.Left = detailHeader.Width - 88;
            _btnCopy.Left = 980;
            _btnCopy.Click += (_, __) =>
            {
                if (!string.IsNullOrWhiteSpace(_rtDetail.Text))
                {
                    Clipboard.SetText(_rtDetail.Text);
                    _btnCopy.Text = "✓  Copied";
                    var t = new System.Windows.Forms.Timer { Interval = 1500 };
                    t.Tick += (__, ___) => { _btnCopy.Text = "⎘  Copy"; t.Stop(); t.Dispose(); };
                    t.Start();
                }
            };
            detailHeader.Controls.AddRange(new Control[] { lblDetail, _btnCopy });

            _rtDetail = new RichTextBox
            {
                Dock      = DockStyle.Fill,
                ReadOnly  = true,
                BackColor = Color.FromArgb(28, 28, 32),
                ForeColor = Color.FromArgb(212, 212, 220),
                Font      = new Font("Consolas", 9),
                WordWrap  = true,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            detailPanel.Controls.Add(_rtDetail);
            detailPanel.Controls.Add(detailHeader);

            // Splitter
            var splitter = new Splitter { Dock = DockStyle.Bottom, Height = 5, BackColor = Color.FromArgb(210, 215, 225) };

            // ── Grid ──────────────────────────────────────────────────────────
            _grid = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                ReadOnly              = true,
                RowHeadersVisible     = false,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect           = false,
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.None,
                BorderStyle           = BorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight   = 28,
                RowTemplate           = { Height = 22 }
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 238, 248);
            _grid.ColumnHeadersDefaultCellStyle.Font      = new Font(DefaultFont, FontStyle.Bold);
            _grid.EnableHeadersVisualStyles               = false;

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Timestamp", Width = 195, Name = "Timestamp",
                DefaultCellStyle = { Font = new Font("Consolas", 8.5f) }
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Level", Width = 52, Name = "Level",
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter, Font = new Font(DefaultFont, FontStyle.Bold) }
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText   = "Message",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                Name         = "Message"
            });

            _grid.SelectionChanged += OnGridSelectionChanged;

            // Assembly order (bottom-to-top for DockStyle.Fill to work correctly)
            Controls.Add(_grid);
            Controls.Add(splitter);
            Controls.Add(detailPanel);
            Controls.Add(toolbar);
            Controls.Add(pageBar);
        }

        // ─── Log file management ──────────────────────────────────────────────

        private void LoadLogFiles()
        {
            _cboFile.Items.Clear();
            var logsPath = Constants.LogsPath;
            if (!Directory.Exists(logsPath)) return;

            var files = Directory.GetFiles(logsPath, "akmlsql*.log")
                                 .OrderByDescending(f => f)
                                 .ToArray();

            foreach (var f in files)
                _cboFile.Items.Add(Path.GetFileName(f));

            if (_cboFile.Items.Count > 0)
                _cboFile.SelectedIndex = 0; // triggers ReloadFile via event
        }

        private void ReloadFile()
        {
            _all.Clear();
            if (_cboFile.SelectedItem == null) { ApplyFilter(); return; }

            var path = Path.Combine(Constants.LogsPath, _cboFile.SelectedItem.ToString()!);
            if (!File.Exists(path)) { ApplyFilter(); return; }

            try
            {
                // FileShare.ReadWrite so we can read while Serilog still has the file open
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                ParseLogEntries(sr);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "LogViewerDialog: could not read log file {Path}", path);
            }

            ApplyFilter();
        }

        private void ParseLogEntries(StreamReader sr)
        {
            LogEntry?     current    = null;
            var           extra      = new StringBuilder();

            void Flush()
            {
                if (current == null) return;
                var tail = extra.ToString().TrimEnd();
                current.FullText = string.IsNullOrEmpty(tail)
                    ? $"{current.Timestamp} [{current.Level}] {current.Message}"
                    : $"{current.Timestamp} [{current.Level}] {current.Message}\n{tail}";
                _all.Add(current);
                extra.Clear();
            }

            while (true)
            {
                var line = sr.ReadLine();
                if (line == null)
                {
                    Flush();
                    break;
                }

                var m = EntryPattern.Match(line);
                if (m.Success)
                {
                    Flush();
                    current = new LogEntry
                    {
                        Timestamp = m.Groups[1].Value,
                        Level     = m.Groups[2].Value,
                        Message   = m.Groups[3].Value
                    };
                }
                else if (current != null)
                {
                    if (extra.Length > 0) extra.AppendLine();
                    extra.Append(line);
                }
            }
        }

        // ─── Filtering ────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            _filtered.Clear();
            var level  = _cboLevel.SelectedItem as string ?? "All";
            var search = _txtSearch.Text.Trim();

            foreach (var e in _all)
            {
                if (level != "All" && !e.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(search) &&
                    e.Message.IndexOf(search,  StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.FullText.IndexOf(search,  StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _filtered.Add(e);
            }

            _page = 0;
            RefreshGrid();
        }

        // ─── Grid population ─────────────────────────────────────────────────

        private void RefreshGrid()
        {
            _grid.SuspendLayout();
            _grid.Rows.Clear();
            _rtDetail.Clear();

            int total = TotalPages();
            if (_page >= total && total > 0) _page = total - 1;

            int start = _page * PageSize;
            int end   = Math.Min(start + PageSize, _filtered.Count);

            for (int i = start; i < end; i++)
            {
                var e   = _filtered[i];
                var idx = _grid.Rows.Add(e.Timestamp, e.Level, e.Message);
                var row = _grid.Rows[idx];
                row.Tag = e;

                row.DefaultCellStyle.ForeColor = e.Level switch
                {
                    "ERR" => Color.FromArgb(200, 50, 50),
                    "WRN" => Color.FromArgb(170, 110, 0),
                    "DBG" => Color.FromArgb(140, 140, 150),
                    _     => SystemColors.WindowText
                };
            }

            _grid.ResumeLayout();

            _lblStatus.Text = $"{_all.Count:N0} entries loaded";
            _lblPage.Text   = _filtered.Count == 0
                ? "No entries match the current filter"
                : $"Page {_page + 1} of {total}  ·  {_filtered.Count:N0} matching";

            _btnPrev.Enabled = _page > 0;
            _btnNext.Enabled = _page < total - 1;
        }

        private int TotalPages() =>
            Math.Max(1, (int)Math.Ceiling((double)_filtered.Count / PageSize));

        // ─── Selection ────────────────────────────────────────────────────────

        private void OnGridSelectionChanged(object sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) { _rtDetail.Clear(); return; }

            if (_grid.SelectedRows[0].Tag is LogEntry entry)
                ShowDetail(entry);
        }

        private void ShowDetail(LogEntry entry)
        {
            _rtDetail.Clear();
            _rtDetail.SelectionColor = Color.FromArgb(150, 200, 255);
            _rtDetail.AppendText(entry.FullText.Contains('\n')
                ? entry.FullText.Substring(0, entry.FullText.IndexOf('\n'))
                : entry.FullText);

            // Append exception / stack trace in a lighter colour
            int nl = entry.FullText.IndexOf('\n');
            if (nl >= 0)
            {
                _rtDetail.SelectionColor = Color.FromArgb(180, 180, 185);
                _rtDetail.AppendText(entry.FullText.Substring(nl));
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static Label MakeLabel(string text, int left, int top) =>
            new Label { Text = text, AutoSize = true, Left = left, Top = top };
    }

    // ─── Tiny fluent helper ───────────────────────────────────────────────────

    internal static class ObjectExtensions
    {
        internal static T Also<T>(this T value, Action<T> action) { action(value); return value; }
    }
}
