using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class BulkFormatProgressInfo
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public int TotalFiles { get; set; }

        [Key(2)]
        public int CompletedFiles { get; set; }

        [Key(3)]
        public string? CurrentFile { get; set; }

        [Key(4)]
        public FileResult? LastResult { get; set; }
    }
}
