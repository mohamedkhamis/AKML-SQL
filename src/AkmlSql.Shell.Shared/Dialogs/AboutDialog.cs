using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Dialogs
{
    internal class AboutDialog : Form
    {
        public AboutDialog()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "About " + Constants.ProductName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(420, 320);
            ShowInTaskbar = false;

            var titleLabel = new Label
            {
                Text = Constants.ProductName,
                Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
                Location = new Point(20, 20),
                AutoSize = true
            };

            var versionLabel = new Label
            {
                Text = $"Version {Constants.Version}",
                Location = new Point(20, 55),
                AutoSize = true
            };

            var buildLabel = new Label
            {
                Text = $"Build Date: {Constants.BuildDate}",
                Location = new Point(20, 80),
                AutoSize = true
            };

            var runtimeLabel = new Label
            {
                Text = $"Runtime: {RuntimeInformation.FrameworkDescription}",
                Location = new Point(20, 105),
                AutoSize = true
            };

            var osLabel = new Label
            {
                Text = $"OS: {RuntimeInformation.OSDescription}",
                Location = new Point(20, 130),
                AutoSize = true
            };

            var archLabel = new Label
            {
                Text = $"Architecture: {RuntimeInformation.ProcessArchitecture}",
                Location = new Point(20, 155),
                AutoSize = true
            };

            var licenseLabel = new Label
            {
                Text = "License: MIT (Open Source)",
                Location = new Point(20, 180),
                AutoSize = true
            };

            var copyDiagButton = new Button
            {
                Text = "Copy Diagnostics",
                Location = new Point(20, 220),
                Size = new Size(130, 30)
            };
            copyDiagButton.Click += (_, _2) =>
            {
                var diagnostics =
                    $"{Constants.ProductName} v{Constants.Version}\n" +
                    $"Build: {Constants.BuildDate}\n" +
                    $"Runtime: {RuntimeInformation.FrameworkDescription}\n" +
                    $"OS: {RuntimeInformation.OSDescription}\n" +
                    $"Arch: {RuntimeInformation.ProcessArchitecture}\n" +
                    $"Logs: {Constants.LogsPath}";
                Clipboard.SetText(diagnostics);
                MessageBox.Show("Diagnostics copied to clipboard.", Constants.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var okButton = new Button
            {
                Text = "OK",
                Location = new Point(310, 220),
                Size = new Size(80, 30),
                DialogResult = DialogResult.OK
            };

            AcceptButton = okButton;

            Controls.AddRange(new Control[]
            {
                titleLabel, versionLabel, buildLabel, runtimeLabel,
                osLabel, archLabel, licenseLabel, copyDiagButton, okButton
            });
        }
    }
}
