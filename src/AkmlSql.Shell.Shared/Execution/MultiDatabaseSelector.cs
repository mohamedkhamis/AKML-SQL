#nullable enable
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Drawing;
using System.Windows.Forms;
using Serilog;

namespace AkmlSql.Shell.Shared.Execution
{
    /// <summary>
    /// T078: WinForms dialog that lists databases from the current server connection
    /// and allows the user to select multiple databases for multi-database query execution.
    /// </summary>
    internal sealed class MultiDatabaseSelector : Form
    {
        private readonly CheckedListBox _databaseList;
        private readonly Button _selectAllButton;
        private readonly Button _deselectAllButton;
        private readonly Button _executeButton;
        private readonly Button _cancelButton;
        private readonly Label _statusLabel;
        private readonly TextBox _filterBox;

        private readonly List<string> _allDatabases = new List<string>();

        /// <summary>
        /// Gets the list of databases selected by the user when the dialog was closed with OK.
        /// </summary>
        public List<string> SelectedDatabases { get; } = new List<string>();

        public MultiDatabaseSelector(string serverName)
        {
            Text = $"Multi-Database Execution — {serverName}";
            Size = new Size(420, 520);
            MinimumSize = new Size(350, 400);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };

            var filterLabel = new Label
            {
                Text = "Filter databases:",
                Location = new Point(12, 12),
                AutoSize = true
            };

            _filterBox = new TextBox
            {
                Location = new Point(12, 32),
                Width = 380,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _filterBox.TextChanged += OnFilterChanged;

            topPanel.Controls.Add(filterLabel);
            topPanel.Controls.Add(_filterBox);

            _databaseList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                Font = new Font("Consolas", 9.5f)
            };

            var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 80 };

            _selectAllButton = new Button
            {
                Text = "Select All",
                Size = new Size(90, 28),
                Location = new Point(12, 8),
                FlatStyle = FlatStyle.System
            };
            _selectAllButton.Click += OnSelectAll;

            _deselectAllButton = new Button
            {
                Text = "Deselect All",
                Size = new Size(90, 28),
                Location = new Point(110, 8),
                FlatStyle = FlatStyle.System
            };
            _deselectAllButton.Click += OnDeselectAll;

            _statusLabel = new Label
            {
                Text = "Loading databases...",
                Location = new Point(12, 44),
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };

            _executeButton = new Button
            {
                Text = "Execute",
                Size = new Size(90, 28),
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.System,
                Font = new Font(DefaultFont, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Size = new Size(90, 28),
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.System,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            buttonPanel.Layout += (_, __) =>
            {
                int top = 44;
                _cancelButton.Location = new Point(buttonPanel.Width - 100, 8);
                _executeButton.Location = new Point(buttonPanel.Width - 198, 8);
            };

            buttonPanel.Controls.AddRange(new Control[]
            {
                _selectAllButton, _deselectAllButton, _statusLabel,
                _executeButton, _cancelButton
            });

            Controls.Add(_databaseList);
            Controls.Add(topPanel);
            Controls.Add(buttonPanel);

            AcceptButton = _executeButton;
            CancelButton = _cancelButton;

            FormClosing += OnFormClosing;
        }

        /// <summary>
        /// Loads the database list from the given connection string.
        /// Call after showing the dialog or use <see cref="LoadDatabasesAsync"/>.
        /// </summary>
        public void LoadDatabases(string connectionString)
        {
            try
            {
                _allDatabases.Clear();

                using var conn = new SqlConnection(connectionString);
                conn.Open();

                using var cmd = new SqlCommand(
                    "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' " +
                    "AND database_id > 4 ORDER BY name", conn);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    _allDatabases.Add(reader.GetString(0));
                }

                RefreshList();
                _statusLabel.Text = $"{_allDatabases.Count} databases found";
                Log.Debug("MultiDatabaseSelector: loaded {Count} databases", _allDatabases.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MultiDatabaseSelector: failed to load databases");
                _statusLabel.Text = "Failed to load databases";
                _statusLabel.ForeColor = Color.Red;
                MessageBox.Show($"Failed to load databases: {ex.Message}",
                    Core.Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshList()
        {
            var filter = _filterBox.Text.Trim();
            _databaseList.Items.Clear();

            foreach (var db in _allDatabases)
            {
                if (string.IsNullOrEmpty(filter) ||
                    db.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _databaseList.Items.Add(db);
                }
            }
        }

        private void OnFilterChanged(object? sender, EventArgs e)
        {
            RefreshList();
        }

        private void OnSelectAll(object? sender, EventArgs e)
        {
            for (int i = 0; i < _databaseList.Items.Count; i++)
                _databaseList.SetItemChecked(i, true);
        }

        private void OnDeselectAll(object? sender, EventArgs e)
        {
            for (int i = 0; i < _databaseList.Items.Count; i++)
                _databaseList.SetItemChecked(i, false);
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) return;

            SelectedDatabases.Clear();
            foreach (var item in _databaseList.CheckedItems)
            {
                SelectedDatabases.Add(item.ToString()!);
            }

            if (SelectedDatabases.Count == 0)
            {
                MessageBox.Show("Please select at least one database.",
                    Core.Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
            }
        }
    }
}
