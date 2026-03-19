using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class DocumentChange
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public int ChangeType { get; set; } // 0=Full, 1=Incremental

        [Key(2)]
        public string? FullText { get; set; }

        [Key(3)]
        public TextChange[]? Changes { get; set; }
    }

    [MessagePackObject]
    public class TextChange
    {
        [Key(0)]
        public int Offset { get; set; }

        [Key(1)]
        public int OldLength { get; set; }

        [Key(2)]
        public string NewText { get; set; } = string.Empty;
    }
}
