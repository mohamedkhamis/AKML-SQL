using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui;
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
            Text = WindowTitles.For("About");
            WindowIcon.Apply(this);
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
                Text = $"Version {Constants.RuntimeVersion}",
                Location = new Point(20, 55),
                AutoSize = true
            };

            var buildLabel = new Label
            {
                Text = $"Build date: {AkmlSql.Core.AppVersion.BuildDateTime}",
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
                    $"{Constants.ProductName} v{Constants.RuntimeVersion}\n" +
                    $"Build: {AkmlSql.Core.AppVersion.BuildDateTime} ({BuildUtc()})\n" +
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

            Controls.AddRange([
                titleLabel, versionLabel, buildLabel, runtimeLabel,
                osLabel, archLabel, licenseLabel, copyDiagButton, okButton
            ]);
        }

        /// <summary>The build instant in UTC for the copied diagnostics (support reads them in any time zone).</summary>
        private static string BuildUtc() =>
            AkmlSql.Core.AppVersion.BuildTimestampUtc is System.DateTime utc
                ? utc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC"
                : "no build stamp";
    }
}
