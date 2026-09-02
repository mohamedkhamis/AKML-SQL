using System.Text;
using AkmlSql.Core.Models.Ai;

namespace AkmlSql.Engine.Ai.Context;

/// <summary>
/// Formats a <see cref="SchemaContext"/> into compact DDL-like text suitable for AI prompt injection.
/// Spec 036 (US1) rendering: a level-1 line for every object in the kept inventory, a full
/// detail block (columns, PK, indexes, FK lines; descriptions at level 4) for every object in
/// <see cref="SchemaContext.DetailedObjectNames"/>, an explicit truncation notice when
/// <see cref="SchemaContext.Truncated"/> (FR-026), and two distinguishable empty states
/// (FR-028): no database connection vs connected-but-no-visible-objects.
/// Uses abbreviations (PK, FK, IX, NVARCHAR) to keep output compact.
/// </summary>
public static class SchemaContextFormatter
{
    /// <summary>
    /// Formats the given <see cref="SchemaContext"/> into a compact DDL-like text block.
    /// </summary>
    /// <param name="context">The schema context to format.</param>
    /// <returns>A formatted string representation of the schema context.</returns>
    public static string Format(SchemaContext context)
    {
        // FR-028: unbound (no connection) must be distinguishable from connected-but-empty.
        if (string.IsNullOrEmpty(context.DatabaseName))
        {
            return "No database connection. The user has not connected a SQL editor to a database. " +
                   "Do not answer schema questions from assumption; tell the user to connect a query " +
                   "window to a database first.";
        }

        if (context.Objects.Count == 0)
        {
            // The login may simply be unable to see the objects — never claim "empty database".
            return $"Database: {context.DatabaseName}\n" +
                   "(No schema objects are visible for this database — it may be empty, its schema " +
                   "may still be loading, or the connected login may not have permission to see them. " +
                   "Do not claim the database is empty.)";
        }

        var detailedNames = context.DetailedObjectNames;
        var detailed = new List<SchemaObjectSummary>();
        var inventoryOnly = new List<SchemaObjectSummary>();
        foreach (var obj in context.Objects)
        {
            if (detailedNames.Contains($"{obj.Schema}.{obj.Name}"))
                detailed.Add(obj);
            else
                inventoryOnly.Add(obj);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Database: {context.DatabaseName}");

        // FR-026: explicit truncation notice the model can quote.
        if (context.Truncated)
        {
            sb.AppendLine($"NOTE: showing {context.Objects.Count:N0} of {context.TotalObjectCount:N0} " +
                          "objects in this database. The inventory below is incomplete.");
        }

        // Level-1 inventory lines (names grouped by type with row counts) for non-promoted objects.
        AppendInventorySection(sb, inventoryOnly);

        // Full detail blocks for promoted objects.
        if (detailed.Count > 0)
        {
            sb.AppendLine();
            foreach (var obj in detailed)
            {
                FormatObjectLevel2(sb, obj);
                FormatObjectDetailLines(sb, obj, includeDescription: context.CompressionLevel >= 4);
            }
        }

        AppendFkSummarySection(sb, context);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Inventory section: names only, grouped by type with approximate row counts.
    /// Example:
    /// <code>
    /// Tables: dbo.Orders (~50K rows), dbo.Customers (~500 rows)
    /// Views: dbo.OrderSummary
    /// </code>
    /// </summary>
    private static void AppendInventorySection(StringBuilder sb, List<SchemaObjectSummary> objects)
    {
        var groups = objects
            .GroupBy(o => o.Type)
            .OrderBy(g => TypeSortOrder(g.Key));

        foreach (var group in groups)
        {
            var typePlural = PluralizeType(group.Key);
            var items = group.Select(o => FormatObjectNameWithRows(o)).ToList();
            sb.AppendLine($"{typePlural}: {string.Join(", ", items)}");
        }
    }

    /// <summary>
    /// Formats an object with inline column definitions.
    /// </summary>
    private static void FormatObjectLevel2(StringBuilder sb, SchemaObjectSummary obj)
    {
        sb.Append($"{obj.Schema}.{obj.Name}");

        if (obj.Columns is { Count: > 0 })
        {
            sb.Append('(');
            for (int i = 0; i < obj.Columns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var col = obj.Columns[i];
                sb.Append($"{col.Name} {col.Type}");

                if (col.IsPrimaryKey) sb.Append(" PK");
                if (!col.IsNullable && !col.IsPrimaryKey) sb.Append(" NOT NULL");

                if (!string.IsNullOrEmpty(col.ForeignKeyTarget))
                {
                    // Extract just the table reference for compact display: "dbo.Customers.CustomerId" -> "FK->dbo.Customers"
                    var fkTarget = col.ForeignKeyTarget;
                    var lastDot = fkTarget.LastIndexOf('.');
                    if (lastDot > 0)
                    {
                        sb.Append($" FK->{fkTarget[..lastDot]}");
                    }
                    else
                    {
                        sb.Append($" FK->{fkTarget}");
                    }
                }
            }
            sb.Append(')');
        }

        if (obj.ApproxRowCount > 0)
        {
            sb.Append($"  ~{FormatRowCount(obj.ApproxRowCount)} rows");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Formats PK, IX, FK, and optionally Description detail lines indented under the object.
    /// </summary>
    private static void FormatObjectDetailLines(StringBuilder sb, SchemaObjectSummary obj, bool includeDescription)
    {
        // PK line
        if (obj.PrimaryKey is { Count: > 0 })
        {
            sb.AppendLine($"  PK: {string.Join(", ", obj.PrimaryKey)}");
        }

        // IX lines
        if (obj.Indexes is { Count: > 0 })
        {
            // Format index names with their columns from the FK/column data
            sb.AppendLine($"  IX: {string.Join(", ", obj.Indexes)}");
        }

        // FK lines from column-level FK targets
        if (obj.Columns != null)
        {
            var fkCols = obj.Columns.Where(c => !string.IsNullOrEmpty(c.ForeignKeyTarget)).ToList();
            foreach (var col in fkCols)
            {
                sb.AppendLine($"  FK: {col.Name} -> {col.ForeignKeyTarget}");
            }
        }

        // Description — sanitize to prevent prompt injection via extended properties
        if (includeDescription && !string.IsNullOrEmpty(obj.Description))
        {
            var sanitized = obj.Description
                .Replace("\r", " ").Replace("\n", " ");
            if (sanitized.Length > 200)
                sanitized = sanitized[..200] + "...";
            sb.AppendLine($"  Desc: {sanitized}");
        }
    }

    /// <summary>
    /// Appends the FK relationship section if there are any FK summaries.
    /// </summary>
    private static void AppendFkSummarySection(StringBuilder sb, SchemaContext context)
    {
        if (context.ForeignKeys.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Relationships:");
        foreach (var fk in context.ForeignKeys)
        {
            sb.AppendLine($"  {fk.ParentTable}.{fk.ParentColumn} -> {fk.ReferencedTable}.{fk.ReferencedColumn}");
        }
    }

    /// <summary>
    /// Formats an object name with approximate row count for Level 1 display.
    /// </summary>
    private static string FormatObjectNameWithRows(SchemaObjectSummary obj)
    {
        if (obj.ApproxRowCount > 0)
        {
            return $"{obj.Schema}.{obj.Name} (~{FormatRowCount(obj.ApproxRowCount)} rows)";
        }

        return $"{obj.Schema}.{obj.Name}";
    }

    /// <summary>
    /// Formats a row count into a compact human-readable string (e.g. 1.2K, 3.5M, 1.1B).
    /// </summary>
    private static string FormatRowCount(long count)
    {
        return count switch
        {
            >= 1_000_000_000 => $"{count / 1_000_000_000.0:0.#}B",
            >= 1_000_000 => $"{count / 1_000_000.0:0.#}M",
            >= 1_000 => $"{count / 1_000.0:0.#}K",
            _ => count.ToString()
        };
    }

    /// <summary>Returns a sort order for object types to group Tables first, then Views, etc.</summary>
    private static int TypeSortOrder(string type)
    {
        return type switch
        {
            "Table" => 0,
            "View" => 1,
            "Procedure" => 2,
            "Function" => 3,
            "Synonym" => 4,
            "Sequence" => 5,
            _ => 9
        };
    }

    /// <summary>Returns the plural form of an object type string.</summary>
    private static string PluralizeType(string type)
    {
        return type switch
        {
            "Table" => "Tables",
            "View" => "Views",
            "Procedure" => "Procedures",
            "Function" => "Functions",
            "Synonym" => "Synonyms",
            "Sequence" => "Sequences",
            _ => type + "s"
        };
    }
}
