using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatPreviewRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string SampleText { get; set; } = string.Empty;

        [Key(2)]
        public string ProfileJson { get; set; } = string.Empty;
    }
}
