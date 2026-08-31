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
        private readonly HashSet<string> _sessionSuppressed;
        private DataGridView _grid = null!;
        private Label _sessionLabel = null!;
        private Button _sessionRestore = null!;

        /// <summary>
        /// True when the user asked to lift the session-only suppressions. The owning command
        /// applies it on Save (the same gesture that commits the grid), so Cancel backs out of
        /// this too.
        /// </summary>
        public bool RestoreSessionSuppressions { get; private set; }

        private const string ColRuleId      = "RuleId";
        private const string ColName        = "Name";
        private const string ColCategory    = "Category";
        private const string ColEnabled     = "Enabled";
        private const string ColSeverity    = "Severity";
        private const string ColAutoFix     = "AutoFix";
        private const string ColDescription = "Description";

        /// <param name="rules">The catalog, from the engine's ListAnalysisRules.</param>
        /// <param name="sessionSuppressedRules">
        /// Rules switched off for this session only (engine memory, nothing on disk). They are
        /// listed so the scope is not a one-way door — without somewhere to see and undo it, a
        /// session suppression would be invisible and irreversible until the IDE restarts.
        /// </param>
        public ManageRulesDialog(
            IReadOnlyList<AnalysisRuleInfoDto> rules,
            IReadOnlyList<string> sessionSuppressedRules = null)
        {
            _rules = rules ?? Array.Empty<AnalysisRuleInfoDto>();
            _sessionSuppressed = new HashSet<string>(
                sessionSuppressedRules ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
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

                // A session-suppressed rule still shows Enabled here — that is accurate, the
                // override is elsewhere — so mark the row rather than lying about the checkbox,
                // which would also corrupt the changed/unchanged comparison in GetOverrides.
                if (_sessionSuppressed.Contains(dto.RuleId))
                {
                    var row = _grid.Rows[idx];
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 226);
                    row.Cells[ColRuleId].ToolTipText =
                        $"{dto.RuleId} is disabled for this session only. It is not saved anywhere " +
                        "and comes back when the IDE restarts, or when you restore it below.";
                }
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

            // ─── Session-suppression strip ────────────────────────────────────
            // Only present when there is something to undo, so the dialog is unchanged for the
            // (common) case of no session suppressions.
            var sessionStrip = BuildSessionStrip();

            // WinForms docks from the back of the z-order forward, so the control added LAST claims
            // the outermost position at its edge. Adding the footer after the strip therefore keeps
            // Save/Cancel flush with the bottom and puts the strip directly above them.
            Controls.Add(_grid);
            Controls.Add(header);
            if (sessionStrip != null) Controls.Add(sessionStrip);
            Controls.Add(footer);
        }

        /// <summary>
        /// The "disabled for this session only" strip: what is suppressed, and one button to put it
        /// back. Returns <c>null</c> when nothing is session-suppressed.
        /// </summary>
        private Panel BuildSessionStrip()
        {
            if (_sessionSuppressed.Count == 0) return null;

            var strip = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 42,
                BackColor = Color.FromArgb(255, 249, 226),
                Padding   = new Padding(12, 0, 12, 0)
            };
            strip.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(232, 220, 170) });

            _sessionRestore = new Button
            {
                Text      = "Restore",
                Width     = 90,
                Height    = 26,
                Top       = 8,
                FlatStyle = FlatStyle.System,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };

            var ordered = _sessionSuppressed.OrderBy(r => r, StringComparer.OrdinalIgnoreCase);
            _sessionLabel = new Label
            {
                Dock      = DockStyle.Fill,
                Text      = "Disabled for this session only: " + string.Join(", ", ordered),
                ForeColor = Color.FromArgb(90, 75, 30),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(0, 0, 100, 0)
            };

            _sessionRestore.Click += (_, __) =>
            {
                RestoreSessionSuppressions = true;
                _sessionLabel.Text = "These rules will be restored when you click Save.";
                _sessionRestore.Enabled = false;
            };

            void PositionRestore() => _sessionRestore.Left = strip.Width - 102;
            strip.Layout += (_, __) => PositionRestore();
            PositionRestore();

            strip.Controls.Add(_sessionLabel);
            strip.Controls.Add(_sessionRestore);
            return strip;
        }

        /// <summary>
        /// Collects the per-rule global overrides to persist. The caller replaces
        /// <c>config.json codeAnalysis.ruleOverrides</c> wholesale with this result, so the method
        /// must preserve any existing global overrides the user did NOT touch in this session.
        ///
        /// Strategy:
        /// 1. Seed <paramref name="result"/> from the current on-disk global overrides so that
        ///    untouched rules keep their saved global setting.
        /// 2. For each grid row compare the current value against the effective baseline shown on
        ///    open (dto.Enabled / dto.EffectiveSeverity — the values the row was populated with):
        ///    - Unchanged → skip; the seeded value (if any) survives unchanged.
        ///    - Changed AND still differs from built-in default → write/replace the override.
        ///    - Changed AND reverted to built-in default → remove the override entry entirely
        ///      (lets the user explicitly clear a rule back to factory default).
        ///
        /// This avoids the old bug where comparing against dto.DefaultSeverity would silently bake
        /// project-.casettings values into config.json on every Save.
        /// </summary>
        public Dictionary<string, RuleOverride> GetOverrides()
        {
            _grid.EndEdit();

            // Seed from the current global config so untouched rows preserve existing overrides.
            var existing = ConfigManager.Load().CodeAnalysis.RuleOverrides;
            var result   = new Dictionary<string, RuleOverride>(existing, StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not AnalysisRuleInfoDto dto) continue;

                bool enabled = Convert.ToBoolean(row.Cells[ColEnabled].Value ?? true);
                int  sevIdx  = LabelToSeverity(row.Cells[ColSeverity].Value?.ToString());

                // If the user left the row exactly as it was displayed on open, do nothing —
                // the seeded value (if any) is already in result.
                bool unchanged = enabled == dto.Enabled && sevIdx == dto.EffectiveSeverity;
                if (unchanged) continue;

                // User changed something. If they reverted all the way back to the rule's built-in
                // default, remove any override so the rule falls through to engine defaults.
                if (enabled && sevIdx == dto.DefaultSeverity)
                {
                    result.Remove(dto.RuleId);
                }
                else
                {
                    result[dto.RuleId] = new RuleOverride
                    {
                        Enabled  = enabled,
                        Severity = SeverityToString(sevIdx)
                    };
                }
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
