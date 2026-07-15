using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 031 FR-007 — one imported style option's classification.
    /// Status is one of: "mapped", "mapped-pending-render", "unsupported", "unknown".
    /// </summary>
    [MessagePackObject]
    public class ProfileImportOptionReport
    {
        [Key(0)]
        public string Path { get; set; } = string.Empty;

        [Key(1)]
        public string Value { get; set; } = string.Empty;

        [Key(2)]
        public string Status { get; set; } = string.Empty;

        [Key(3)]
        public string? Reason { get; set; }
    }
}
