using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ui;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// Spec 030 T059 (FR-019) — results list for "Find Invalid Objects". A read-only grid (matching
    /// <see cref="Dialogs.BulkAnalysisResultDialog"/>) of objects whose definitions reference an
    /// entity SQL Server can no longer resolve, as found by the engine's <c>FindInvalidObjects</c>
    /// scan (<see cref="InvalidObjectRecord"/>).
    /// </summary>
    internal sealed class FindInvalidObjectsDialog : Form
    {
        private static readonly string[] TypeLabels = { "Table", "View", "Procedure", "Function", "Trigger", "Synonym" };

        private readonly IReadOnlyList<InvalidObjectRecord> _records;
        private readonly int _totalScanned;

        public FindInvalidObjectsDialog(IReadOnlyList<InvalidObjectRecord> records, int totalScanned)
        {
            _records      = records ?? Array.Empty<InvalidObjectRecord>();
            _totalScanned = totalScanned;
            Build();
        }

        private void Build()
        {
            Text            = WindowTitles.For("Find invalid objects");
            WindowIcon.Apply(this);
            var showsGrid   = ShowsGrid(_records.Count);
            // Spec 047 (SSMS-003): with nothing to list, the window is the summary, not an empty grey grid.
            Size            = showsGrid ? new Size(960, 560) : new Size(560, 150);
            MinimumSize     = showsGrid ? new Size(720, 380) : new Size(420, 150);
            StartPosition   = FormStartPosition.CenterParent;
            // A one-line answer is a fixed dialog; a list can be made larger. A modal window is never minimised (it offered
            // Minimise and Maximise for the sentence "No invalid objects found. All 63 references resolve.").
            FormBorderStyle = showsGrid ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
            MinimizeBox     = false;
            MaximizeBox     = showsGrid;
            ShowInTaskbar   = false;
            BackColor       = Color.FromArgb(250, 250, 252);

            // ─── Summary strip ────────────────────────────────────────────────
            var summary = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 44,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding   = new Padding(12, 8, 12, 8)
            };
            summary.Controls.Add(new Label
            {
                Dock      = DockStyle.Fill,
                Text      = SummaryText(_records.Count, _totalScanned),
                ForeColor = _records.Count > 0 ? Color.FromArgb(160, 60, 0)
                          : _totalScanned > 0  ? Color.FromArgb(0, 110, 50)
                          : Color.FromArgb(60, 60, 60),
                Font      = new Font(DefaultFont, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });

            // ─── Grid ─────────────────────────────────────────────────────────
            var grid = new DataGridView
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
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 238, 245);
            grid.ColumnHeadersDefaultCellStyle.Font      = new Font(DefaultFont, FontStyle.Bold);
            grid.EnableHeadersVisualStyles               = false;

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Schema", Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Object", Width = 200 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Missing Dependency", Width = 200 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Error", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            foreach (var r in _records.OrderBy(r => r.Schema, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                grid.Rows.Add(r.Schema, r.Name, TypeLabel(r.Type), r.MissingDependency ?? string.Empty, r.ErrorMessage);
            }

            // ─── Footer ───────────────────────────────────────────────────────
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Color.FromArgb(242, 242, 245) };
            footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(210, 210, 215) });
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
            footer.Layout += (_, __) => btnClose.Left = footer.Width - 100;
            btnClose.Left = 860;
            footer.Controls.Add(btnClose);
            AcceptButton = btnClose;
            CancelButton = btnClose;   // Escape closes it too, as it closes every other dialog

            if (showsGrid) Controls.Add(grid);
            else grid.Dispose();
            Controls.Add(summary);
            Controls.Add(footer);
        }

        /// <summary>
        /// Spec 047 (SSMS-003, data-model §9): what the scan found, given the records and the references it checked
        /// (<see cref="FindInvalidObjectsResponse.TotalScanned"/>, every reference of a user object).
        /// </summary>
        internal static string SummaryText(int records, int totalScanned)
        {
            if (records == 1) return "1 invalid object — a reference that can no longer be resolved.";
            if (records > 1) return $"{records} invalid objects — references that can no longer be resolved.";
            if (totalScanned <= 0) return "This database has no object references to check.";
            return totalScanned == 1
                ? "No invalid objects found. The 1 reference resolves."
                : $"No invalid objects found. All {totalScanned} references resolve.";
        }

        /// <summary>Spec 047 (SSMS-003): the grid is shown only when it has something to list.</summary>
        internal static bool ShowsGrid(int records) => records > 0;

        private static string TypeLabel(int type) =>
            type >= 0 && type < TypeLabels.Length ? TypeLabels[type] : "Object";
    }
}
