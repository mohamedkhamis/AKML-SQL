using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Core.Logging;
using AkmlSql.Core.Update;
using Serilog;

namespace AkmlSql.Updater
{
    internal static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static async Task<int> Main(string[] args)
        {
            try
            {
                LoggerFactory.Initialize();

                if (args.Length == 0 || args[0] != "--check")
                {
                    Log.Information("Usage: AkmlSql.Updater.exe --check");
                    return 1;
                }

                Log.Information("Update check started for v{Version}", Constants.Version);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"AkmlSql.Updater/{Constants.Version}");

                var json = await client.GetStringAsync(Constants.UpdateManifestUrl, cts.Token);
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);

                if (manifest == null)
                {
                    Log.Warning("Update manifest deserialized to null");
                    UpdateLastCheckTimestamp();
                    return 0;
                }

                if (IsNewerVersion(manifest.Version, Constants.Version))
                {
                    Log.Information("Update available: v{Current} -> v{Latest}",
                        Constants.Version, manifest.Version);

                    var result = new UpdateResult
                    {
                        Available = true,
                        Version = manifest.Version,
                        DownloadUrl = manifest.DownloadUrl,
                        ReleaseNotesUrl = manifest.ReleaseNotesUrl,
                        CheckedAt = DateTimeOffset.UtcNow
                    };

                    var resultJson = JsonSerializer.Serialize(result, JsonOptions);

                    var resultDir = Path.GetDirectoryName(Constants.UpdateResultFilePath);
                    if (resultDir != null)
                    {
                        Directory.CreateDirectory(resultDir);
                    }

                    // Atomic write: write to temp file then rename
                    var tempPath = Constants.UpdateResultFilePath + ".tmp";
                    await File.WriteAllTextAsync(tempPath, resultJson, cts.Token);
                    File.Move(tempPath, Constants.UpdateResultFilePath, overwrite: true);
                    Log.Information("Update result written to {Path}", Constants.UpdateResultFilePath);
                }
                else
                {
                    Log.Information("No update available (current: v{Current}, latest: v{Latest})",
                        Constants.Version, manifest.Version);

                    // Remove stale update result if present
                    if (File.Exists(Constants.UpdateResultFilePath))
                    {
                        File.Delete(Constants.UpdateResultFilePath);
                    }
                }

                UpdateLastCheckTimestamp();
                return 0;
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Update check timed out");
                return 0;
            }
            catch (HttpRequestException ex)
            {
                Log.Warning(ex, "Update check failed (network error)");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Update check failed unexpectedly");
                return 0;
            }
            finally
            {
                LoggerFactory.Shutdown();
            }
        }

        /// <summary>
        /// Compares version strings safely, handling SemVer pre-release suffixes
        /// by stripping the pre-release tag before comparison.
        /// </summary>
        private static bool IsNewerVersion(string latest, string current)
        {
            try
            {
                var latestVersion = new Version(StripPreRelease(latest));
                var currentVersion = new Version(StripPreRelease(current));
                return latestVersion > currentVersion;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to compare versions: {Latest} vs {Current}", latest, current);
                return false;
            }
        }

        private static string StripPreRelease(string version)
        {
            var dashIndex = version.IndexOf('-');
            return dashIndex >= 0 ? version.Substring(0, dashIndex) : version;
        }

        private static void UpdateLastCheckTimestamp()
        {
            try
            {
                var settings = ConfigManager.Load();
                settings.LastUpdateCheck = DateTimeOffset.UtcNow;
                ConfigManager.Save(settings);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update last check timestamp");
            }
        }
    }
}
