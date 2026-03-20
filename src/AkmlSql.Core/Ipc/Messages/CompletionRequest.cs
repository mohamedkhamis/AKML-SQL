using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class CompletionRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public int CursorOffset { get; set; }

        [Key(2)]
        public int TriggerKind { get; set; } // 0=Auto, 1=Manual, 2=AfterDot
    }
}
