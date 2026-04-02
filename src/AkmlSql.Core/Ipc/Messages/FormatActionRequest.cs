using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    public enum FormatActionType
    {
        CasingOnly = 0,
        InsertSemicolons = 1,
        RemoveSemicolons = 2,
        ExpandWildcards = 3,
        QualifyObjectNames = 4,
        AddSquareBrackets = 5,
        RemoveSquareBrackets = 6,
        AddAsKeyword = 7,
        RemoveAsKeyword = 8,

        // Phase 6 — Refactoring lightweight operations
        ExpandInsertColumns = 9,
        ExpandExecParameters = 10,
        ExpandUpdateColumns = 11,
        ConvertOldStyleJoins = 12,
        AddGroupByColumns = 13,
        EncapsulateBeginEnd = 14,
        ReplaceDeprecatedSyntax = 15,

        // Phase 10 — SQL Prompt Core Parity remaining gaps
        ConvertSpExecutesql = 16,

        // Phase 12 — SQL History & Final Gaps
        Unformat = 17
    }

    [MessagePackObject]
    public class FormatActionRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string Text { get; set; } = string.Empty;

        [Key(2)]
        public int ActionType { get; set; }

        [Key(3)]
        public string? ProfileName { get; set; }

        /// <summary>
        /// Character offset of selection start (0 = act on full document).
        /// </summary>
        [Key(4)]
        public int SelectionStart { get; set; }

        /// <summary>
        /// Length of selection in characters (0 = act on full document).
        /// </summary>
        [Key(5)]
        public int SelectionLength { get; set; }
    }
}
