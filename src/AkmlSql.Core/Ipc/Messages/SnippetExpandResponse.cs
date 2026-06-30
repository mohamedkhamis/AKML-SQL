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

        // Spec 030 T040/T047 — selection-range markers ($SELECTIONSTART$/$SELECTIONEND$).
        // Offsets into ExpandedText after the markers are stripped; -1 when the marker was absent.
        [Key(6)]
        public int SelectionStartOffset { get; set; } = -1;

        [Key(7)]
        public int SelectionEndOffset { get; set; } = -1;
    }
}
