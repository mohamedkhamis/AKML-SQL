using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class BulkFormatRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string[] FilePaths { get; set; } = [];

        [Key(2)]
        public string? ProfileName { get; set; }

        [Key(3)]
        public bool DryRun { get; set; }

        [Key(4)]
        public bool CreateBackups { get; set; }
    }
}
