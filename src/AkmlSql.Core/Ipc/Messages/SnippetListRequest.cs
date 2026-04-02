using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class SnippetListRequest
    {
        [Key(0)]
        public string Query { get; set; } = string.Empty;

        [Key(1)]
        public string? Context { get; set; }

        [Key(2)]
        public bool HasSelection { get; set; }

        [Key(3)]
        public int SourceFilter { get; set; } // 0=All, 1=Personal, 2=Team, 3=BuiltIn

        [Key(4)]
        public string? CategoryFilter { get; set; }
    }
}
