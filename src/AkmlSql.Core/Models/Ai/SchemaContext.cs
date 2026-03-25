#nullable enable
using System.Collections.Generic;

namespace AkmlSql.Core.Models.Ai
{
    /// <summary>
    /// Compressed schema context sent to AI providers alongside prompts.
    /// Contains database object summaries filtered and compressed to fit within token budgets.
    /// </summary>
    public class SchemaContext
    {
        /// <summary>Name of the database this context was built from.</summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// Compression level used when building this context:
        /// 0 = full (all columns, descriptions), 1 = standard (columns, no descriptions),
        /// 2 = compact (table/column names only), 3 = minimal (table names only).
        /// </summary>
        public int CompressionLevel { get; set; } = 2;

        /// <summary>Database objects included in this context.</summary>
        public List<SchemaObjectSummary> Objects { get; set; } = new();

        /// <summary>Foreign key relationships between tables.</summary>
        public List<FkSummary> ForeignKeys { get; set; } = new();

        /// <summary>Estimated token count of this context when serialized for an AI prompt.</summary>
        public int EstimatedTokens { get; set; }
    }

    /// <summary>
    /// A foreign key relationship summary for AI context.
    /// </summary>
    public class FkSummary
    {
        /// <summary>Fully qualified parent (referencing) table name.</summary>
        public string ParentTable { get; set; } = string.Empty;

        /// <summary>Column in the parent table.</summary>
        public string ParentColumn { get; set; } = string.Empty;

        /// <summary>Fully qualified referenced table name.</summary>
        public string ReferencedTable { get; set; } = string.Empty;

        /// <summary>Column in the referenced table.</summary>
        public string ReferencedColumn { get; set; } = string.Empty;
    }
}
