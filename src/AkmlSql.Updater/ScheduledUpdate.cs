using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Update;
using Serilog;

namespace AkmlSql.Updater
{
    /// <summary>
    /// <c>AkmlSql.Updater.exe --scheduled</c>: what the "AKML SQL\Update Check" scheduled task runs,
    /// daily and at sign-in, as the signed-in user.
    /// <list type="number">
    ///   <item><description>Honours the user's "Check for updates automatically" setting.</description></item>
    ///   <item><description>Checks, unless it (or an IDE) already checked within
    ///   <see cref="Constants.ScheduledCheckMinimumHours"/> hours.</description></item>
    ///   <item><description>Downloads and verifies an offered update.</description></item>
    ///   <item><description>Tells the user once per version, with a Windows notification.</description></item>
    /// </list>
    /// It never installs anything: installing needs the user's click and Windows' admin prompt.
    /// Every failure is quiet — a background task has no one to show an error to, and the next
    /// run simply tries again.
    /// <para>
    /// <c>--check-now</c> (the Start-menu "Check for AKML SQL updates" shortcut) runs the same
    /// steps as an explicit request: it always checks, even with automatic updates off, and it
    /// always answers — ready, up to date, or could not check.
    /// </para>
    /// </summary>
    internal sealed class ScheduledUpdate
    {
        private readonly string _configPath;
        private readonly string _resultPath;
        private readonly string _currentVersion;
        private readonly Func<Task> _check;
        private readonly Func<CancellationToken, Task<int>> _download;
        private readonly IUpdateNotifications _notifications;
        private readonly Func<DateTimeOffset> _now;

        public ScheduledUpdate(
            string configPath,
            string resultPath,
            string currentVersion,
            Func<Task> check,
            Func<CancellationToken, Task<int>> download,
            IUpdateNotifications notifications,
            Func<DateTimeOffset>? now = null)
        {
            _configPath = configPath;
            _resultPath = resultPath;
            _currentVersion = currentVersion;
            _check = check;
            _download = download;
            _notifications = notifications;
            _now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <param name="interactive">True for <c>--check-now</c>: the user asked, so always check and answer.</param>
        public async Task<int> RunAsync(bool interactive, CancellationToken cancellationToken)
        {
            var preferences = UpdaterPreferences.Read(_configPath);
            if (!interactive && !preferences.AutoUpdateEnabled)
            {
                Log.Information("Scheduled update check skipped: automatic updates are turned off");
                return 0;
            }

            var checkFailed = false;
            var sinceLastCheck = preferences.LastUpdateCheck is { } last ? _now() - last : TimeSpan.MaxValue;
            if (interactive || sinceLastCheck >= TimeSpan.FromHours(Constants.ScheduledCheckMinimumHours))
            {
                try
                {
                    await _check().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    checkFailed = true;
                    Log.Information("Update check could not reach the server: {Reason}", ex.Message);
                }
            }
            else
            {
                Log.Debug("Checked {Hours:F1}h ago; not checking again", sinceLastCheck.TotalHours);
            }

            var result = UpdateResultStore.Load(_resultPath);

            // An offer for the version already installed (the update was just applied) is spent.
            if (result is { Available: true } && !VersionComparer.IsNewer(result.Version, _currentVersion))
            {
                Log.Information("v{Version} is already installed; clearing the offer", result.Version);
                TryDelete(_resultPath);
                result = null;
            }

            if (result is not { Available: true })
            {
                if (interactive)
                {
                    if (checkFailed)
                    {
                        _notifications.CouldNotCheck();
                    }
                    else
                    {
                        _notifications.UpToDate(_currentVersion);
                    }
                }

                return 0;
            }

            if (!IsReady(result))
            {
                await _download(cancellationToken).ConfigureAwait(false);
                result = UpdateResultStore.Load(_resultPath);
            }

            if (result is { Available: true } && IsReady(result) && (interactive || result.NotifiedAt is null))
            {
                if (_notifications.Ready(result.Version))
                {
                    result.NotifiedAt = _now();
                    UpdateResultStore.SaveAtomic(result, _resultPath);
                }
            }
            else if (interactive && result is { DownloadState: UpdateDownloadStates.Failed })
            {
                _notifications.CouldNotDownload(result.Version);
            }

            return 0;
        }

        private static bool IsReady(UpdateResult result) =>
            result.DownloadState == UpdateDownloadStates.Verified
            && !string.IsNullOrEmpty(result.VerifiedInstallerPath)
            && File.Exists(result.VerifiedInstallerPath);

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not remove {Path}", path);
            }
        }
    }

    /// <summary>What the updater can tell the user. Windows notifications in production.</summary>
    internal interface IUpdateNotifications
    {
        /// <summary>A verified update is ready. Returns false when Windows refused to show it.</summary>
        bool Ready(string version);

        void UpToDate(string currentVersion);

        void CouldNotCheck();

        void CouldNotDownload(string version);
    }
}
