using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Spec 030 T053 (FR-026) — Manage Code Analysis Rules dialog.
    ///
    /// A read-only-chrome WinForms grid (matching <see cref="Dialogs.BulkAnalysisResultDialog"/>) of
    /// every analysis rule (loaded from the engine via <c>ListAnalysisRules</c>), grouped/sorted by
    /// category, with an editable Enabled checkbox and a Severity dropdown per row. The owning
    /// command reads <see cref="GetOverrides"/> on OK, persists the deviations to
    /// <c>config.json codeAnalysis.ruleOverrides</c>, and notifies the engine.
    /// </summary>
    internal sealed class ManageRulesDialog : Form
    {
        // DiagnosticSeverity: Hint=0, Information=1, Warning=2, Error=3.
        private static readonly string[] SeverityLabels = { "Hint", "Information", "Warning", "Error" };

        private readonly IReadOnlyList<AnalysisRuleInfoDto> _rules;
        private DataGridView _grid = null!;

        private const string ColRuleId      = "RuleId";
        private const string ColName        = "Name";
        private const string ColCategory    = "Category";
        private const string ColEnabled     = "Enabled";
        private const string ColSeverity    = "Severity";
        private const string ColAutoFix     = "AutoFix";
        private const string ColDescription = "Description";

        public ManageRulesDialog(IReadOnlyList<AnalysisRuleInfoDto> rules)
        {
            _rules = rules ?? Array.Empty<AnalysisRuleInfoDto>();
            Build();
        }

        private void Build()
        {
            Text            = Constants.ProductName + " — Manage Code Analysis Rules";
            Size            = new Size(980, 600);
            MinimumSize     = new Size(760, 440);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar   = false;
            BackColor       = Color.FromArgb(250, 250, 252);

            // ─── Header strip ─────────────────────────────────────────────────
            var header = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 44,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding   = new Padding(12, 8, 12, 8)
            };
            header.Controls.Add(new Label
            {
                Dock      = DockStyle.Fill,
                Text      = $"{_rules.Count} rules. Toggle Enabled or change Severity, then Save. " +
                            "Project .casettings still override these per-folder.",
                ForeColor = Color.FromArgb(70, 70, 80),
                TextAlign = ContentAlignment.MiddleLeft
            });

            // ─── Grid ─────────────────────────────────────────────────────────
            _grid = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible     = false,
                SelectionMode         = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.None,
                MultiSelect           = false,
                BorderStyle           = BorderStyle.None,
                EditMode              = DataGridViewEditMode.EditOnEnter,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight   = 28,
                RowTemplate           = { Height = 24 }
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 238, 245);
            _grid.ColumnHeadersDefaultCellStyle.Font      = new Font(DefaultFont, FontStyle.Bold);
            _grid.EnableHeadersVisualStyles               = false;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = ColRuleId, HeaderText = "Rule", Width = 64, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = ColName, HeaderText = "Name", Width = 230, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = ColCategory, HeaderText = "Category", Width = 110, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = ColEnabled, HeaderText = "Enabled", Width = 64 });
            var severityCol = new DataGridViewComboBoxColumn
            {
                Name = ColSeverity,
                HeaderText = "Severity",
                Width = 104,
                FlatStyle = FlatStyle.Flat
            };
            severityCol.Items.AddRange(SeverityLabels);  // fixed list — Items avoids DataSource binding quirks
            _grid.Columns.Add(severityCol);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = ColAutoFix, HeaderText = "Fix", Width = 40, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ColDescription,
                HeaderText = "Description",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ReadOnly = true
            });

            foreach (var dto in _rules.OrderBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(r => r.RuleId, StringComparer.OrdinalIgnoreCase))
            {
                int idx = _grid.Rows.Add(
                    dto.RuleId,
                    dto.Name,
                    dto.Category,
                    dto.Enabled,
                    SeverityLabel(dto.EffectiveSeverity),
                    dto.AutoFixable ? "✓" : string.Empty,
                    dto.Description);
                _grid.Rows[idx].Tag = dto;
            }

            // ─── Footer (Save / Cancel) ───────────────────────────────────────
            var footer = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 48,
                BackColor = Color.FromArgb(242, 242, 245)
            };
            footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(210, 210, 215) });

            var btnSave = new Button
            {
                Text         = "Save",
                Width        = 90,
                Height       = 28,
                Top          = 10,
                FlatStyle    = FlatStyle.System,
                DialogResult = DialogResult.OK,
                Anchor       = AnchorStyles.Top | AnchorStyles.Right
            };
            var btnCancel = new Button
            {
                Text         = "Cancel",
                Width        = 90,
                Height       = 28,
                Top          = 10,
                FlatStyle    = FlatStyle.System,
                DialogResult = DialogResult.Cancel,
                Anchor       = AnchorStyles.Top | AnchorStyles.Right
            };
            void PositionFooterButtons()
            {
                btnSave.Left   = footer.Width - 100;
                btnCancel.Left = footer.Width - 198;
            }
            footer.Layout += (_, __) => PositionFooterButtons();
            PositionFooterButtons();

            footer.Controls.Add(btnSave);
            footer.Controls.Add(btnCancel);

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Controls.Add(_grid);
            Controls.Add(header);
            Controls.Add(footer);
        }

        /// <summary>
        /// Collects the per-rule deviations from the grid: a rule is an override only when it is
        /// disabled or its severity differs from the rule's built-in default; rules left at their
        /// default are omitted (so toggling one back to default removes its override).
        /// </summary>
        public Dictionary<string, RuleOverride> GetOverrides()
        {
            _grid.EndEdit();
            var result = new Dictionary<string, RuleOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not AnalysisRuleInfoDto dto) continue;

                bool enabled = Convert.ToBoolean(row.Cells[ColEnabled].Value ?? true);
                int  sevIdx  = LabelToSeverity(row.Cells[ColSeverity].Value?.ToString());

                bool isDefault = enabled && sevIdx == dto.DefaultSeverity;
                if (isDefault) continue;

                result[dto.RuleId] = new RuleOverride
                {
                    Enabled  = enabled,
                    Severity = SeverityToString(sevIdx)
                };
            }

            return result;
        }

        private static string SeverityLabel(int severity) =>
            severity >= 0 && severity < SeverityLabels.Length ? SeverityLabels[severity] : "Warning";

        private static int LabelToSeverity(string? label)
        {
            int idx = Array.IndexOf(SeverityLabels, label);
            return idx >= 0 ? idx : 2; // default Warning
        }

        private static string SeverityToString(int severity) => severity switch
        {
            0 => "hint",
            1 => "information",
            2 => "warning",
            3 => "error",
            _ => "warning"
        };
    }
}
