using System;
using System.ComponentModel.Design;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Constants = AkmlSql.Core.Constants;
using AkmlSql.Core.Update;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class CheckUpdateCommand
    {
        private readonly Package _package;

        private CheckUpdateCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdCheckUpdate);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static CheckUpdateCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new CheckUpdateCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                // Launch updater and wait for it to finish (with timeout)
                var process = Update.UpdateLauncher.LaunchUpdaterAndWait(TimeSpan.FromSeconds(15));
                if (process == null)
                {
                    MessageBox.Show(
                        "Unable to launch update checker. The updater was not found.",
                        Constants.ProductName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var resultPath = Constants.UpdateResultFilePath;
                if (File.Exists(resultPath))
                {
                    var json = File.ReadAllText(resultPath);
                    var result = JsonSerializer.Deserialize<UpdateResult>(json, JsonOptions.CamelCase);

                    if (result != null && result.Available && IsValidHttpsUrl(result.DownloadUrl))
                    {
                        var dialogResult = MessageBox.Show(
                            $"A new version ({result.Version}) is available.\n\nWould you like to download it?",
                            Constants.ProductName,
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);

                        if (dialogResult == DialogResult.Yes)
                        {
                            using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = result.DownloadUrl,
                                UseShellExecute = true
                            })) { }
                        }
                        return;
                    }
                }

                MessageBox.Show(
                    $"{Constants.ProductName} v{Constants.Version} is up to date.",
                    Constants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check for updates");
                MessageBox.Show(
                    "Unable to check for updates. Please try again later.",
                    Constants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static bool IsValidHttpsUrl(string url)
        {
            return !string.IsNullOrEmpty(url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps;
        }
    }
}
