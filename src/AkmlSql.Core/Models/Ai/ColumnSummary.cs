namespace AkmlSql.Core.Models.Ai
{
    /// <summary>
    /// Summary of a table or view column for inclusion in AI prompts.
    /// </summary>
    public class ColumnSummary
    {
        /// <summary>Column name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>SQL data type (e.g. "int", "nvarchar(100)", "decimal(18,2)").</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Whether the column allows NULL values.</summary>
        public bool IsNullable { get; set; }

        /// <summary>Whether the column is part of the primary key.</summary>
        public bool IsPrimaryKey { get; set; }

        /// <summary>
        /// Foreign key target in "schema.table.column" format, or null if not a FK column.
        /// </summary>
        public string? ForeignKeyTarget { get; set; }

        /// <summary>Extended property description (MS_Description). Null when not available.</summary>
        public string? Description { get; set; }
    }
}
