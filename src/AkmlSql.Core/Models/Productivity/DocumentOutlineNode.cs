#nullable enable
using System.Collections.Generic;

namespace AkmlSql.Core.Models.Productivity
{
    /// <summary>Type of structural element in the document outline.</summary>
    public enum OutlineNodeType
    {
        Procedure,
        Function,
        CTE,
        TempTable,
        Statement,
        Region,
        Block,
        Trigger,
        View
    }

    /// <summary>A structural element in the script outline tree.</summary>
    public class DocumentOutlineNode
    {
        /// <summary>Display name (e.g., "sp_GetOrders", "CTE: OrderTotals").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Type of this node.</summary>
        public OutlineNodeType NodeType { get; set; }

        /// <summary>0-based line number in the script (matches VS editor and DTO convention).</summary>
        public int StartLine { get; set; }

        /// <summary>Character offset from script start.</summary>
        public int StartOffset { get; set; }

        /// <summary>Character offset of node end.</summary>
        public int EndOffset { get; set; }

        /// <summary>Depth in the tree hierarchy.</summary>
        public int NestingLevel { get; set; }

        /// <summary>Child nodes (nested blocks, statements within procedures).</summary>
        public List<DocumentOutlineNode> Children { get; set; } = [];
    }
}
