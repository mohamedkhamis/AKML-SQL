using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class CodeIssueInfo
    {
        [Key(0)] public string RuleId { get; set; } = string.Empty;
        [Key(1)] public int Severity { get; set; }   // 0=Hint,1=Info,2=Warning,3=Error
        [Key(2)] public string Message { get; set; } = string.Empty;
        [Key(3)] public int StartOffset { get; set; }
        [Key(4)] public int EndOffset { get; set; }
        [Key(5)] public int Line { get; set; }
        [Key(6)] public int Column { get; set; }
        [Key(7)] public FixActionInfo[] FixActions { get; set; } = [];
    }
}
