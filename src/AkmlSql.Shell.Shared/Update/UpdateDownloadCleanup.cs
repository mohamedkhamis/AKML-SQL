#nullable enable
using System;
using System.IO;
using AkmlSql.Core.Update;
using Constants = AkmlSql.Core.Constants;
using Serilog;

namespace AkmlSql.Shell.Shared.Update
{
    /// <summary>
    /// Shell-side cleanup after a cancelled update download (spec 036 US5 / FR-039a). The
    /// progress window cancels by killing the updater process, and a killed process never runs
    /// its finally blocks — so the shell deletes the <c>.partial</c> itself and rolls the
    /// persisted state back to "available" (state machine: downloading → available, offer
    /// retained).
    /// </summary>
    internal static class UpdateDownloadCleanup
    {
        internal static void AfterCancel(string version)
        {
            AfterCancel(version, Constants.CachePath, Constants.UpdateResultFilePath);
        }

        /// <summary>Path-injected core, directly testable without AppData redirection.</summary>
        internal static void AfterCancel(string version, string cacheDirectory, string resultFilePath)
        {
            try
            {
                var partial = Path.Combine(cacheDirectory, $"AKMLSQLSetup-{version}.exe.partial");
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }

                var result = UpdateResultStore.Load(resultFilePath);
                if (result is { Available: true }
                    && result.DownloadState == UpdateDownloadStates.Downloading)
                {
                    result.DownloadState = UpdateDownloadStates.None;
                    result.FailureReason = null;
                    result.VerifiedInstallerPath = null;
                    UpdateResultStore.SaveAtomic(result, resultFilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to clean up after a cancelled update download");
            }
        }
    }
}
