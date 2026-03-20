using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileImportRequest
    {
        [Key(0)]
        public string SourceFormat { get; set; } = string.Empty;

        [Key(1)]
        public byte[] FileContent { get; set; } = [];

        [Key(2)]
        public string? TargetProfileName { get; set; }
    }
}
