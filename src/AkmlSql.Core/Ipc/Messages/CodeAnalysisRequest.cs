using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class CodeAnalysisRequest
    {
        [Key(0)] public string SessionId { get; set; } = string.Empty;
        [Key(1)] public string RequestId { get; set; } = string.Empty;
        [Key(2)] public string DocumentText { get; set; } = string.Empty;
        [Key(3)] public int DocumentVersion { get; set; }
    }
}
