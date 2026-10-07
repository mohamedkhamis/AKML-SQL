using System;
using System.IO;
using System.Text.Json;

namespace AkmlSql.Core.Update
{
    /// <summary>
    /// How far the updater's <c>--download</c> has got, written by the updater to
    /// <c>%AppData%\AKML SQL\update-download-progress.json</c> a few times a second while it runs
    /// and deleted when it ends. The shell's download window reads it to show the bytes, the
    /// speed and the time left — the download used to show only an endless bar, which on a slow
    /// connection looked like nothing was happening at all.
    /// </summary>
    public class UpdateDownloadProgress
    {
        /// <summary>The version being downloaded (matches <see cref="UpdateResult.Version"/>).</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>One of <see cref="UpdateDownloadPhases"/>.</summary>
        public string Phase { get; set; } = UpdateDownloadPhases.Connecting;

        /// <summary>Bytes of the installer received so far.</summary>
        public long BytesReceived { get; set; }

        /// <summary>The installer's size from the server's Content-Length; <c>null</c> when not sent.</summary>
        public long? TotalBytes { get; set; }

        /// <summary>UTC time of this snapshot.</summary>
        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>The phases of <see cref="UpdateDownloadProgress"/>.</summary>
    public static class UpdateDownloadPhases
    {
        /// <summary>Request sent; waiting for the download server to answer.</summary>
        public const string Connecting = "connecting";

        /// <summary>Installer bytes arriving.</summary>
        public const string Downloading = "downloading";

        /// <summary>All bytes in; checking the checksum and moving the file into place.</summary>
        public const string Verifying = "verifying";
    }

    /// <summary>
    /// Reads and writes <see cref="UpdateDownloadProgress"/>. Writes are atomic (temp file, then
    /// rename) so the shell never reads half a file; reads never throw — a missing, locked or
    /// half-replaced file just reads as "no progress yet".
    /// </summary>
    public static class UpdateDownloadProgressStore
    {
        public static void Save(UpdateDownloadProgress progress, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(progress, UpdateJsonContext.Default.UpdateDownloadProgress);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
#if NETSTANDARD2_0
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

        public static UpdateDownloadProgress? TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return JsonSerializer.Deserialize(reader.ReadToEnd(), UpdateJsonContext.Default.UpdateDownloadProgress);
            }
            catch (Exception)
            {
                return null; // being replaced or removed right now: the next poll reads it
            }
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                if (File.Exists(path + ".tmp"))
                {
                    File.Delete(path + ".tmp");
                }
            }
            catch (Exception)
            {
                // Best effort: a stale progress file is ignored once its version is not offered.
            }
        }
    }
}
