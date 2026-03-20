using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatDiagnosticInfo
    {
        [Key(0)]
        public int Severity { get; set; }

        [Key(1)]
        public string Message { get; set; } = string.Empty;

        [Key(2)]
        public int Offset { get; set; }

        [Key(3)]
        public int Line { get; set; }
    }
}
