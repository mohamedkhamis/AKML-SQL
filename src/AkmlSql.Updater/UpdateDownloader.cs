using System;
using System.Diagnostics;
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
    /// produce a verified installer (checksum mismatch, non-HTTPS URL, transport error, a stalled
    /// or truncated transfer — the persisted <see cref="UpdateResult.FailureReason"/> says which),
    /// <c>1</c> is reserved for usage errors in <c>Program.Main</c>.
    /// <para>
    /// The checksum is computed from the bytes as they arrive, so the installer is read once
    /// rather than written and then read back in full. While it runs the downloader publishes
    /// <see cref="UpdateDownloadProgress"/> for the shell's download window, and it gives up when
    /// no data arrives for <see cref="DefaultStallTimeout"/> instead of waiting indefinitely. The
    /// log records how long the server took to answer, the transfer and its rate, and the
    /// verification, so a slow update shows where its time went.
    /// </para>
    /// </summary>
    public sealed class UpdateDownloader
    {
        /// <summary>No data for this long ends the download as failed.</summary>
        public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(90);

        /// <summary>The shortest gap between two progress snapshots.</summary>
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

        private const int BufferSize = 128 * 1024;

        private readonly HttpMessageHandler _httpMessageHandler;
        private readonly string _resultFilePath;
        private readonly string _cacheDirectory;
        private readonly string? _progressFilePath;
        private readonly TimeSpan _stallTimeout;

        /// <param name="httpMessageHandler">Transport; tests inject a stub handler.</param>
        /// <param name="resultFilePath">The <c>update-available.json</c> path.</param>
        /// <param name="cacheDirectory">Download cache (<c>Constants.CachePath</c> in production).</param>
        /// <param name="progressFilePath">Where to publish <see cref="UpdateDownloadProgress"/>; <c>null</c> publishes nothing.</param>
        /// <param name="stallTimeout">No data for this long fails the download (default <see cref="DefaultStallTimeout"/>).</param>
        public UpdateDownloader(HttpMessageHandler httpMessageHandler, string resultFilePath, string cacheDirectory,
            string? progressFilePath = null, TimeSpan? stallTimeout = null)
        {
            _httpMessageHandler = httpMessageHandler ?? throw new ArgumentNullException(nameof(httpMessageHandler));
            _resultFilePath = resultFilePath ?? throw new ArgumentNullException(nameof(resultFilePath));
            _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
            _progressFilePath = progressFilePath;
            _stallTimeout = stallTimeout ?? DefaultStallTimeout;
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

                // 3. Fetch, hashing the bytes as they arrive.
                using var client = new HttpClient(_httpMessageHandler, disposeHandler: false);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"AkmlSql.Updater/{Constants.RuntimeVersion}");

                Log.Information("Downloading update v{Version} from {Url}", result.Version, result.DownloadUrl);
                var clock = Stopwatch.StartNew();
                Publish(result.Version, UpdateDownloadPhases.Connecting, 0, null);
                string actualHash;
                long received;
                TimeSpan transferTime;
                using (var response = await client.GetAsync(
                           result.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength;
                    Log.Information("Download server answered in {Seconds:0.0} s: {Status}, {Size} from {Host}",
                        clock.Elapsed.TotalSeconds, (int)response.StatusCode,
                        total.HasValue ? $"{total.Value / 1048576.0:0.0} MB" : "size not sent",
                        response.RequestMessage?.RequestUri?.Host ?? "?");

                    var transfer = Stopwatch.StartNew();
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using (var target = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                     BufferSize, useAsync: true))
                    {
                        (received, actualHash) = await CopyAndHashAsync(source, target, result.Version, total, cancellationToken);
                    }

                    transferTime = transfer.Elapsed;
                    Log.Information("Received {Size:0.0} MB in {Seconds:0.0} s ({Rate:0.00} MB/s)",
                        received / 1048576.0, transferTime.TotalSeconds,
                        received / 1048576.0 / Math.Max(transferTime.TotalSeconds, 0.001));

                    // A connection closed early is a failed download, not a short installer.
                    if (total.HasValue && received != total.Value)
                    {
                        return Fail(result, $"the download ended early ({received:N0} of {total.Value:N0} bytes)");
                    }
                }

                // 4+5. Verify against the manifest hash; a mismatch aborts (FR-040).
                Publish(result.Version, UpdateDownloadPhases.Verifying, received, received);
                var finishing = Stopwatch.StartNew();
                if (!string.Equals(actualHash, result.Sha256Hash, StringComparison.OrdinalIgnoreCase))
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
                // Time spent after the last byte is the file system's (an antivirus scan of the
                // new installer shows up here), not the network's.
                Log.Information("Update v{Version} downloaded and verified in {Seconds:0.0} s (finishing took {Finish:0.0} s)",
                    result.Version, clock.Elapsed.TotalSeconds, finishing.Elapsed.TotalSeconds);
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

                if (_progressFilePath != null)
                {
                    UpdateDownloadProgressStore.TryDelete(_progressFilePath);
                }
            }
        }

        /// <summary>
        /// Copies <paramref name="source"/> to <paramref name="target"/>, hashing as it goes and
        /// publishing progress. A read that brings no data within the stall timeout ends the
        /// download with a <see cref="TimeoutException"/> (the caller records it as the failure).
        /// </summary>
        private async Task<(long Received, string Sha256)> CopyAndHashAsync(
            Stream source, Stream target, string version, long? total, CancellationToken cancellationToken)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            long received = 0;
            var sinceReport = Stopwatch.StartNew();
            Publish(version, UpdateDownloadPhases.Downloading, 0, total);

            while (true)
            {
                int read;
                using (var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    stall.CancelAfter(_stallTimeout);
                    try
                    {
                        read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), stall.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"the download stalled: no data arrived for {(int)_stallTimeout.TotalSeconds} seconds");
                    }
                }

                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;

                if (sinceReport.Elapsed >= ProgressInterval)
                {
                    Publish(version, UpdateDownloadPhases.Downloading, received, total);
                    sinceReport.Restart();
                }
            }

            return (received, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }

        /// <summary>Writes a progress snapshot for the shell; a failed write never stops the download.</summary>
        private void Publish(string version, string phase, long received, long? total)
        {
            if (_progressFilePath == null)
            {
                return;
            }

            try
            {
                UpdateDownloadProgressStore.Save(new UpdateDownloadProgress
                {
                    Version = version,
                    Phase = phase,
                    BytesReceived = received,
                    TotalBytes = total,
                    UpdatedAt = DateTimeOffset.UtcNow,
                }, _progressFilePath);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not write the download progress");
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
