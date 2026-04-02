using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class CompletionResponse
    {
        [Key(0)]
        public CompletionItem[] Items { get; set; } = [];

        [Key(1)]
        public bool IsIncomplete { get; set; }
    }

    [MessagePackObject]
    public class CompletionItem
    {
        [Key(0)]
        public string DisplayText { get; set; } = string.Empty;

        [Key(1)]
        public string InsertText { get; set; } = string.Empty;

        [Key(2)]
        public int ObjectType { get; set; } // CompletionObjectType enum

        [Key(3)]
        public string SecondaryText { get; set; } = string.Empty;

        [Key(4)]
        public string SourceObject { get; set; } = string.Empty;

        [Key(5)]
        public int SortPriority { get; set; }
    }

    public enum CompletionObjectType
    {
        Table = 0,
        View = 1,
        Column = 2,
        Keyword = 3,
        Snippet = 4,
        Function = 5,
        Procedure = 6,
        Schema = 7,
        Database = 8,
        Variable = 9,
        Alias = 10,
        Parameter = 11
    }
}
