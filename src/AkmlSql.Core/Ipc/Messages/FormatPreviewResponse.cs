using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatPreviewResponse
    {
        [Key(0)]
        public string FormattedText { get; set; } = string.Empty;

        [Key(1)]
        public long ElapsedMs { get; set; }
    }
}
