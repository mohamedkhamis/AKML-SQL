namespace AkmlSql.Core.Update
{
    /// <summary>
    /// JSON manifest fetched from <c>https://updates.akmlsql.com/manifest.json</c> by the Updater process.
    /// The Updater compares <see cref="Version"/> against the running extension version and writes
    /// an <see cref="UpdateResult"/> file if a newer version is available.
    /// </summary>
    public class UpdateManifest
    {
        /// <summary>Latest available version as a SemVer string (e.g. <c>"1.2.3"</c>).</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Direct download URL for the installer of the latest version.</summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>URL for the release notes page or changelog.</summary>
        public string ReleaseNotesUrl { get; set; } = string.Empty;

        /// <summary>Minimum Windows version required to run this release (e.g. <c>"10.0"</c>).</summary>
        public string MinimumOsVersion { get; set; } = string.Empty;

        /// <summary>SHA-256 hex digest of the installer binary for integrity verification.</summary>
        public string Sha256Hash { get; set; } = string.Empty;
    }
}
