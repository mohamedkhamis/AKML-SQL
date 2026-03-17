namespace AkmlSql.Core.Update
{
    public class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotesUrl { get; set; } = string.Empty;
        public string MinimumOsVersion { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
    }
}
