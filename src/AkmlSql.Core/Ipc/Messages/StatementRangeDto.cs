#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// DTO representing a single SQL statement's location within a script.
    /// Used in <see cref="StatementBoundaryResponse"/>.
    /// </summary>
    [MessagePackObject]
    public class StatementRangeDto
    {
        /// <summary>0-based character offset of the statement start.</summary>
        [Key(0)]
        public int StartOffset { get; set; }

        /// <summary>0-based character offset of the statement end (exclusive).</summary>
        [Key(1)]
        public int EndOffset { get; set; }

        /// <summary>0-based start line number.</summary>
        [Key(2)]
        public int StartLine { get; set; }

        /// <summary>0-based end line number.</summary>
        [Key(3)]
        public int EndLine { get; set; }

        /// <summary>
        /// Statement type name derived from the AST node (e.g. "SelectStatement",
        /// "InsertStatement", "CreateProcedureStatement").
        /// </summary>
        [Key(4)]
        public string StatementType { get; set; } = string.Empty;
    }
}
