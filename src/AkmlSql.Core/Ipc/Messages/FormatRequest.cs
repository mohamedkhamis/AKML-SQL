using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string Text { get; set; } = string.Empty;

        [Key(2)]
        public string? ProfileName { get; set; }

        [Key(3)]
        public byte[]? ProfileOverrides { get; set; }

        /// <summary>Never read; kept in place so key 4 is never reused.</summary>
        [Key(4)]
        public int[]? IncludeActions { get; set; }

        /// <summary>
        /// Spec 040 (STY-11) — the interactive Format SQL actions. Null = use the style's own
        /// format actions, as today (CLI, bulk format, the web edition and older shells).
        /// </summary>
        [Key(5)]
        public FormatSqlActionsDto? Actions { get; set; }

        /// <summary>
        /// Spec 042 — the style as <c>.akmlstyle</c> JSON, for a PostgreSQL session whose style lives in the web edition's browser store.
        /// When present it is used instead of <see cref="ProfileName"/>. Older peers ignore it.
        /// </summary>
        [Key(6)]
        public string? ProfileJson { get; set; }
    }
}
