using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileImportResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public int MappedOptionsCount { get; set; }

        [Key(2)]
        public int UnmappedOptionsCount { get; set; }

        [Key(3)]
        public string[]? UnmappedOptions { get; set; }

        [Key(4)]
        public string? ErrorMessage { get; set; }

        /// <summary>Spec 031 FR-007 — per-option classifications. Null from pre-031 engines.</summary>
        [Key(5)]
        public ProfileImportOptionReport[]? OptionReports { get; set; }

        /// <summary>
        /// Spec 031 — the final saved profile name (post target-name override, if any). The shell
        /// needs this to select/activate the freshly-imported profile without re-deriving it from
        /// the request. Null on failure or from pre-031 engines.
        /// </summary>
        [Key(6)]
        public string? ProfileName { get; set; }
    }
}
