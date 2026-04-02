using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class SnippetListResponse
    {
        [Key(0)]
        public SnippetInfo[] Snippets { get; set; } = [];
    }

    [MessagePackObject]
    public class SnippetInfo
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;

        [Key(1)]
        public string Shortcode { get; set; } = string.Empty;

        [Key(2)]
        public string Name { get; set; } = string.Empty;

        [Key(3)]
        public string Description { get; set; } = string.Empty;

        [Key(4)]
        public string Category { get; set; } = string.Empty;

        [Key(5)]
        public int Source { get; set; } // 1=Personal, 2=Team, 3=BuiltIn

        [Key(6)]
        public bool SurroundsWith { get; set; }

        [Key(7)]
        public int UsageCount { get; set; }

        [Key(8)]
        public string[] Tags { get; set; } = [];
    }
}
