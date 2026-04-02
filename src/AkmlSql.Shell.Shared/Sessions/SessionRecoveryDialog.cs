#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Shell.Shared.Sessions
{
    /// <summary>
    /// WinForms dialog shown on startup when recoverable sessions are found after a crash.
    /// Lists recovered tabs with checkboxes so the user can select which tabs to restore.
    /// Uses programmatic layout (no Designer), following the existing dialog pattern in the codebase.
    /// </summary>
    internal class SessionRecoveryDialog : Form
    {
        private readonly RecoverableSessionDto[] _sessions;
        private CheckedListBox _tabListBox = null!;
        private Label _infoLabel = null!;
        private ComboBox _sessionCombo = null!;

        /// <summary>
        /// The tabs the user selected for restoration. Populated after <see cref="DialogResult.OK"/>.
        /// </summary>
        public List<TabSnapshotDto> SelectedTabs { get; } = new();

        /// <summary>
        /// The session ID the user chose to restore from (for deletion after restore).
        /// </summary>
        public string? SelectedSessionId { get; private set; }

        public SessionRecoveryDialog(RecoverableSessionDto[] sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "AKML SQL - Session Recovery";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(560, 480);
            ShowInTaskbar = false;

            _infoLabel = new Label
            {
                Text = "A previous session was not closed properly. Select tabs to restore:",
                Location = new Point(16, 16),
                Size = new Size(520, 20),
                AutoSize = false
            };

            var sessionLabel = new Label
            {
                Text = "Session:",
                Location = new Point(16, 44),
                AutoSize = true
            };

            _sessionCombo = new ComboBox
            {
                Location = new Point(80, 40),
                Size = new Size(456, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            foreach (var session in _sessions)
            {
                var capturedAt = DateTime.TryParse(session.CapturedAt, null,
                    DateTimeStyles.RoundtripKind, out var dt)
                    ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : session.CapturedAt;
                _sessionCombo.Items.Add($"{capturedAt} ({session.TabCount} tabs) - {session.SessionId[..Math.Min(8, session.SessionId.Length)]}...");
            }

            if (_sessionCombo.Items.Count > 0)
            {
                _sessionCombo.SelectedIndex = 0;
            }

            _sessionCombo.SelectedIndexChanged += OnSessionChanged;

            _tabListBox = new CheckedListBox
            {
                Location = new Point(16, 72),
                Size = new Size(520, 300),
                CheckOnClick = true
            };

            var selectAllButton = new Button
            {
                Text = "Select All",
                Location = new Point(16, 382),
                Size = new Size(90, 28)
            };
            selectAllButton.Click += (_, _) => SetAllChecked(true);

            var selectNoneButton = new Button
            {
                Text = "Select None",
                Location = new Point(112, 382),
                Size = new Size(90, 28)
            };
            selectNoneButton.Click += (_, _) => SetAllChecked(false);

            var okButton = new Button
            {
                Text = "Restore Selected",
                Location = new Point(326, 416),
                Size = new Size(110, 30),
                DialogResult = DialogResult.OK
            };
            okButton.Click += OnOkClick;

            var cancelButton = new Button
            {
                Text = "Dismiss",
                Location = new Point(442, 416),
                Size = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = okButton;
            CancelButton = cancelButton;

            Controls.AddRange(new Control[]
            {
                _infoLabel, sessionLabel, _sessionCombo, _tabListBox,
                selectAllButton, selectNoneButton, okButton, cancelButton
            });

            // Populate tabs for the first session
            if (_sessions.Length > 0)
            {
                PopulateTabs(_sessions[0]);
            }
        }

        private void OnSessionChanged(object? sender, EventArgs e)
        {
            var idx = _sessionCombo.SelectedIndex;
            if (idx >= 0 && idx < _sessions.Length)
            {
                PopulateTabs(_sessions[idx]);
            }
        }

        private void PopulateTabs(RecoverableSessionDto session)
        {
            _tabListBox.Items.Clear();

            foreach (var tab in session.Tabs)
            {
                var displayText = BuildTabDisplayText(tab);
                _tabListBox.Items.Add(displayText, isChecked: true);
            }
        }

        private static string BuildTabDisplayText(TabSnapshotDto tab)
        {
            var parts = new List<string>();

            // Title
            parts.Add(tab.Title ?? $"Tab {tab.TabIndex}");

            // File path (truncated)
            if (!string.IsNullOrEmpty(tab.FilePath))
            {
                var path = tab.FilePath!;
                if (path.Length > 50)
                {
                    path = "..." + path.Substring(path.Length - 47);
                }
                parts.Add(path);
            }

            // Connection info
            if (!string.IsNullOrEmpty(tab.Server))
            {
                var conn = tab.Server!;
                if (!string.IsNullOrEmpty(tab.Database))
                {
                    conn += "." + tab.Database;
                }
                parts.Add("[" + conn + "]");
            }

            return string.Join("  |  ", parts);
        }

        private void SetAllChecked(bool isChecked)
        {
            for (int i = 0; i < _tabListBox.Items.Count; i++)
            {
                _tabListBox.SetItemChecked(i, isChecked);
            }
        }

        private void OnOkClick(object? sender, EventArgs e)
        {
            var sessionIdx = _sessionCombo.SelectedIndex;
            if (sessionIdx < 0 || sessionIdx >= _sessions.Length)
            {
                return;
            }

            var session = _sessions[sessionIdx];
            SelectedSessionId = session.SessionId;

            SelectedTabs.Clear();
            for (int i = 0; i < _tabListBox.Items.Count; i++)
            {
                if (_tabListBox.GetItemChecked(i) && i < session.Tabs.Length)
                {
                    SelectedTabs.Add(session.Tabs[i]);
                }
            }
        }
    }
}
