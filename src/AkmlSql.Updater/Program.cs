using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Logging;
using AkmlSql.Core.Update;
using Serilog;

namespace AkmlSql.Updater
{
    internal static class Program
    {
        private const string Usage =
            "Usage: AkmlSql.Updater.exe --check | --download | --scheduled | --check-now | --install [akmlsql-update:install|details]" +
            " | --configure auto-update=on|off error-reports=on|off";

        private static async Task<int> Main(string[] args)
        {
            var mode = args.Length > 0 ? args[0] : string.Empty;
            try
            {
                LoggerFactory.Initialize();

                switch (mode)
                {
                    case "--check":
                        await RunCheck();
                        return 0;
                    case "--download":
                        return await RunDownload();
                    case "--scheduled":
                        return await RunScheduled(interactive: false);
                    case "--check-now":
                        return await RunScheduled(interactive: true);
                    case "--install":
                        return new InstallLauncher(Constants.UpdateResultFilePath, Constants.CachePath, Constants.RuntimeVersion)
                            .Run(InstallLauncher.ActionFrom(args.Length > 1 ? args[1] : null));
                    case "--configure":
                        return RunConfigure(args.Skip(1));
                    default:
                        Log.Information(Usage);
                        Console.Error.WriteLine(Usage);
                        return 1;
                }
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Update check timed out");
                return 0;
            }
            catch (HttpRequestException ex)
            {
                // --check: a failed check is not a user-facing error (FR-041). --download: the
                // run produced no verified installer, and the downloader already persisted why.
                Log.Warning(ex, "Update check failed (network error)");
                return mode == "--download" ? 2 : 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Updater {Mode} failed unexpectedly", mode);
                return mode is "--check" or "--scheduled" or "--check-now" ? 0 : 2;
            }
            finally
            {
                LoggerFactory.Shutdown();
            }
        }

        /// <summary>
        /// Fetches the manifest, compares versions and writes the result file. Throws on a network
        /// failure; each caller decides what that means (for <c>--check</c>: nothing — FR-041).
        /// </summary>
        private static async Task RunCheck()
        {
            Log.Information("Update check started for v{Version}", Constants.RuntimeVersion);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"AkmlSql.Updater/{Constants.RuntimeVersion}");

            var json = await client.GetStringAsync(Constants.UpdateManifestUrl, cts.Token);
            // Source-generated metadata: reflection-based STJ is disabled in this trimmed exe.
            var manifest = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.UpdateManifest);

            if (manifest == null)
            {
                Log.Warning("Update manifest deserialized to null");
                UpdaterPreferences.StampLastCheck(Constants.ConfigFilePath, DateTimeOffset.UtcNow);
                return;
            }

            if (VersionComparer.IsNewer(manifest.Version, Constants.RuntimeVersion))
            {
                Log.Information("Update available: v{Current} -> v{Latest}",
                    Constants.RuntimeVersion, manifest.Version);

                var result = new UpdateResult
                {
                    Available = true,
                    Version = manifest.Version,
                    DownloadUrl = manifest.DownloadUrl,
                    ReleaseNotesUrl = manifest.ReleaseNotesUrl,
                    Sha256Hash = manifest.Sha256Hash ?? string.Empty,
                    CheckedAt = DateTimeOffset.UtcNow
                };

                // A repeated offer of the SAME version must not discard a completed download (or
                // re-notify): carry the lifecycle forward so the download short-circuit in
                // UpdateDownloader stays reachable and a declined install never re-downloads.
                var existing = UpdateResultStore.Load(Constants.UpdateResultFilePath);
                UpdateResultStore.CarryForwardDownloadState(result, existing);

                // Atomic write: temp file + rename, via the shared store (data-model V21).
                UpdateResultStore.SaveAtomic(result, Constants.UpdateResultFilePath);
                Log.Information("Update result written to {Path}", Constants.UpdateResultFilePath);
                PruneDownloadCache(keepVersion: manifest.Version);
            }
            else
            {
                Log.Information("No update available (current: v{Current}, latest: v{Latest})",
                    Constants.RuntimeVersion, manifest.Version);

                // Remove stale update result if present
                if (File.Exists(Constants.UpdateResultFilePath))
                {
                    File.Delete(Constants.UpdateResultFilePath);
                }

                PruneDownloadCache(keepVersion: null);
            }

            UpdaterPreferences.StampLastCheck(Constants.ConfigFilePath, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Deletes downloaded installers other than <paramref name="keepVersion"/>'s. Each is
        /// 100 MB or more, and nothing else ever removed them: every update left its installer
        /// behind in the user's profile for good.
        /// </summary>
        private static void PruneDownloadCache(string? keepVersion)
        {
            try
            {
                if (!Directory.Exists(Constants.CachePath))
                {
                    return;
                }

                var keep = keepVersion is null ? null : $"AKMLSQLSetup-{keepVersion}.exe";
                foreach (var file in Directory.EnumerateFiles(Constants.CachePath, "AKMLSQLSetup-*.exe*"))
                {
                    // StartsWith, not Equals: the kept version's ".partial" may be a download that
                    // an IDE has in progress right now.
                    if (keep is null || !Path.GetFileName(file).StartsWith(keep, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                        Log.Information("Removed old downloaded installer {File}", Path.GetFileName(file));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An installer still running from the cache is locked; the next check removes it.
                Log.Debug(ex, "Could not prune the download cache");
            }
        }

        /// <summary>
        /// Downloads + verifies the offered installer (contracts/update-manifest.md §3).
        /// Ctrl+C maps to a graceful cancellation so the .partial cleanup in
        /// <see cref="UpdateDownloader"/> always runs (FR-039a).
        /// </summary>
        private static async Task<int> RunDownload()
        {
            Log.Information("Update download started for v{Version}", Constants.RuntimeVersion);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true; // graceful: let the downloader's finally delete the .partial
                cts.Cancel();
            };

            return await DownloadAsync(cts.Token);
        }

        private static Task<int> DownloadAsync(CancellationToken cancellationToken) =>
            new UpdateDownloader(new HttpClientHandler(), Constants.UpdateResultFilePath, Constants.CachePath)
                .RunAsync(cancellationToken);

        /// <summary>
        /// The scheduled task's run (and, interactive, the Start-menu "Check for AKML SQL updates"):
        /// check, download, notify. Never installs.
        /// </summary>
        private static async Task<int> RunScheduled(bool interactive)
        {
            // A generous ceiling: a stalled download must not keep a hidden process alive for days
            // (the task's own time limit is the backstop).
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(25));
            return await new ScheduledUpdate(
                Constants.ConfigFilePath,
                Constants.UpdateResultFilePath,
                Constants.RuntimeVersion,
                RunCheck,
                DownloadAsync,
                new UpdateToast()).RunAsync(interactive, cts.Token);
        }

        /// <summary>
        /// <c>--configure auto-update=on error-reports=off</c>: the installer records the options
        /// page's choices through this, run as the signed-in user (not the elevated installer), so
        /// they land in that user's own config.json.
        /// </summary>
        private static int RunConfigure(System.Collections.Generic.IEnumerable<string> settings)
        {
            var values = UpdaterPreferences.ParseConfigureArgs(settings, out var error);
            if (values is null)
            {
                Log.Warning("--configure: {Error}", error);
                Console.Error.WriteLine(error);
                return 1;
            }

            UpdaterPreferences.Write(Constants.ConfigFilePath, values);
            Log.Information("Preferences written: {Values}",
                string.Join(", ", values.Select(v => $"{v.Key}={v.Value}")));
            return 0;
        }
    }
}
