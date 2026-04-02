using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatSelectionResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string FormattedText { get; set; } = string.Empty;

        [Key(2)]
        public int OriginalStart { get; set; }

        [Key(3)]
        public int OriginalEnd { get; set; }

        [Key(4)]
        public bool WasModified { get; set; }

        [Key(5)]
        public bool ValidationPassed { get; set; }

        [Key(6)]
        public long ElapsedMs { get; set; }
    }
}
