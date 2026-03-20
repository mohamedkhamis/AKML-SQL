using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// T107: Programmatic WinForms dialog for configuring bulk format operations.
    /// Allows the user to select SQL files/folders, choose a profile, set mode and backup option.
    /// </summary>
    internal sealed class BulkFormatWizard : Form
    {
        private ListBox _fileListBox;
        private Button _addFilesButton;
        private Button _addFolderButton;
        private Button _removeButton;
        private ComboBox _profileCombo;
        private CheckBox _createBackupsCheck;
        private CheckBox _previewOnlyCheck;
        private CheckBox _skipParseErrorsCheck;
        private CheckBox _respectNoformatCheck;
        private NumericUpDown _parallelismNumeric;
        private Button _startButton;
        private Button _cancelButton;

        /// <summary>
        /// The selected file paths after the dialog is closed with OK.
        /// </summary>
        public List<string> SelectedFiles { get; } = new List<string>();

        /// <summary>
        /// The selected profile name.
        /// </summary>
        public string SelectedProfile { get; private set; } = "Default";

        /// <summary>
        /// Whether to create .bak backups before formatting.
        /// </summary>
        public bool CreateBackups { get; private set; } = true;

        /// <summary>
        /// Whether to preview only (dry run) without writing changes.
        /// </summary>
        public bool PreviewOnly { get; private set; }

        /// <summary>
        /// Whether to skip files with parse errors.
        /// </summary>
        public bool SkipParseErrors { get; private set; } = true;

        /// <summary>
        /// Whether to respect noformat markers.
        /// </summary>
        public bool RespectNoformat { get; private set; } = true;

        /// <summary>
        /// Max degree of parallelism.
        /// </summary>
        public int MaxParallelism { get; private set; } = Environment.ProcessorCount;

        public BulkFormatWizard(string[] availableProfiles)
        {
            InitializeComponents(availableProfiles ?? new[] { "Default" });
        }

        private void InitializeComponents(string[] availableProfiles)
        {
            Text = Constants.ProductName + " - Bulk Format";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(600, 520);
            ShowInTaskbar = false;

            // ---- File list section ----
            var filesLabel = new Label
            {
                Text = "SQL Files to Format:",
                Location = new Point(12, 12),
                AutoSize = true
            };

            _fileListBox = new ListBox
            {
                Location = new Point(12, 32),
                Size = new Size(440, 180),
                SelectionMode = SelectionMode.MultiExtended,
                HorizontalScrollbar = true
            };

            _addFilesButton = new Button
            {
                Text = "Add Files...",
                Location = new Point(460, 32),
                Size = new Size(110, 28)
            };
            _addFilesButton.Click += OnAddFiles;

            _addFolderButton = new Button
            {
                Text = "Add Folder...",
                Location = new Point(460, 66),
                Size = new Size(110, 28)
            };
            _addFolderButton.Click += OnAddFolder;

            _removeButton = new Button
            {
                Text = "Remove",
                Location = new Point(460, 100),
                Size = new Size(110, 28)
            };
            _removeButton.Click += OnRemove;

            // ---- Options section ----
            var profileLabel = new Label
            {
                Text = "Profile:",
                Location = new Point(12, 224),
                AutoSize = true
            };

            _profileCombo = new ComboBox
            {
                Location = new Point(110, 221),
                Size = new Size(200, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _profileCombo.Items.AddRange(availableProfiles);
            if (_profileCombo.Items.Count > 0)
                _profileCombo.SelectedIndex = 0;

            _createBackupsCheck = new CheckBox
            {
                Text = "Create .bak backups before formatting",
                Location = new Point(12, 256),
                AutoSize = true,
                Checked = true
            };

            _previewOnlyCheck = new CheckBox
            {
                Text = "Preview only (dry run, no files changed)",
                Location = new Point(12, 282),
                AutoSize = true,
                Checked = false
            };

            _skipParseErrorsCheck = new CheckBox
            {
                Text = "Skip files with parse errors",
                Location = new Point(12, 308),
                AutoSize = true,
                Checked = true
            };

            _respectNoformatCheck = new CheckBox
            {
                Text = "Respect --noformat markers",
                Location = new Point(12, 334),
                AutoSize = true,
                Checked = true
            };

            var parallelLabel = new Label
            {
                Text = "Parallelism:",
                Location = new Point(12, 366),
                AutoSize = true
            };

            _parallelismNumeric = new NumericUpDown
            {
                Location = new Point(110, 363),
                Size = new Size(60, 24),
                Minimum = 1,
                Maximum = 64,
                Value = Math.Min(Environment.ProcessorCount, 64)
            };

            // ---- Action buttons ----
            _startButton = new Button
            {
                Text = "Start",
                Location = new Point(380, 440),
                Size = new Size(90, 30),
                DialogResult = DialogResult.OK
            };
            _startButton.Click += OnStart;

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(480, 440),
                Size = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = _startButton;
            CancelButton = _cancelButton;

            Controls.AddRange(new Control[]
            {
                filesLabel, _fileListBox, _addFilesButton, _addFolderButton, _removeButton,
                profileLabel, _profileCombo,
                _createBackupsCheck, _previewOnlyCheck, _skipParseErrorsCheck, _respectNoformatCheck,
                parallelLabel, _parallelismNumeric,
                _startButton, _cancelButton
            });
        }

        private void OnAddFiles(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select SQL Files";
                ofd.Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*";
                ofd.Multiselect = true;
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (var file in ofd.FileNames)
                    {
                        if (!_fileListBox.Items.Contains(file))
                            _fileListBox.Items.Add(file);
                    }
                }
            }
        }

        private void OnAddFolder(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select folder containing SQL files";
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    var files = Directory.GetFiles(fbd.SelectedPath, "*.sql", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (!_fileListBox.Items.Contains(file))
                            _fileListBox.Items.Add(file);
                    }
                }
            }
        }

        private void OnRemove(object sender, EventArgs e)
        {
            var selected = _fileListBox.SelectedItems.Cast<string>().ToList();
            foreach (var item in selected)
                _fileListBox.Items.Remove(item);
        }

        private void OnStart(object sender, EventArgs e)
        {
            SelectedFiles.Clear();
            foreach (var item in _fileListBox.Items)
                SelectedFiles.Add(item.ToString());

            SelectedProfile = _profileCombo.SelectedItem?.ToString() ?? "Default";
            CreateBackups = _createBackupsCheck.Checked;
            PreviewOnly = _previewOnlyCheck.Checked;
            SkipParseErrors = _skipParseErrorsCheck.Checked;
            RespectNoformat = _respectNoformatCheck.Checked;
            MaxParallelism = (int)_parallelismNumeric.Value;

            if (SelectedFiles.Count == 0)
            {
                MessageBox.Show("Please add at least one SQL file.", Constants.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        }
    }
}
