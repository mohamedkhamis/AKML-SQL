namespace AkmlSql.Core.Update
{
    /// <summary>
    /// The <see cref="UpdateResult.DownloadState"/> lifecycle values (data-model entity 6,
    /// spec 036 US5 / FR-039a). <c>none</c> is the state of an offer that has not been
    /// downloaded (initial state, and the state a cancelled download returns to).
    /// </summary>
    public static class UpdateDownloadStates
    {
        /// <summary>No download attempted, or a cancelled one was rolled back.</summary>
        public const string None = "none";

        /// <summary>The updater is fetching the installer (<c>.partial</c> may exist).</summary>
        public const string Downloading = "downloading";

        /// <summary>SHA-256 matched the manifest; <c>VerifiedInstallerPath</c> is set.</summary>
        public const string Verified = "verified";

        /// <summary>The download failed; <c>FailureReason</c> says why.</summary>
        public const string Failed = "failed";
    }
}
