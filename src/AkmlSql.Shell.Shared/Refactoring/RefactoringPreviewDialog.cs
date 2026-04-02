#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// WinForms preview dialog for Safe Rename and other heavyweight refactoring operations.
    /// Displays a tree of proposed changes grouped by file, a diff view for the selected change,
    /// and allows the user to approve/reject individual changes before applying.
    /// </summary>
    internal sealed class RefactoringPreviewDialog : Form
    {
        private readonly RefactorPreviewResponse _response;
        private readonly string _originalName;
        private readonly string _newName;

        private SplitContainer _splitContainer = null!;
        private TreeView _treeView = null!;
        private RichTextBox _diffView = null!;
        private Panel _bottomPanel = null!;
        private Label _warningLabel = null!;
        private Button _generateScriptButton = null!;
        private Button _cancelButton = null!;
        private Font? _boldFont;
        private Font? _italicFont;

        /// <summary>
        /// Returns only the <see cref="RefactorChangeInfo"/> items whose tree nodes are checked.
        /// </summary>
        public RefactorChangeInfo[] ApprovedChanges
        {
            get
            {
                var approved = new List<RefactorChangeInfo>();
                foreach (TreeNode rootNode in _treeView.Nodes)
                {
                    foreach (TreeNode childNode in rootNode.Nodes)
                    {
                        if (childNode.Checked && childNode.Tag is RefactorChangeInfo change)
                        {
                            approved.Add(change);
                        }
                    }
                }
                return approved.ToArray();
            }
        }

        /// <summary>
        /// Creates the refactoring preview dialog.
        /// </summary>
        /// <param name="response">The preview response from the engine containing all proposed changes.</param>
        /// <param name="originalName">The original identifier name (for display in title/diff).</param>
        /// <param name="newName">The new identifier name (for display in title/diff).</param>
        public RefactoringPreviewDialog(RefactorPreviewResponse response, string originalName, string newName)
        {
            _response = response ?? throw new ArgumentNullException(nameof(response));
            _originalName = originalName ?? string.Empty;
            _newName = newName ?? string.Empty;

            BuildLayout();
            PopulateTree();
            PopulateWarnings();
            UpdateGenerateScriptButton();
        }

        private void BuildLayout()
        {
            Text = Constants.ProductName + " - Refactoring Preview";
            Size = new Size(800, 550);
            MinimumSize = new Size(600, 400);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;
            KeyPreview = true;

            // --- Bottom panel (warnings + buttons) ---
            _bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                Padding = new Padding(8, 4, 8, 8),
            };

            _warningLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(128, 96, 0),
                BackColor = Color.FromArgb(255, 255, 225),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0),
                Visible = false,
            };

            _generateScriptButton = new Button
            {
                Text = "Generate Script",
                Dock = DockStyle.Right,
                Width = 120,
                Margin = new Padding(4),
                DialogResult = DialogResult.OK,
            };
            _generateScriptButton.Click += OnGenerateScriptClick;

            _cancelButton = new Button
            {
                Text = "Cancel",
                Dock = DockStyle.Right,
                Width = 80,
                Margin = new Padding(4),
                DialogResult = DialogResult.Cancel,
            };

            // Button panel to keep buttons right-aligned
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0),
            };
            buttonPanel.Controls.Add(_generateScriptButton);
            buttonPanel.Controls.Add(_cancelButton);

            _bottomPanel.Controls.Add(_warningLabel);
            _bottomPanel.Controls.Add(buttonPanel);

            AcceptButton = _generateScriptButton;
            CancelButton = _cancelButton;

            // --- SplitContainer (tree + diff) ---
            _splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 250,
                FixedPanel = FixedPanel.Panel1,
                BorderStyle = BorderStyle.Fixed3D,
            };

            // Left: TreeView
            _treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                Font = new Font("Segoe UI", 9f),
            };
            _treeView.AfterCheck += OnTreeAfterCheck;
            _treeView.AfterSelect += OnTreeAfterSelect;

            _splitContainer.Panel1.Controls.Add(_treeView);

            // Right: RichTextBox (diff view)
            _diffView = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10f),
                BackColor = Color.White,
                WordWrap = false,
                DetectUrls = false,
            };

            _splitContainer.Panel2.Controls.Add(_diffView);

            // Assemble
            Controls.Add(_splitContainer);
            Controls.Add(_bottomPanel);
        }

        private void PopulateTree()
        {
            _treeView.BeginUpdate();
            try
            {
                _treeView.Nodes.Clear();

                // Group changes by FilePath
                var groups = _response.Changes
                    .GroupBy(c => c.FilePath ?? string.Empty)
                    .OrderBy(g => g.Key);

                foreach (var group in groups)
                {
                    string displayName = string.IsNullOrEmpty(group.Key)
                        ? "Current Document"
                        : TruncateFilePath(group.Key);

                    var rootNode = new TreeNode(displayName)
                    {
                        Checked = true,
                        Tag = group.Key,
                    };
                    rootNode.NodeFont = new Font(_treeView.Font, FontStyle.Bold);

                    foreach (var change in group.OrderBy(c => c.Line))
                    {
                        string oldDisplay = Truncate(change.OldText, 30);
                        string newDisplay = Truncate(change.NewText, 30);
                        string label = $"Line {change.Line}: {oldDisplay} \u2192 {newDisplay}";

                        var childNode = new TreeNode(label)
                        {
                            Checked = true,
                            Tag = change,
                        };
                        rootNode.Nodes.Add(childNode);
                    }

                    _treeView.Nodes.Add(rootNode);
                }

                _treeView.ExpandAll();

                // Select the first child node if available
                if (_treeView.Nodes.Count > 0 && _treeView.Nodes[0].Nodes.Count > 0)
                {
                    _treeView.SelectedNode = _treeView.Nodes[0].Nodes[0];
                }
            }
            finally
            {
                _treeView.EndUpdate();
            }
        }

        private void PopulateWarnings()
        {
            var messages = new List<string>();

            if (_response.Warnings != null && _response.Warnings.Length > 0)
            {
                messages.AddRange(_response.Warnings.Select(w => "\u26A0 " + w));
            }

            if (_response.Errors != null && _response.Errors.Length > 0)
            {
                messages.AddRange(_response.Errors.Select(e => "\u274C " + e));
            }

            if (messages.Count > 0)
            {
                _warningLabel.Text = string.Join("  |  ", messages);
                _warningLabel.Visible = true;

                if (_response.Errors != null && _response.Errors.Length > 0)
                {
                    _warningLabel.ForeColor = Color.DarkRed;
                    _warningLabel.BackColor = Color.FromArgb(255, 230, 230);
                }
            }
        }

        private void UpdateGenerateScriptButton()
        {
            _generateScriptButton.Enabled = _response.CanApply && ApprovedChanges.Length > 0;
        }

        // --- Event handlers ---

        private void OnTreeAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (e.Action == TreeViewAction.Unknown)
                return;

            var node = e.Node;

            _treeView.AfterCheck -= OnTreeAfterCheck;
            try
            {
                // If a root node is toggled, propagate to all children
                if (node.Tag is string)
                {
                    foreach (TreeNode child in node.Nodes)
                    {
                        child.Checked = node.Checked;
                    }
                }
                else if (node.Parent != null)
                {
                    // If a child is toggled, update the parent check state.
                    // Parent is checked if any child is checked.
                    bool anyChecked = false;
                    foreach (TreeNode sibling in node.Parent.Nodes)
                    {
                        if (sibling.Checked)
                        {
                            anyChecked = true;
                            break;
                        }
                    }
                    node.Parent.Checked = anyChecked;
                }
            }
            finally
            {
                _treeView.AfterCheck += OnTreeAfterCheck;
            }

            UpdateGenerateScriptButton();
        }

        private void OnTreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is RefactorChangeInfo change)
            {
                ShowDiff(change);
            }
            else
            {
                _diffView.Clear();
            }
        }

        private void OnGenerateScriptClick(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        // --- Diff rendering ---

        private void ShowDiff(RefactorChangeInfo change)
        {
            _diffView.Clear();
            _diffView.SuspendLayout();
            try
            {
                string filePath = string.IsNullOrEmpty(change.FilePath)
                    ? "Current Document"
                    : change.FilePath;

                // Header
                AppendLine($"File: {filePath}", Color.DimGray, FontStyle.Italic);
                AppendLine($"Line {change.Line}, Column {change.Column}  [{change.ChangeCategory}]",
                    Color.DimGray, FontStyle.Italic);
                AppendLine(new string('\u2500', 60), Color.LightGray, FontStyle.Regular);
                AppendLine(string.Empty, Color.Black, FontStyle.Regular);

                // Old text (removed)
                string[] oldLines = (change.OldText ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (string line in oldLines)
                {
                    AppendLine("- " + line, Color.FromArgb(180, 0, 0), FontStyle.Regular);
                }

                // New text (added)
                string[] newLines = (change.NewText ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (string line in newLines)
                {
                    AppendLine("+ " + line, Color.FromArgb(0, 128, 0), FontStyle.Regular);
                }

                // Context snippet
                if (!string.IsNullOrWhiteSpace(change.ContextSnippet))
                {
                    AppendLine(string.Empty, Color.Black, FontStyle.Regular);
                    AppendLine("Context:", Color.DimGray, FontStyle.Bold);
                    string[] contextLines = change.ContextSnippet.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (string line in contextLines)
                    {
                        AppendLine("  " + line, Color.Gray, FontStyle.Regular);
                    }
                }

                // Scroll to top
                _diffView.SelectionStart = 0;
                _diffView.ScrollToCaret();
            }
            finally
            {
                _diffView.ResumeLayout();
            }
        }

        private void AppendLine(string text, Color color, FontStyle style)
        {
            int start = _diffView.TextLength;
            _diffView.AppendText(text + Environment.NewLine);
            int end = _diffView.TextLength;

            _diffView.Select(start, end - start);
            _diffView.SelectionColor = color;

            if (style != FontStyle.Regular)
            {
                // Cache font objects to avoid GDI handle leaks on repeated ShowDiff calls
                if (style == FontStyle.Bold)
                    _boldFont ??= new Font(_diffView.Font, FontStyle.Bold);
                else if (style == FontStyle.Italic)
                    _italicFont ??= new Font(_diffView.Font, FontStyle.Italic);
                _diffView.SelectionFont = style == FontStyle.Bold ? _boldFont : (_italicFont ?? _diffView.Font);
            }

            _diffView.SelectionLength = 0;
        }

        // --- Utility ---

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // Replace newlines for single-line display
            string singleLine = value.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            if (singleLine.Length <= maxLength)
                return singleLine;

            return singleLine.Substring(0, maxLength - 3) + "...";
        }

        private static string TruncateFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return filePath;

            // Show just the filename, or schema.object name if it looks like a DB object path
            try
            {
                string fileName = Path.GetFileName(filePath);
                if (!string.IsNullOrEmpty(fileName))
                    return fileName;
            }
            catch
            {
                // Not a valid path — return as-is (could be a DB object reference like "dbo.GetOrders")
            }

            return filePath;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _treeView?.Dispose();
                _diffView?.Dispose();
                _splitContainer?.Dispose();
                _warningLabel?.Dispose();
                _generateScriptButton?.Dispose();
                _cancelButton?.Dispose();
                _boldFont?.Dispose();
                _italicFont?.Dispose();
                _bottomPanel?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
