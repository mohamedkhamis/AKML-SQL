using System.Text;
using AkmlSql.Core.Models.Ai;

namespace AkmlSql.Engine.Ai.Context;

/// <summary>
/// Formats a <see cref="SchemaContext"/> into compact DDL-like text suitable for AI prompt injection.
/// Output detail is controlled by <see cref="SchemaContext.CompressionLevel"/>:
/// <list type="bullet">
///   <item>Level 1: Names only — grouped by type with row counts</item>
///   <item>Level 2: Inline column definitions with FK annotations</item>
///   <item>Level 3: PK, index, and FK detail lines</item>
///   <item>Level 4: Same as Level 3 plus Description lines from extended properties</item>
/// </list>
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
        if (context.Objects.Count == 0)
        {
            return $"Database: {context.DatabaseName}\n(No schema objects available)";
        }

        return context.CompressionLevel switch
        {
            <= 1 => FormatLevel1(context),
            2 => FormatLevel2(context),
            3 => FormatLevel3(context),
            _ => FormatLevel4(context)
        };
    }

    /// <summary>
    /// Level 1: Names only, grouped by type with approximate row counts.
    /// Example:
    /// <code>
    /// Database: Northwind
    /// Tables: dbo.Orders (~50K rows), dbo.Customers (~500 rows)
    /// Views: dbo.OrderSummary
    /// </code>
    /// </summary>
    private static string FormatLevel1(SchemaContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Database: {context.DatabaseName}");

        var groups = context.Objects
            .GroupBy(o => o.Type)
            .OrderBy(g => TypeSortOrder(g.Key));

        foreach (var group in groups)
        {
            var typePlural = PluralizeType(group.Key);
            var items = group.Select(o => FormatObjectNameWithRows(o)).ToList();
            sb.AppendLine($"{typePlural}: {string.Join(", ", items)}");
        }

        AppendFkSummarySection(sb, context);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Level 2: Inline column definitions with FK annotations.
    /// Example:
    /// <code>
    /// dbo.Orders(OrderId INT PK, CustomerId INT FK->dbo.Customers, OrderDate DATE, Total DECIMAL(18,2))
    /// </code>
    /// </summary>
    private static string FormatLevel2(SchemaContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Database: {context.DatabaseName}");
        sb.AppendLine();

        foreach (var obj in context.Objects)
        {
            FormatObjectLevel2(sb, obj);
        }

        AppendFkSummarySection(sb, context);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Level 3: Column definitions plus PK, index, and FK detail lines.
    /// </summary>
    private static string FormatLevel3(SchemaContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Database: {context.DatabaseName}");
        sb.AppendLine();

        foreach (var obj in context.Objects)
        {
            FormatObjectLevel2(sb, obj);
            FormatObjectDetailLines(sb, obj, includeDescription: false);
        }

        AppendFkSummarySection(sb, context);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Level 4: Same as Level 3 plus Description lines from extended properties.
    /// </summary>
    private static string FormatLevel4(SchemaContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Database: {context.DatabaseName}");
        sb.AppendLine();

        foreach (var obj in context.Objects)
        {
            FormatObjectLevel2(sb, obj);
            FormatObjectDetailLines(sb, obj, includeDescription: true);
        }

        AppendFkSummarySection(sb, context);
        return sb.ToString().TrimEnd();
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

        // Description
        if (includeDescription && !string.IsNullOrEmpty(obj.Description))
        {
            sb.AppendLine($"  Desc: {obj.Description}");
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
    private static string FormatRowCount(long count) => count switch
    {
        >= 1_000_000_000 => $"{count / 1_000_000_000.0:0.#}B",
        >= 1_000_000 => $"{count / 1_000_000.0:0.#}M",
        >= 1_000 => $"{count / 1_000.0:0.#}K",
        _ => count.ToString()
    };

    /// <summary>Returns a sort order for object types to group Tables first, then Views, etc.</summary>
    private static int TypeSortOrder(string type) => type switch
    {
        "Table" => 0,
        "View" => 1,
        "Procedure" => 2,
        "Function" => 3,
        "Synonym" => 4,
        "Sequence" => 5,
        _ => 9
    };

    /// <summary>Returns the plural form of an object type string.</summary>
    private static string PluralizeType(string type) => type switch
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
