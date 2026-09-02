using System;
using System.Collections.Generic;

namespace AkmlSql.Core.Models.Ai
{
    /// <summary>
    /// Compressed schema context sent to AI providers alongside prompts.
    /// Contains database object summaries filtered and compressed to fit within token budgets.
    /// </summary>
    public class SchemaContext
    {
        /// <summary>
        /// Name of the database this context was built from. Empty when the request had no
        /// bound connection — the "no database connection" signal (FR-028); the formatter
        /// renders that state distinctly from a connected-but-empty database.
        /// </summary>
        public string DatabaseName { get; set; } = string.Empty;

        /// <summary>
        /// Requested detail level (1–4): 1 = names/row counts only (ghost text),
        /// 2 = add columns, 3 = add PK/indexes/FK detail, 4 = add descriptions.
        /// Inventory objects are always rendered at level 1; prompt-relevant objects and their
        /// FK 1-hop neighbours are promoted to the requested level (see
        /// <see cref="DetailedObjectNames"/>).
        /// </summary>
        public int CompressionLevel { get; set; } = 2;

        /// <summary>Database objects included in this context.</summary>
        public List<SchemaObjectSummary> Objects { get; set; } = new();

        /// <summary>Foreign key relationships between tables.</summary>
        // ReSharper disable once UnusedMember.Global
        public List<FkSummary> ForeignKeys { get; set; } = new();

        /// <summary>
        /// True when the database inventory exceeded the object budget and was truncated
        /// (FR-026). The formatter emits an explicit "showing N of M" notice; silent
        /// truncation is a defect.
        /// </summary>
        public bool Truncated { get; set; }

        /// <summary>Total number of objects in the database cache before budget truncation.</summary>
        public int TotalObjectCount { get; set; }

        /// <summary>
        /// Full names ("schema.name", case-insensitive) of the objects promoted to full detail
        /// because the prompt named them (or they are FK 1-hop neighbours of a named object).
        /// Empty on the level-1 latency path (ghost text).
        /// </summary>
        public HashSet<string> DetailedObjectNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Estimated token count of this context when serialized for an AI prompt.</summary>
        // ReSharper disable once UnusedMember.Global
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
        // ReSharper disable once UnusedMember.Global
        public string ReferencedColumn { get; set; } = string.Empty;
    }
}
