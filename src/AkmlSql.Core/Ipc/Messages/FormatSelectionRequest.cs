using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatSelectionRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string Text { get; set; } = string.Empty;

        [Key(2)]
        public int SelectionStart { get; set; }

        [Key(3)]
        public int SelectionEnd { get; set; }

        [Key(4)]
        public string? ProfileName { get; set; }

        /// <summary>
        /// Spec 040 (STY-11) — the interactive Format SQL actions. Null = use the style's own
        /// format actions, as today.
        /// </summary>
        [Key(5)]
        public FormatSqlActionsDto? Actions { get; set; }
    }
}
