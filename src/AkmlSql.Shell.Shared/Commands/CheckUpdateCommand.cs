#nullable enable
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using AkmlSql.Core.Config;
using AkmlSql.Core.Update;
using AkmlSql.Shell.Shared.Update;
using Constants = AkmlSql.Core.Constants;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class CheckUpdateCommand
    {
        private CheckUpdateCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdCheckUpdate);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static CheckUpdateCommand Instance { get; private set; } = null!;

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new CheckUpdateCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                // FR-042: the manual check calls LaunchUpdaterAndWait directly — never through
                // the 24-hour throttle in LaunchIfDue — and reports all three outcomes.
                var outcome = UpdateCheckFlow.RunManualCheck(
                    UpdateLauncher.LaunchUpdaterAndWait,
                    () => ConfigManager.Load().LastUpdateCheck,
                    () => UpdateNotifier.CheckForPendingUpdate(),
                    TimeSpan.FromSeconds(15));

                switch (outcome)
                {
                    case UpdateCheckFlow.ManualCheckOutcome.CheckFailed:
                        MessageBox.Show(
                            "Unable to check for updates. Please try again later.",
                            Constants.ProductName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;

                    case UpdateCheckFlow.ManualCheckOutcome.UpToDate:
                        MessageBox.Show(
                            $"{Constants.ProductName} v{Constants.RuntimeVersion} is up to date.",
                            Constants.ProductName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;

                    case UpdateCheckFlow.ManualCheckOutcome.UpdateAvailable:
                        var result = UpdateNotifier.CheckForPendingUpdate();
                        if (result is { Available: true })
                        {
                            BeginGuidedUpdate(result);
                        }
                        return;
                }
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

        /// <summary>
        /// The guided flow (spec 036 US5 / FR-039): the updater downloads and verifies the
        /// installer out of process with visible progress and a working cancel, a confirmation
        /// names the applications that must close, and only then is the verified installer
        /// launched with its normal UI — never /VERYSILENT.
        /// </summary>
        private static void BeginGuidedUpdate(UpdateResult result)
        {
            var offer = UpdateAvailableDialog.CreateFor(result);
            if (offer.ShowDialog() != true)
            {
                return; // "Not now" — the offer stays on disk
            }

            var process = UpdateLauncher.LaunchUpdaterDownload();
            if (process == null)
            {
                MessageBox.Show(
                    "Unable to launch the updater. The updater was not found.",
                    Constants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var progress = UpdateDownloadProgressWindow.CreateFor(result.Version, process);
            var updaterExited = progress.ShowDialog() == true;
            if (!updaterExited)
            {
                // Cancelled: the window killed the updater and cleaned up (no .partial, offer
                // retained as "available"). Nothing else to do.
                Log.Information("Update download cancelled by the user");
                return;
            }

            var updated = UpdateNotifier.CheckForPendingUpdate();
            if (updated is { Available: true }
                && updated.DownloadState == UpdateDownloadStates.Verified
                && !string.IsNullOrEmpty(updated.VerifiedInstallerPath))
            {
                ConfirmAndLaunch(updated);
            }
            else if (updated is { Available: true }
                && updated.DownloadState == UpdateDownloadStates.Failed)
            {
                // FR-040: an explicit message; the unverified file was already deleted.
                MessageBox.Show(
                    $"The update could not be downloaded.\n\n{updated.FailureReason}",
                    Constants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static void ConfirmAndLaunch(UpdateResult result)
        {
            // FR-039: one confirmation naming the applications that must close. Declining
            // installs nothing, closes nothing and retains the offer (spec scenario 4a).
            var confirm = UpdateInstallConfirmDialog.CreateForUpdate(result.Version);
            if (confirm.ShowDialog() != true)
            {
                return;
            }

            try
            {
                // Canonicalised (data-model V18); normal installer UI — /VERYSILENT stays
                // reserved for the documented unattended-deployment path in doc/deployment.md.
                var path = Path.GetFullPath(result.VerifiedInstallerPath!);
                if (!File.Exists(path))
                {
                    MessageBox.Show(
                        "The downloaded installer is no longer on disk. Please try the update again.",
                        Constants.ProductName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                using (Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                })) { }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch the update installer");
                MessageBox.Show(
                    "Unable to launch the installer. Please try again later.",
                    Constants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}
