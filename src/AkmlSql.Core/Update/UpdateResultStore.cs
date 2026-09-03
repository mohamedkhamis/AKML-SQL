using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace AkmlSql.Core.Update
{
    /// <summary>
    /// Reads and writes <c>update-available.json</c> (spec 036 US5). Every write is atomic —
    /// temp file then rename — so the shell never observes a half-written result (data-model
    /// V21). Shared by the updater (<c>--check</c> and <c>--download</c>) and the shell's
    /// guided-update flow. On netstandard2.0 the rename uses <c>File.Replace</c>, on .NET 10+
    /// <c>File.Move(overwrite: true)</c> — the same split as <c>ConfigManager.Save</c>.
    /// Serialization goes through <see cref="UpdateJsonContext"/> (source-generated) because
    /// the trimmed single-file updater has reflection-based JSON disabled.
    /// </summary>
    public static class UpdateResultStore
    {
        /// <summary>Loads the result file, or <c>null</c> when it is missing or unreadable.</summary>
        public static UpdateResult? Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                return JsonSerializer.Deserialize(File.ReadAllText(path), UpdateJsonContext.Default.UpdateResult);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read update result from {Path}", path);
                return null;
            }
        }

        /// <summary>
        /// Persists <paramref name="result"/> atomically: writes a temp sibling and renames it
        /// over <paramref name="path"/>. Creates the containing directory when missing.
        /// </summary>
        public static void SaveAtomic(UpdateResult result, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(result, UpdateJsonContext.Default.UpdateResult);

            // Atomic write: write to temp file then rename
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
#if NETSTANDARD2_0
            // File.Replace is atomic on NTFS (avoids TOCTOU race between Delete + Move)
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
#else
            File.Move(tempPath, path, overwrite: true);
#endif
        }

        /// <summary>
        /// Carries the download lifecycle (<see cref="UpdateResult.DownloadState"/>,
        /// <see cref="UpdateResult.VerifiedInstallerPath"/>, <see cref="UpdateResult.FailureReason"/>)
        /// from an existing result onto a fresh one when both offer the SAME version. A user who
        /// downloaded and verified an update and then declined the install must not be made to
        /// re-download ~70 MB because the next scheduled check rewrote the file (spec 036 edge
        /// case: "update already downloaded and verified on a previous attempt"). A version
        /// change starts clean: a stale verified path must never survive it.
        /// </summary>
        public static void CarryForwardDownloadState(UpdateResult fresh, UpdateResult? existing)
        {
            if (existing is { Available: true }
                && string.Equals(existing.Version, fresh.Version, StringComparison.Ordinal))
            {
                fresh.DownloadState = existing.DownloadState;
                fresh.VerifiedInstallerPath = existing.VerifiedInstallerPath;
                fresh.FailureReason = existing.FailureReason;
            }
        }
    }
}
