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

        /// <summary>
        /// True only for linked-server suggestions (set by <c>ObjectProvider.ToLinkedServerItem</c>).
        /// Explicit discriminator for the truncation pinning in <c>CompletionEngine</c> —
        /// <see cref="ObjectType"/> cannot serve: <see cref="CompletionObjectType.Database"/> is also
        /// used for USE-clause database suggestions and is purely an icon hint for the hosts.
        /// Additive/back-compatible: older peers that omit this key deserialize to <c>false</c>.
        /// </summary>
        [Key(6)]
        public bool IsLinkedServer { get; set; }
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
        Parameter = 11,
        /// <summary>
        /// A contextual smart action (e.g. the SQL-Prompt-style "▶ Add columns from SELECT"
        /// GROUP BY action) whose InsertText is literal text to drop in on accept. Distinct
        /// from <see cref="Snippet"/> so SSMS commits it as a literal insert (not a snippet
        /// shortcode lookup) and never hides it behind the snippet-visibility toggle.
        /// </summary>
        SmartAction = 12
    }
}
