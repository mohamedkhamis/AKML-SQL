#nullable enable
using System;
using System.IO;
using AkmlSql.Core.Update;
using AkmlSql.Shell.Shared.Update;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 036 US5 / FR-039a, shell side: cancelling the download kills the updater process —
    /// and a killed process never runs its finally blocks — so the shell itself deletes the
    /// <c>.partial</c> and rolls the persisted state back to "available" (offer retained).
    /// </summary>
    public sealed class UpdateDownloadCleanupTests : IDisposable
    {
        private readonly string _root;
        private readonly string _cacheDir;
        private readonly string _resultPath;

        public UpdateDownloadCleanupTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "akml-updatecleanup-" + Guid.NewGuid().ToString("N"));
            _cacheDir = Path.Combine(_root, "cache");
            _resultPath = Path.Combine(_root, "state", "update-available.json");
            Directory.CreateDirectory(_cacheDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [Fact]
        public void AfterCancel_deletes_the_partial_and_rolls_back_to_available()
        {
            var partial = Path.Combine(_cacheDir, "AKMLSQLSetup-1.26.0903.0900.exe.partial");
            File.WriteAllBytes(partial, new byte[4096]);
            UpdateResultStore.SaveAtomic(new UpdateResult
            {
                Available = true,
                Version = "1.26.0903.0900",
                DownloadState = UpdateDownloadStates.Downloading
            }, _resultPath);

            UpdateDownloadCleanup.AfterCancel("1.26.0903.0900", _cacheDir, _resultPath);

            Assert.False(File.Exists(partial));
            var result = UpdateResultStore.Load(_resultPath);
            Assert.NotNull(result);
            Assert.True(result!.Available); // offer retained
            Assert.Equal(UpdateDownloadStates.None, result.DownloadState);
            Assert.Null(result.FailureReason);
        }

        [Fact]
        public void AfterCancel_leaves_a_verified_result_untouched()
        {
            // Defensive: a cancel racing a completed verification must not discard it.
            UpdateResultStore.SaveAtomic(new UpdateResult
            {
                Available = true,
                Version = "1.26.0903.0900",
                DownloadState = UpdateDownloadStates.Verified,
                VerifiedInstallerPath = Path.Combine(_cacheDir, "AKMLSQLSetup-1.26.0903.0900.exe")
            }, _resultPath);

            UpdateDownloadCleanup.AfterCancel("1.26.0903.0900", _cacheDir, _resultPath);

            var result = UpdateResultStore.Load(_resultPath);
            Assert.Equal(UpdateDownloadStates.Verified, result!.DownloadState);
            Assert.NotNull(result.VerifiedInstallerPath);
        }

        [Fact]
        public void AfterCancel_without_a_result_file_is_a_no_op()
        {
            UpdateDownloadCleanup.AfterCancel("1.26.0903.0900", _cacheDir, _resultPath);

            Assert.False(File.Exists(_resultPath));
        }
    }
}
