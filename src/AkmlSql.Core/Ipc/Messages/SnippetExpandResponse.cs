using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class SnippetExpandResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string ExpandedText { get; set; } = string.Empty;

        [Key(2)]
        public PlaceholderInfo[] Placeholders { get; set; } = [];

        [Key(3)]
        public int CursorOffset { get; set; } = -1;

        [Key(4)]
        public bool WasFormatted { get; set; }

        [Key(5)]
        public string? ErrorMessage { get; set; }
    }
}
