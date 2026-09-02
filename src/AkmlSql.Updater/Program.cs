using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        private static async Task<int> Main(string[] args)
        {
            var downloadMode = args.Length > 0 && args[0] == "--download";
            try
            {
                LoggerFactory.Initialize();

                if (args.Length == 0 || (args[0] != "--check" && args[0] != "--download"))
                {
                    Log.Information("Usage: AkmlSql.Updater.exe --check|--download");
                    return 1;
                }

                if (downloadMode)
                {
                    return await RunDownload();
                }

                return await RunCheck();
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
                return downloadMode ? 2 : 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Update check failed unexpectedly");
                return downloadMode ? 2 : 0;
            }
            finally
            {
                LoggerFactory.Shutdown();
            }
        }

        /// <summary>
        /// Fetches the manifest, compares versions and writes the result file. Always exits 0 —
        /// a failed check is never a user-facing error (FR-041).
        /// </summary>
        private static async Task<int> RunCheck()
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
                UpdateLastCheckTimestamp();
                return 0;
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

                // Atomic write: temp file + rename, via the shared store (data-model V21).
                UpdateResultStore.SaveAtomic(result, Constants.UpdateResultFilePath);
                Log.Information("Update result written to {Path}", Constants.UpdateResultFilePath);
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
            }

            UpdateLastCheckTimestamp();
            return 0;
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

            var downloader = new UpdateDownloader(
                new HttpClientHandler(),
                Constants.UpdateResultFilePath,
                Constants.CachePath);
            return await downloader.RunAsync(cts.Token);
        }

        /// <summary>
        /// Stamps <c>lastUpdateCheck</c> in config.json. Done as a targeted JSON edit
        /// (JsonNode, no reflection) rather than a ConfigManager round-trip: this exe is
        /// trimmed, so the reflection-based AppSettings serialization path is unavailable.
        /// All other settings pass through untouched.
        /// </summary>
        private static void UpdateLastCheckTimestamp()
        {
            try
            {
                var path = Constants.ConfigFilePath;
                var config = File.Exists(path)
                    ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
                    : new JsonObject();
                config["lastUpdateCheck"] = DateTimeOffset.UtcNow.ToString("O");

                var directory = Path.GetDirectoryName(path);
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                }

                // Atomic write: temp file + rename (same pattern as every other JSON write)
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tempPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update last check timestamp");
            }
        }
    }
}
