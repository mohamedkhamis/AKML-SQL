#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Update;
using Microsoft.VisualStudio.Threading;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Update
{
    /// <summary>
    /// Offers a downloaded update once, when SSMS or Visual Studio starts.
    /// <para>
    /// The scheduled task (and the IDE's own 24-hour check) download and verify updates in the
    /// background, but until now nothing ever read the result: the user only learned about an
    /// update by clicking Check for Updates. So a verified update is offered here, once per
    /// version — "Later" keeps it one click away in Check for Updates rather than asking again at
    /// every start. The offer waits until the IDE has finished loading so it never sits on top of
    /// a splash screen or a half-drawn window.
    /// </para>
    /// </summary>
    internal static class UpdateStartupPrompt
    {
        internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);

        /// <summary>Schedules the offer when one is due; returns immediately.</summary>
        public static void ScheduleIfReady(JoinableTaskFactory joinableTaskFactory, CancellationToken disposalToken)
        {
            try
            {
                var result = UpdateNotifier.CheckForPendingUpdate();
                if (!ShouldPrompt(result, File.Exists))
                {
                    return;
                }

                _ = joinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await Task.Delay(Delay, disposalToken);
                        await joinableTaskFactory.SwitchToMainThreadAsync(disposalToken);
                        Offer();
                    }
                    catch (OperationCanceledException)
                    {
                        // The IDE is closing; the offer stands for the next start.
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Update startup offer failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not schedule the update startup offer");
            }
        }

        /// <summary>
        /// True for a verified download of a NEWER version whose file is still there and has not
        /// been offered. (Right after an update installs, the offer file still describes the
        /// version now running — offering to install it again would be nonsense.)
        /// </summary>
        internal static bool ShouldPrompt(UpdateResult? result, Func<string, bool> fileExists) =>
            ShouldPrompt(result, fileExists, Constants.RuntimeVersion);

        internal static bool ShouldPrompt(UpdateResult? result, Func<string, bool> fileExists, string currentVersion) =>
            result is { Available: true }
            && VersionComparer.IsNewer(result.Version, currentVersion)
            && result.DownloadState == UpdateDownloadStates.Verified
            && !string.IsNullOrEmpty(result.VerifiedInstallerPath)
            && fileExists(result.VerifiedInstallerPath!)
            && result.ShellPromptedAt is null;

        private static void Offer()
        {
            // Re-read: another IDE, or the notification, may have handled it in the meantime.
            var result = UpdateResultStore.Load(Constants.UpdateResultFilePath);
            if (!ShouldPrompt(result, File.Exists))
            {
                return;
            }

            // Recorded BEFORE showing, so SSMS and Visual Studio started together offer it once.
            result!.ShellPromptedAt = DateTimeOffset.UtcNow;
            UpdateResultStore.SaveAtomic(result, Constants.UpdateResultFilePath);

            var dialog = UpdateInstallConfirmDialog.CreateForUpdate(result.Version, declineText: "Later");
            if (dialog.ShowDialog() == true)
            {
                Log.Information("Install of v{Version} accepted from the startup offer", result.Version);
                UpdateLauncher.LaunchInstall();
            }
            else
            {
                Log.Information("Install of v{Version} postponed from the startup offer", result.Version);
            }
        }
    }
}
