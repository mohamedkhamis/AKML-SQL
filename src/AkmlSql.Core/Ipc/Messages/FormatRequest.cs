using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string Text { get; set; } = string.Empty;

        [Key(2)]
        public string? ProfileName { get; set; }

        [Key(3)]
        public byte[]? ProfileOverrides { get; set; }

        [Key(4)]
        public int[]? IncludeActions { get; set; }
    }
}
