#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// MessagePack-serializable DTO representing a single tab snapshot for IPC transfer.
    /// Maps 1:1 to <see cref="AkmlSql.Core.Models.Tabs.TabSnapshot"/> but uses positional
    /// <see cref="KeyAttribute"/> keys for compact wire encoding.
    /// </summary>
    [MessagePackObject]
    public class TabSnapshotDto
    {
        /// <summary>Zero-based index of the tab in the document well.</summary>
        [Key(0)]
        public int TabIndex { get; set; }

        /// <summary>Display title of the tab.</summary>
        [Key(1)]
        public string? Title { get; set; }

        /// <summary>Full text content of the editor.</summary>
        [Key(2)]
        public string? Content { get; set; }

        /// <summary>Absolute file path if saved to disk.</summary>
        [Key(3)]
        public string? FilePath { get; set; }

        /// <summary>SQL Server instance name. No passwords.</summary>
        [Key(4)]
        public string? Server { get; set; }

        /// <summary>Database name.</summary>
        [Key(5)]
        public string? Database { get; set; }

        /// <summary>Authentication type (e.g. "Windows", "SqlAuth"). No passwords.</summary>
        [Key(6)]
        public string? AuthType { get; set; }

        /// <summary>Zero-based cursor line position.</summary>
        [Key(7)]
        public int CursorLine { get; set; }

        /// <summary>Zero-based cursor column position.</summary>
        [Key(8)]
        public int CursorColumn { get; set; }

        /// <summary>Whether the tab was pinned.</summary>
        [Key(9)]
        public bool IsPinned { get; set; }
    }
}
