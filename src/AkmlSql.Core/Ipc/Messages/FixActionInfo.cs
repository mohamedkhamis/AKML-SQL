using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FixActionInfo
    {
        [Key(0)] public string Label { get; set; } = string.Empty;
        [Key(1)] public int FixType { get; set; }         // 0=Transform,1=Insert,2=Remove,3=Suppress
        [Key(2)] public int ReplacementStart { get; set; }
        [Key(3)] public int ReplacementEnd { get; set; }
        [Key(4)] public string ReplacementText { get; set; } = string.Empty;
        [Key(5)] public string? SuppressRuleId { get; set; }
        [Key(6)] public int? SuppressScopeCode { get; set; } // 0=Line,1=File,2=Global
    }
}
