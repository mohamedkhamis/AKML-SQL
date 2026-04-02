using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class BulkFormatReportResponse
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public int TotalFiles { get; set; }

        [Key(2)]
        public int SuccessCount { get; set; }

        [Key(3)]
        public int FailedCount { get; set; }

        [Key(4)]
        public int SkippedCount { get; set; }

        [Key(5)]
        public long ElapsedMs { get; set; }

        [Key(6)]
        public FileResult[] Results { get; set; } = [];
    }
}
