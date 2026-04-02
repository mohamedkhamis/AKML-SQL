using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class SnippetExpandRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string Shortcode { get; set; } = string.Empty;

        [Key(2)]
        public int CursorOffset { get; set; }

        [Key(3)]
        public string SelectedText { get; set; } = string.Empty;

        [Key(4)]
        public bool FormatOnExpand { get; set; }

        [Key(5)]
        public string ProfileName { get; set; } = string.Empty;

        [Key(6)]
        public string ClipboardText { get; set; } = string.Empty;
    }
}
