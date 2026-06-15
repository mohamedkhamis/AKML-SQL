using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Ipc.Messages;
using Constants = AkmlSql.Core.Constants;

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
            Text            = Constants.ProductName + " — Find Invalid Objects";
            Size            = new Size(960, 560);
            MinimumSize     = new Size(720, 380);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
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
                Text      = _records.Count == 0
                                ? $"No invalid objects found ({_totalScanned} dependency rows scanned)."
                                : $"{_records.Count} invalid object(s) — references that can no longer be resolved.",
                ForeColor = _records.Count == 0 ? Color.FromArgb(0, 110, 50) : Color.FromArgb(160, 60, 0),
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

            Controls.Add(grid);
            Controls.Add(summary);
            Controls.Add(footer);
        }

        private static string TypeLabel(int type) =>
            type >= 0 && type < TypeLabels.Length ? TypeLabels[type] : "Object";
    }
}
