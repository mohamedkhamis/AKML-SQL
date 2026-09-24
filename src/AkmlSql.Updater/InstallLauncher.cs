using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using AkmlSql.Core;
using AkmlSql.Core.Update;
using Serilog;

namespace AkmlSql.Updater
{
    /// <summary>
    /// <c>AkmlSql.Updater.exe --install [akmlsql-update:install|details]</c> — what the update
    /// notification's buttons run.
    /// <para>
    /// The URL scheme is registered machine-wide, so anything — a web page included — can invoke
    /// it. It therefore carries no data: the only installer ever launched is the one a previous
    /// run downloaded into this user's own update cache and verified against the manifest hash,
    /// and it is verified again right before launch. It runs with its normal UI, so Windows' admin
    /// prompt and the installer's own "close SSMS" step still stand between a click
    /// and any change.
    /// </para>
    /// </summary>
    internal sealed class InstallLauncher
    {
        private readonly string _resultFilePath;
        private readonly string _cacheDirectory;
        private readonly string _currentVersion;
        private readonly Func<ProcessStartInfo, bool> _start;

        public InstallLauncher(string resultFilePath, string cacheDirectory, string currentVersion,
            Func<ProcessStartInfo, bool>? start = null)
        {
            _resultFilePath = resultFilePath;
            _cacheDirectory = cacheDirectory;
            _currentVersion = currentVersion;
            _start = start ?? StartDefault;
        }

        /// <summary>The action named by a URL (or bare word) argument: install unless it says details.</summary>
        public static string ActionFrom(string? argument)
        {
            var text = (argument ?? string.Empty).Trim().TrimEnd('/');
            var colon = text.LastIndexOf(':');
            var action = (colon >= 0 ? text[(colon + 1)..] : text).Trim('/').ToLowerInvariant();
            return action == UpdateToast.DetailsArgument ? UpdateToast.DetailsArgument : UpdateToast.InstallArgument;
        }

        public int Run(string action)
        {
            var result = UpdateResultStore.Load(_resultFilePath);

            if (action == UpdateToast.DetailsArgument)
            {
                OpenUrl(IsHttps(result?.ReleaseNotesUrl) ? result!.ReleaseNotesUrl : Constants.DownloadPageUrl);
                return 0;
            }

            if (result is not { Available: true }
                || !VersionComparer.IsNewer(result.Version, _currentVersion)
                || result.DownloadState != UpdateDownloadStates.Verified
                || string.IsNullOrEmpty(result.VerifiedInstallerPath))
            {
                // Nothing ready (already installed, or never downloaded): the download page is the
                // honest next step, and it is where a notification clicked days later should lead.
                Log.Information("Install requested with no verified update on disk; opening the download page");
                OpenUrl(Constants.DownloadPageUrl);
                return 0;
            }

            var path = Path.GetFullPath(result.VerifiedInstallerPath);
            if (!IsInside(path, _cacheDirectory))
            {
                Log.Warning("Refusing to launch {Path}: it is not in the update cache", path);
                return 2;
            }

            if (!File.Exists(path) || !HashMatches(path, result.Sha256Hash))
            {
                Log.Warning("Downloaded installer {Path} is missing or no longer matches its checksum", path);
                result.DownloadState = UpdateDownloadStates.Failed;
                result.FailureReason = "the downloaded installer changed or was removed";
                result.VerifiedInstallerPath = null;
                UpdateResultStore.SaveAtomic(result, _resultFilePath);
                OpenUrl(Constants.DownloadPageUrl);
                return 2;
            }

            Log.Information("Launching the verified installer for v{Version}", result.Version);
            return _start(new ProcessStartInfo(path) { UseShellExecute = true }) ? 0 : 2;
        }

        internal static bool IsInside(string path, string directory)
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HashMatches(string path, string expected)
        {
            if (string.IsNullOrWhiteSpace(expected))
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHttps(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

        private void OpenUrl(string url) => _start(new ProcessStartInfo(url) { UseShellExecute = true });

        private static bool StartDefault(ProcessStartInfo info)
        {
            try
            {
                using var process = Process.Start(info);
                return true;
            }
            catch (Exception ex)
            {
                // Declining the admin prompt lands here too (Win32Exception 1223) -- not an error.
                Log.Warning(ex, "Could not start {File}", info.FileName);
                return false;
            }
        }
    }
}
