#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T076: WinForms dialog for editing a single cell value. Shows the current value in a
    /// read-only field, a TextBox for the new value, and generates the corresponding UPDATE SQL
    /// for confirmation before execution. Requires table PK info from the schema cache to build
    /// a safe WHERE clause.
    /// </summary>
    internal sealed class CellEditDialog : Form
    {
        private TextBox _currentValueBox;
        private TextBox _newValueBox;
        private TextBox _sqlPreviewBox;
        private CheckBox _setNullCheckBox;
        private Button _executeButton;
        private Button _cancelButton;
        private Label _warningLabel;

        private readonly string _tableName;
        private readonly string _columnName;
        private readonly string _currentValue;
        private readonly string? _pkColumnName;
        private readonly string? _pkValue;

        /// <summary>
        /// Gets the generated UPDATE SQL statement if the user confirms.
        /// </summary>
        public string? GeneratedSql { get; private set; }

        /// <summary>
        /// Gets the new value entered by the user.
        /// </summary>
        public string? NewValue { get; private set; }

        private CellEditDialog(
            string tableName,
            string columnName,
            string currentValue,
            string? pkColumnName,
            string? pkValue)
        {
            _tableName = tableName;
            _columnName = columnName;
            _currentValue = currentValue;
            _pkColumnName = pkColumnName;
            _pkValue = pkValue;

            Text = $"{Constants.ProductName} - Edit Cell";
            Size = new Size(520, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;

            BuildLayout();
            UpdateSqlPreview();
        }

        /// <summary>
        /// Shows the cell edit dialog for a specific cell.
        /// </summary>
        /// <param name="tableName">Fully qualified table name (e.g. "dbo.Customers").</param>
        /// <param name="columnName">The column name to update.</param>
        /// <param name="currentValue">Current display value of the cell.</param>
        /// <param name="pkColumnName">Primary key column name (null if unknown).</param>
        /// <param name="pkValue">Primary key value for the row (null if unknown).</param>
        /// <returns>DialogResult.OK with <see cref="GeneratedSql"/> populated, or Cancel.</returns>
        public static CellEditDialog Create(
            string tableName,
            string columnName,
            string currentValue,
            string? pkColumnName = null,
            string? pkValue = null)
        {
            return new CellEditDialog(tableName, columnName, currentValue, pkColumnName, pkValue);
        }

        /// <summary>
        /// Shows the dialog for an active grid cell. Attempts to determine table/column info
        /// from the grid column headers. Falls back to manual entry if PK is unknown.
        /// </summary>
        public static DialogResult ShowForCell(DataGridView grid, int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.RowCount
                             || columnIndex < 0 || columnIndex >= grid.ColumnCount)
            {
                return DialogResult.Cancel;
            }

            try
            {
                var columnName = grid.Columns[columnIndex].HeaderText ?? $"Column{columnIndex}";
                var currentValue = grid.Rows[rowIndex].Cells[columnIndex].Value;
                var currentValueStr = currentValue == null || currentValue is DBNull
                    ? "NULL"
                    : currentValue.ToString() ?? string.Empty;

                // Attempt to find PK — use first column as a heuristic
                string? pkColumnName = null;
                string? pkValue = null;
                if (grid.ColumnCount > 0)
                {
                    pkColumnName = grid.Columns[0].HeaderText;
                    var pkCellValue = grid.Rows[rowIndex].Cells[0].Value;
                    if (pkCellValue != null && !(pkCellValue is DBNull))
                    {
                        pkValue = pkCellValue.ToString();
                    }
                }

                // Table name is unknown from grid alone — user must verify
                var tableName = "[TableName]";

                using var dialog = Create(tableName, columnName, currentValueStr, pkColumnName, pkValue);
                var result = dialog.ShowDialog();
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CellEditDialog: failed to show for cell ({Row}, {Col})", rowIndex, columnIndex);
                return DialogResult.Cancel;
            }
        }

        private void BuildLayout()
        {
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(12),
                AutoSize = false
            };

            int row = 0;

            // Table/Column info
            var infoLabel = new Label
            {
                Text = $"Table: {_tableName}    Column: {_columnName}",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
                Padding = new Padding(0, 0, 0, 8)
            };
            mainPanel.Controls.Add(infoLabel);
            row++;

            // Current value
            var currentLabel = new Label
            {
                Text = "Current Value:",
                AutoSize = true,
                Padding = new Padding(0, 4, 0, 2)
            };
            mainPanel.Controls.Add(currentLabel);
            row++;

            _currentValueBox = new TextBox
            {
                Text = _currentValue,
                ReadOnly = true,
                Multiline = true,
                Height = 50,
                Dock = DockStyle.Top,
                Font = new Font("Consolas", 9f),
                BackColor = SystemColors.Control,
                ScrollBars = ScrollBars.Vertical
            };
            mainPanel.Controls.Add(_currentValueBox);
            row++;

            // New value
            var newLabel = new Label
            {
                Text = "New Value:",
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 2)
            };
            mainPanel.Controls.Add(newLabel);
            row++;

            _newValueBox = new TextBox
            {
                Multiline = true,
                Height = 50,
                Dock = DockStyle.Top,
                Font = new Font("Consolas", 9f),
                ScrollBars = ScrollBars.Vertical
            };
            _newValueBox.TextChanged += (_, __) => UpdateSqlPreview();
            mainPanel.Controls.Add(_newValueBox);
            row++;

            _setNullCheckBox = new CheckBox
            {
                Text = "Set to NULL",
                AutoSize = true,
                Padding = new Padding(0, 4, 0, 4)
            };
            _setNullCheckBox.CheckedChanged += (_, __) =>
            {
                _newValueBox.Enabled = !_setNullCheckBox.Checked;
                UpdateSqlPreview();
            };
            mainPanel.Controls.Add(_setNullCheckBox);
            row++;

            // SQL preview
            var sqlLabel = new Label
            {
                Text = "Generated SQL:",
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 2)
            };
            mainPanel.Controls.Add(sqlLabel);
            row++;

            _sqlPreviewBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Height = 70,
                Dock = DockStyle.Top,
                Font = new Font("Consolas", 9f),
                BackColor = Color.FromArgb(255, 255, 240),
                ScrollBars = ScrollBars.Vertical
            };
            mainPanel.Controls.Add(_sqlPreviewBox);
            row++;

            // Warning label (shown when PK is unknown)
            _warningLabel = new Label
            {
                Text = string.IsNullOrEmpty(_pkColumnName)
                    ? "Warning: Primary key is unknown. UPDATE may affect multiple rows. Please review the WHERE clause carefully."
                    : string.Empty,
                AutoSize = true,
                ForeColor = Color.DarkRed,
                Font = new Font(Font.FontFamily, 8, FontStyle.Italic),
                Padding = new Padding(0, 4, 0, 4),
                Visible = string.IsNullOrEmpty(_pkColumnName)
            };
            mainPanel.Controls.Add(_warningLabel);
            row++;

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(4)
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Width = 80,
                Height = 28,
                DialogResult = DialogResult.Cancel
            };

            _executeButton = new Button
            {
                Text = "Copy SQL",
                Width = 100,
                Height = 28
            };
            _executeButton.Click += OnExecuteClick;

            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_executeButton);

            CancelButton = _cancelButton;

            Controls.Add(mainPanel);
            Controls.Add(buttonPanel);
        }

        private void UpdateSqlPreview()
        {
            var newValue = _setNullCheckBox.Checked
                ? "NULL"
                : $"'{EscapeSqlString(_newValueBox.Text)}'";

            string whereClause;
            if (!string.IsNullOrEmpty(_pkColumnName) && !string.IsNullOrEmpty(_pkValue))
            {
                whereClause = $"[{_pkColumnName}] = '{EscapeSqlString(_pkValue)}'";
            }
            else
            {
                // Fallback — use current value as filter (fragile)
                var currentFilter = _currentValue == "NULL"
                    ? $"[{_columnName}] IS NULL"
                    : $"[{_columnName}] = '{EscapeSqlString(_currentValue)}'";
                whereClause = currentFilter;
            }

            GeneratedSql = $"UPDATE {_tableName}\r\nSET [{_columnName}] = {newValue}\r\nWHERE {whereClause};";
            _sqlPreviewBox.Text = GeneratedSql;
        }

        private void OnExecuteClick(object? sender, EventArgs e)
        {
            try
            {
                // Copy the SQL to clipboard for user to execute manually
                if (!string.IsNullOrEmpty(GeneratedSql))
                {
                    Clipboard.SetText(GeneratedSql);
                    NewValue = _setNullCheckBox.Checked ? null : _newValueBox.Text;
                    DialogResult = DialogResult.OK;
                    Close();

                    Log.Information("CellEditDialog: UPDATE SQL copied to clipboard for {Table}.{Column}",
                        _tableName, _columnName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CellEditDialog: failed to copy SQL to clipboard");
                MessageBox.Show("Failed to copy SQL to clipboard: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string EscapeSqlString(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("'", "''");
        }
    }
}
