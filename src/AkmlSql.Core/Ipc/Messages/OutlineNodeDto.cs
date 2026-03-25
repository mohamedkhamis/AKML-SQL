#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// DTO representing a structural element in the document outline tree.
    /// Used in <see cref="DocumentOutlineResponse"/>.
    /// </summary>
    [MessagePackObject]
    public class OutlineNodeDto
    {
        /// <summary>Display name (e.g. "sp_GetOrders", "CTE: OrderTotals", "--region Reports").</summary>
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Type of this node: "Procedure", "Function", "View", "Trigger",
        /// "CTE", "TempTable", "Block", "Region", "Statement".
        /// </summary>
        [Key(1)]
        public string NodeType { get; set; } = string.Empty;

        /// <summary>0-based start line number in the script.</summary>
        [Key(2)]
        public int StartLine { get; set; }

        /// <summary>0-based character offset of node start.</summary>
        [Key(3)]
        public int StartOffset { get; set; }

        /// <summary>0-based character offset of node end (exclusive).</summary>
        [Key(4)]
        public int EndOffset { get; set; }

        /// <summary>Child nodes (nested blocks, statements within procedures).</summary>
        [Key(5)]
        public OutlineNodeDto[] Children { get; set; } = [];
    }
}
