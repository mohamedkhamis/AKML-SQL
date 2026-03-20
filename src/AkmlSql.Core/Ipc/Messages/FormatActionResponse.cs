using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatActionResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string FormattedText { get; set; } = string.Empty;

        [Key(2)]
        public bool WasModified { get; set; }

        [Key(3)]
        public long ElapsedMs { get; set; }

        [Key(4)]
        public string? ErrorMessage { get; set; }
    }
}
