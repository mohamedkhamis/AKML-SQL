using System;
using System.IO;
using System.Text.Json;
using Xunit;
using AkmlSql.Core.Update;

namespace AkmlSql.Core.Tests.Update
{
    /// <summary>
    /// Spec 036 US5 / data-model V21: every write to <c>update-available.json</c> is atomic
    /// (temp file + move) so the shell never reads a half-written result, and the download
    /// lifecycle fields round-trip (FR-039/FR-039a/FR-041).
    /// </summary>
    public sealed class UpdateResultStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public UpdateResultStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "akml-updateresult-" + Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_dir, "cache", "update-available.json");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [Fact]
        public void Load_MissingFile_ReturnsNull()
        {
            Assert.Null(UpdateResultStore.Load(_path));
        }

        [Fact]
        public void Load_InvalidJson_ReturnsNull()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, "{ this is not json");

            Assert.Null(UpdateResultStore.Load(_path));
        }

        [Fact]
        public void SaveAtomic_CreatesDirectory_AndRoundTrips_AllFields()
        {
            var result = new UpdateResult
            {
                Available = true,
                Version = "1.26.0903.0900",
                DownloadUrl = "https://example.com/setup.exe",
                ReleaseNotesUrl = "https://example.com/notes",
                Sha256Hash = "deadbeef",
                CheckedAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
                VerifiedInstallerPath = @"C:\cache\AKMLSQLSetup-1.26.0903.0900.exe",
                DownloadState = "verified",
                FailureReason = null
            };

            UpdateResultStore.SaveAtomic(result, _path);
            var loaded = UpdateResultStore.Load(_path);

            Assert.NotNull(loaded);
            Assert.True(loaded!.Available);
            Assert.Equal(result.Version, loaded.Version);
            Assert.Equal(result.DownloadUrl, loaded.DownloadUrl);
            Assert.Equal(result.ReleaseNotesUrl, loaded.ReleaseNotesUrl);
            Assert.Equal(result.Sha256Hash, loaded.Sha256Hash);
            Assert.Equal(result.CheckedAt, loaded.CheckedAt);
            Assert.Equal(result.VerifiedInstallerPath, loaded.VerifiedInstallerPath);
            Assert.Equal("verified", loaded.DownloadState);
            Assert.Null(loaded.FailureReason);
        }

        [Fact]
        public void SaveAtomic_WritesCamelCaseJson()
        {
            UpdateResultStore.SaveAtomic(new UpdateResult { Available = true, Version = "1.2.3" }, _path);

            var json = File.ReadAllText(_path);
            Assert.Contains("\"available\": true", json);
            Assert.Contains("\"downloadState\": \"none\"", json);
        }

        [Fact]
        public void SaveAtomic_LeavesNoTempFile_AndOverwritesCleanly()
        {
            UpdateResultStore.SaveAtomic(new UpdateResult { Available = true, Version = "1.2.3" }, _path);
            UpdateResultStore.SaveAtomic(new UpdateResult { Available = true, Version = "1.2.4" }, _path);

            // temp + move: no orphaned temp files, and the survivor holds complete JSON.
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories));
            var loaded = UpdateResultStore.Load(_path);
            Assert.Equal("1.2.4", loaded!.Version);

            // The file on disk parses as one complete document — never a truncated half-write.
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            Assert.Equal("1.2.4", doc.RootElement.GetProperty("version").GetString());
        }

        [Fact]
        public void SaveAtomic_PreservesFailureFields()
        {
            var result = new UpdateResult
            {
                Available = true,
                Version = "1.2.3",
                DownloadState = "failed",
                FailureReason = "checksum mismatch"
            };

            UpdateResultStore.SaveAtomic(result, _path);
            var loaded = UpdateResultStore.Load(_path);

            Assert.Equal("failed", loaded!.DownloadState);
            Assert.Equal("checksum mismatch", loaded.FailureReason);
            Assert.Null(loaded.VerifiedInstallerPath);
        }

        // ── PR #251 review finding 1: a re-check offering the SAME version must not discard a
        // verified download (declined install → next 24 h check must not force a re-download). ──

        /// <summary>Simulates RunCheck's write path: fresh manifest result + carry-forward + save.</summary>
        private UpdateResult SimulateCheckSave(UpdateResult fresh)
        {
            var existing = UpdateResultStore.Load(_path);
            UpdateResultStore.CarryForwardDownloadState(fresh, existing);
            UpdateResultStore.SaveAtomic(fresh, _path);
            return UpdateResultStore.Load(_path)!;
        }

        [Fact]
        public void Recheck_SameVersion_KeepsVerifiedDownloadState()
        {
            UpdateResultStore.SaveAtomic(new UpdateResult
            {
                Available = true,
                Version = "1.26.0903.0900",
                DownloadUrl = "https://cdn.example.com/old.exe",
                ReleaseNotesUrl = "https://example.com/notes-old",
                Sha256Hash = "oldhash",
                CheckedAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
                VerifiedInstallerPath = @"C:\cache\AKMLSQLSetup-1.26.0903.0900.exe",
                DownloadState = "verified",
                FailureReason = null
            }, _path);

            var reloaded = SimulateCheckSave(new UpdateResult
            {
                Available = true,
                Version = "1.26.0903.0900",
                DownloadUrl = "https://cdn.example.com/new.exe",
                ReleaseNotesUrl = "https://example.com/notes-new",
                Sha256Hash = "newhash",
                CheckedAt = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)
            });

            // Lifecycle carried forward; manifest fields always refreshed.
            Assert.Equal("verified", reloaded.DownloadState);
            Assert.Equal(@"C:\cache\AKMLSQLSetup-1.26.0903.0900.exe", reloaded.VerifiedInstallerPath);
            Assert.Null(reloaded.FailureReason);
            Assert.Equal("https://cdn.example.com/new.exe", reloaded.DownloadUrl);
            Assert.Equal("newhash", reloaded.Sha256Hash);
            Assert.Equal(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero), reloaded.CheckedAt);
        }

        [Fact]
        public void Recheck_NewerVersion_StartsClean()
        {
            UpdateResultStore.SaveAtomic(new UpdateResult
            {
                Available = true,
                Version = "1.26.0903.0900",
                VerifiedInstallerPath = @"C:\cache\AKMLSQLSetup-1.26.0903.0900.exe",
                DownloadState = "verified",
                FailureReason = "checksum mismatch"
            }, _path);

            var reloaded = SimulateCheckSave(new UpdateResult
            {
                Available = true,
                Version = "1.26.0904.1010",
                CheckedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)
            });

            // A stale verified path must never survive a version change.
            Assert.Equal("none", reloaded.DownloadState);
            Assert.Null(reloaded.VerifiedInstallerPath);
            Assert.Null(reloaded.FailureReason);
        }

        [Fact]
        public void Recheck_NoExistingResult_BehavesAsBefore()
        {
            var reloaded = SimulateCheckSave(new UpdateResult
            {
                Available = true,
                Version = "1.26.0903.0900",
                DownloadUrl = "https://cdn.example.com/setup.exe",
                CheckedAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero)
            });

            Assert.True(reloaded.Available);
            Assert.Equal("1.26.0903.0900", reloaded.Version);
            Assert.Equal("none", reloaded.DownloadState);
            Assert.Null(reloaded.VerifiedInstallerPath);
        }
    }
}
