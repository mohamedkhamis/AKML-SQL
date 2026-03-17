using System;

namespace AkmlSql.Core.Update
{
    public class UpdateResult
    {
        public bool Available { get; set; }
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotesUrl { get; set; } = string.Empty;
        public DateTimeOffset CheckedAt { get; set; }
    }
}
