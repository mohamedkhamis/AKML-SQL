using System;

namespace AkmlSql.Core.Update
{
    /// <summary>
    /// Written by the Updater process to <c>%AppData%\AKML SQL\update-available.json</c>
    /// after comparing the remote <see cref="UpdateManifest"/> against the installed version.
    /// The shell extension reads this file on next startup to decide whether to show the update dialog.
    /// </summary>
    public class UpdateResult
    {
        /// <summary><c>true</c> when a newer version than the installed one was found.</summary>
        public bool Available { get; set; }

        /// <summary>Version string from the remote manifest (e.g. <c>"1.2.3"</c>).</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Direct download URL for the new installer.</summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>URL for the release notes or changelog.</summary>
        public string ReleaseNotesUrl { get; set; } = string.Empty;

        /// <summary>SHA-256 hex digest of the new installer for the shell to display to the user.</summary>
        public string Sha256Hash { get; set; } = string.Empty;

        /// <summary>UTC timestamp when the update check completed.</summary>
        public DateTimeOffset CheckedAt { get; set; }

        /// <summary>
        /// Absolute path of the downloaded installer, set only after its SHA-256 matched the
        /// manifest value (FR-039/FR-040). Canonicalised with <c>Path.GetFullPath()</c> before
        /// the shell launches it (data-model V18). <c>null</c> until verified.
        /// </summary>
        public string? VerifiedInstallerPath { get; set; }

        /// <summary>
        /// Download lifecycle state (FR-039a): one of <see cref="UpdateDownloadStates.None"/>,
        /// <see cref="UpdateDownloadStates.Downloading"/>, <see cref="UpdateDownloadStates.Verified"/>
        /// or <see cref="UpdateDownloadStates.Failed"/>.
        /// </summary>
        public string DownloadState { get; set; } = UpdateDownloadStates.None;

        /// <summary>
        /// Why the download failed (e.g. <c>"checksum mismatch"</c>); populated when
        /// <see cref="DownloadState"/> is <see cref="UpdateDownloadStates.Failed"/> and surfaced
        /// only for a manual check (FR-041). <c>null</c> otherwise.
        /// </summary>
        public string? FailureReason { get; set; }
    }
}
