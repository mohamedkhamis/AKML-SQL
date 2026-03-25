#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T022: WPF-style find bar overlaid on the SSMS results grid. Implemented as a WinForms
    /// UserControl (since the grid itself is WinForms) with a search TextBox, regex toggle,
    /// match count label, and Next/Previous navigation buttons.
    /// F3 = Next, Shift+F3 = Previous.
    /// </summary>
    internal sealed class GridFindBar : UserControl
    {
        private readonly TextBox _searchBox;
        private readonly CheckBox _regexToggle;
        private readonly Label _matchCountLabel;
        private readonly Button _nextButton;
        private readonly Button _prevButton;
        private readonly Button _closeButton;

        private DataGridView? _targetGrid;
        private readonly List<(int Row, int Col)> _matches = new List<(int, int)>();
        private int _currentMatchIndex = -1;

        private static readonly Color HighlightColor = Color.FromArgb(255, 255, 150);
        private static readonly Color CurrentMatchColor = Color.FromArgb(255, 165, 0);
        private static readonly Color DefaultCellColor = Color.White;

        public GridFindBar()
        {
            Height = 32;
            Dock = DockStyle.Top;
            BackColor = SystemColors.Control;
            Padding = new Padding(4, 2, 4, 2);

            // Search icon label
            var searchIcon = new Label
            {
                Text = "Find:",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Left,
                Padding = new Padding(2, 4, 4, 0)
            };

            _searchBox = new TextBox
            {
                Width = 200,
                Dock = DockStyle.Left,
                Font = new Font("Segoe UI", 9f)
            };
            _searchBox.TextChanged += OnSearchTextChanged;
            _searchBox.KeyDown += OnSearchBoxKeyDown;

            _regexToggle = new CheckBox
            {
                Text = "Regex",
                AutoSize = true,
                Dock = DockStyle.Left,
                Padding = new Padding(4, 4, 4, 0),
                CheckAlign = ContentAlignment.MiddleLeft
            };
            _regexToggle.CheckedChanged += OnRegexToggleChanged;

            _matchCountLabel = new Label
            {
                Text = "0 matches",
                AutoSize = true,
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 4, 4, 0),
                ForeColor = SystemColors.GrayText
            };

            _prevButton = new Button
            {
                Text = "\u25B2",
                Width = 28,
                Height = 24,
                Dock = DockStyle.Left,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(0),
                Margin = new Padding(2)
            };
            _prevButton.FlatAppearance.BorderSize = 1;
            _prevButton.Click += (_, __) => NavigatePrevious();

            _nextButton = new Button
            {
                Text = "\u25BC",
                Width = 28,
                Height = 24,
                Dock = DockStyle.Left,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(0),
                Margin = new Padding(2)
            };
            _nextButton.FlatAppearance.BorderSize = 1;
            _nextButton.Click += (_, __) => NavigateNext();

            _closeButton = new Button
            {
                Text = "\u2715",
                Width = 28,
                Height = 24,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(0),
                Margin = new Padding(2)
            };
            _closeButton.FlatAppearance.BorderSize = 0;
            _closeButton.Click += (_, __) => HideFindBar();

            // Add in reverse dock order for Left docking
            Controls.Add(_closeButton);
            Controls.Add(_matchCountLabel);
            Controls.Add(_nextButton);
            Controls.Add(_prevButton);
            Controls.Add(_regexToggle);
            Controls.Add(_searchBox);
            Controls.Add(searchIcon);

            Visible = false;
        }

        /// <summary>
        /// Attaches the find bar to a specific DataGridView and makes it visible.
        /// </summary>
        public void Show(DataGridView grid)
        {
            _targetGrid = grid;

            // Insert above the grid in its parent
            var parent = grid.Parent;
            if (parent != null && !parent.Controls.Contains(this))
            {
                parent.Controls.Add(this);
                BringToFront();
            }

            Visible = true;
            _searchBox.Focus();
            _searchBox.SelectAll();

            Log.Debug("GridFindBar: shown for grid with {Rows} rows, {Cols} columns",
                grid.RowCount, grid.ColumnCount);
        }

        /// <summary>
        /// Hides the find bar and clears all highlights.
        /// </summary>
        public void HideFindBar()
        {
            ClearHighlights();
            _matches.Clear();
            _currentMatchIndex = -1;
            _matchCountLabel.Text = "0 matches";
            Visible = false;

            // Return focus to the grid
            _targetGrid?.Focus();
        }

        /// <summary>
        /// Toggles visibility of the find bar.
        /// </summary>
        public void Toggle(DataGridView? grid = null)
        {
            if (Visible)
            {
                HideFindBar();
            }
            else
            {
                grid ??= GridAccessHelper.GetActiveResultsGrid();
                if (grid != null)
                {
                    Show(grid);
                }
            }
        }

        /// <summary>
        /// Navigate to the next match. Wraps around.
        /// </summary>
        public void NavigateNext()
        {
            if (_matches.Count == 0) return;

            _currentMatchIndex = (_currentMatchIndex + 1) % _matches.Count;
            HighlightCurrentMatch();
            UpdateMatchCountLabel();
        }

        /// <summary>
        /// Navigate to the previous match. Wraps around.
        /// </summary>
        public void NavigatePrevious()
        {
            if (_matches.Count == 0) return;

            _currentMatchIndex = (_currentMatchIndex - 1 + _matches.Count) % _matches.Count;
            HighlightCurrentMatch();
            UpdateMatchCountLabel();
        }

        #region Event handlers

        private void OnSearchTextChanged(object? sender, EventArgs e)
        {
            PerformSearch();
        }

        private void OnRegexToggleChanged(object? sender, EventArgs e)
        {
            PerformSearch();
        }

        private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.F3 when e.Shift:
                    NavigatePrevious();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;

                case Keys.F3:
                    NavigateNext();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;

                case Keys.Enter:
                    NavigateNext();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;

                case Keys.Escape:
                    HideFindBar();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
            }
        }

        #endregion

        #region Search logic

        private void PerformSearch()
        {
            ClearHighlights();
            _matches.Clear();
            _currentMatchIndex = -1;

            var searchText = _searchBox.Text;
            if (string.IsNullOrEmpty(searchText) || _targetGrid == null)
            {
                _matchCountLabel.Text = "0 matches";
                _matchCountLabel.ForeColor = SystemColors.GrayText;
                return;
            }

            Regex? regex = null;
            if (_regexToggle.Checked)
            {
                try
                {
                    regex = new Regex(searchText, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                catch (ArgumentException)
                {
                    _matchCountLabel.Text = "Invalid regex";
                    _matchCountLabel.ForeColor = Color.Red;
                    return;
                }
            }

            try
            {
                _targetGrid.SuspendLayout();

                for (int row = 0; row < _targetGrid.RowCount; row++)
                {
                    for (int col = 0; col < _targetGrid.ColumnCount; col++)
                    {
                        var cellValue = _targetGrid.Rows[row].Cells[col].Value;
                        if (cellValue == null || cellValue is DBNull) continue;

                        var cellText = cellValue.ToString() ?? string.Empty;
                        bool isMatch;

                        if (regex != null)
                        {
                            isMatch = regex.IsMatch(cellText);
                        }
                        else
                        {
                            isMatch = cellText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                        }

                        if (isMatch)
                        {
                            _matches.Add((row, col));
                            _targetGrid.Rows[row].Cells[col].Style.BackColor = HighlightColor;
                        }
                    }
                }

                if (_matches.Count > 0)
                {
                    _currentMatchIndex = 0;
                    HighlightCurrentMatch();
                }

                UpdateMatchCountLabel();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridFindBar: search failed");
                _matchCountLabel.Text = "Search error";
                _matchCountLabel.ForeColor = Color.Red;
            }
            finally
            {
                _targetGrid.ResumeLayout();
            }
        }

        private void HighlightCurrentMatch()
        {
            if (_targetGrid == null || _currentMatchIndex < 0 || _currentMatchIndex >= _matches.Count)
                return;

            try
            {
                // Reset all match highlights to the standard highlight color
                foreach (var (row, col) in _matches)
                {
                    _targetGrid.Rows[row].Cells[col].Style.BackColor = HighlightColor;
                }

                // Highlight the current match with a distinct color
                var current = _matches[_currentMatchIndex];
                _targetGrid.Rows[current.Row].Cells[current.Col].Style.BackColor = CurrentMatchColor;

                // Scroll to the current match
                _targetGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, current.Row - 2);
                _targetGrid.CurrentCell = _targetGrid.Rows[current.Row].Cells[current.Col];
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridFindBar: failed to highlight current match");
            }
        }

        private void ClearHighlights()
        {
            if (_targetGrid == null) return;

            try
            {
                foreach (var (row, col) in _matches)
                {
                    if (row < _targetGrid.RowCount && col < _targetGrid.ColumnCount)
                    {
                        _targetGrid.Rows[row].Cells[col].Style.BackColor = DefaultCellColor;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridFindBar: failed to clear highlights");
            }
        }

        private void UpdateMatchCountLabel()
        {
            if (_matches.Count == 0)
            {
                _matchCountLabel.Text = string.IsNullOrEmpty(_searchBox.Text)
                    ? "0 matches"
                    : "No matches";
                _matchCountLabel.ForeColor = string.IsNullOrEmpty(_searchBox.Text)
                    ? SystemColors.GrayText
                    : Color.Red;
            }
            else
            {
                _matchCountLabel.Text = $"{_currentMatchIndex + 1} of {_matches.Count}";
                _matchCountLabel.ForeColor = SystemColors.ControlText;
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearHighlights();
                _matches.Clear();
            }

            base.Dispose(disposing);
        }
    }
}
