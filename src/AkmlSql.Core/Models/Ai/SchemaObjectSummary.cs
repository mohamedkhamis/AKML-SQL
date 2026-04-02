using System.Collections.Generic;

namespace AkmlSql.Core.Models.Ai
{
    /// <summary>
    /// Summary of a database object for inclusion in AI prompts.
    /// Detail level depends on <see cref="SchemaContext.CompressionLevel"/>.
    /// </summary>
    public class SchemaObjectSummary
    {
        /// <summary>Schema name (e.g. "dbo").</summary>
        public string Schema { get; set; } = string.Empty;

        /// <summary>Object name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Object type: "Table", "View", "Procedure", or "Function".</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Approximate row count (from sys.dm_db_partition_stats or sys.sysindexes).</summary>
        public long ApproxRowCount { get; set; }

        /// <summary>Column definitions. Null when compression level excludes columns.</summary>
        public List<ColumnSummary>? Columns { get; set; }

        /// <summary>Primary key column names. Null when not available.</summary>
        public List<string>? PrimaryKey { get; set; }

        /// <summary>Index names on this object. Null when compression level excludes indexes.</summary>
        public List<string>? Indexes { get; set; }

        /// <summary>Extended property description (MS_Description). Null when not available or excluded.</summary>
        public string? Description { get; set; }
    }
}
