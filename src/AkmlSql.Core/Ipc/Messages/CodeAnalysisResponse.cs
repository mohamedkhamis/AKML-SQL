using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class CodeAnalysisResponse
    {
        [Key(0)] public string RequestId { get; set; } = string.Empty;
        [Key(1)] public CodeIssueInfo[] Issues { get; set; } = [];
        [Key(2)] public int AnalyzedVersion { get; set; }
    }
}
