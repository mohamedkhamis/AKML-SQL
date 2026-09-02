using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Update;
using Serilog;

namespace AkmlSql.Updater
{
    /// <summary>
    /// Implements <c>AkmlSql.Updater.exe --download</c> per spec 036
    /// <c>contracts/update-manifest.md</c> §3 (FR-039/FR-039a/FR-040): re-reads the result file
    /// written by <c>--check</c>, downloads the installer to a <c>.partial</c> in the cache,
    /// verifies its SHA-256 against the manifest hash, and only then renames it to the final
    /// name and records <see cref="UpdateResult.VerifiedInstallerPath"/>. Anonymous (FR-034) —
    /// no token, no credential.
    ///
    /// Exit codes: <c>0</c> success or nothing to do (incl. cancelled), <c>2</c> the run did not
    /// produce a verified installer (checksum mismatch, non-HTTPS URL, transport error — the
    /// persisted <see cref="UpdateResult.FailureReason"/> says which), <c>1</c> is reserved for
    /// usage errors in <c>Program.Main</c>.
    /// </summary>
    public sealed class UpdateDownloader
    {
        private readonly HttpMessageHandler _httpMessageHandler;
        private readonly string _resultFilePath;
        private readonly string _cacheDirectory;

        /// <param name="httpMessageHandler">Transport; tests inject a stub handler.</param>
        /// <param name="resultFilePath">The <c>update-available.json</c> path.</param>
        /// <param name="cacheDirectory">Download cache (<c>Constants.CachePath</c> in production).</param>
        public UpdateDownloader(HttpMessageHandler httpMessageHandler, string resultFilePath, string cacheDirectory)
        {
            _httpMessageHandler = httpMessageHandler ?? throw new ArgumentNullException(nameof(httpMessageHandler));
            _resultFilePath = resultFilePath ?? throw new ArgumentNullException(nameof(resultFilePath));
            _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
        }

        public async Task<int> RunAsync(CancellationToken cancellationToken = default)
        {
            // 1. Read the result file — nothing to do unless a check found an update.
            var result = UpdateResultStore.Load(_resultFilePath);
            if (result is not { Available: true })
            {
                Log.Debug("No update offer on disk -- nothing to download");
                return 0;
            }

            // HTTPS only, rejected before the request (mirrors CheckUpdateCommand.IsValidHttpsUrl).
            if (!IsValidHttpsUrl(result.DownloadUrl))
            {
                return Fail(result, "download URL is not HTTPS");
            }

            // FR-040: no published checksum means the installer cannot be verified — fail closed.
            if (string.IsNullOrWhiteSpace(result.Sha256Hash))
            {
                return Fail(result, "manifest carries no checksum");
            }

            var finalPath = Path.Combine(_cacheDirectory, $"AKMLSQLSetup-{result.Version}.exe");
            var partialPath = finalPath + ".partial";
            var reachedRename = false;

            try
            {
                // Already verified in an earlier run and the file is still intact -> done.
                if (result.DownloadState == UpdateDownloadStates.Verified
                    && !string.IsNullOrEmpty(result.VerifiedInstallerPath)
                    && File.Exists(result.VerifiedInstallerPath)
                    && await HashMatchesAsync(result.VerifiedInstallerPath, result.Sha256Hash, cancellationToken))
                {
                    Log.Information("Update v{Version} already downloaded and verified", result.Version);
                    return 0;
                }

                // 2. Persist the downloading state before touching the network.
                result.DownloadState = UpdateDownloadStates.Downloading;
                result.FailureReason = null;
                result.VerifiedInstallerPath = null;
                UpdateResultStore.SaveAtomic(result, _resultFilePath);

                Directory.CreateDirectory(_cacheDirectory);
                // A partial left by an interrupted previous run can never be resumed — drop it.
                TryDelete(partialPath);

                // 3. Fetch.
                using var client = new HttpClient(_httpMessageHandler, disposeHandler: false);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"AkmlSql.Updater/{Constants.RuntimeVersion}");

                Log.Information("Downloading update v{Version}", result.Version);
                using (var response = await client.GetAsync(
                           result.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var target = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(target, cancellationToken);
                }

                // 4+5. Verify against the manifest hash; a mismatch aborts (FR-040).
                if (!await HashMatchesAsync(partialPath, result.Sha256Hash, cancellationToken))
                {
                    Log.Warning("Update download checksum mismatch for v{Version}", result.Version);
                    return Fail(result, "checksum mismatch");
                }

                // 6+7. Rename to the final name and record the verified absolute path.
                File.Move(partialPath, finalPath, overwrite: true);
                reachedRename = true;

                result.VerifiedInstallerPath = Path.GetFullPath(finalPath);
                result.DownloadState = UpdateDownloadStates.Verified;
                result.FailureReason = null;
                UpdateResultStore.SaveAtomic(result, _resultFilePath);
                Log.Information("Update v{Version} downloaded and verified", result.Version);
                return 0;
            }
            catch (OperationCanceledException)
            {
                // State machine: downloading --cancel--> available. No partial survives (finally).
                // A cancel during the already-verified probe leaves that verified state untouched.
                if (result.DownloadState == UpdateDownloadStates.Downloading)
                {
                    RollBackToAvailable(result);
                }

                Log.Information("Update download cancelled");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Update download failed");
                return Fail(result, ex.Message);
            }
            finally
            {
                // FR-039a: cancel, failure or interruption never leaves a partial behind.
                if (!reachedRename)
                {
                    TryDelete(partialPath);
                }
            }
        }

        private int Fail(UpdateResult result, string reason)
        {
            result.DownloadState = UpdateDownloadStates.Failed;
            result.FailureReason = reason;
            result.VerifiedInstallerPath = null;
            TrySave(result);
            return 2;
        }

        private void RollBackToAvailable(UpdateResult result)
        {
            result.DownloadState = UpdateDownloadStates.None;
            result.FailureReason = null;
            result.VerifiedInstallerPath = null;
            TrySave(result);
        }

        private void TrySave(UpdateResult result)
        {
            try
            {
                UpdateResultStore.SaveAtomic(result, _resultFilePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist update result");
            }
        }

        private static async Task<bool> HashMatchesAsync(string path, string expectedSha256, CancellationToken cancellationToken)
        {
            var actual = await ComputeSha256Async(path, cancellationToken);
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool IsValidHttpsUrl(string url)
        {
            return !string.IsNullOrEmpty(url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete {Path}", path);
            }
        }
    }
}
