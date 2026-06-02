using AkmlSql.Core.Config;
using AkmlSql.Engine.Schema.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Expands INSERT statements that have no column list by adding explicit column names
/// sourced from the schema cache. When <c>InsertColumnsIncludeTypes</c> is enabled,
/// each column receives an inline comment with its data type, nullability, and default.
/// </summary>
public class ExpandInsertColumnsOperation : ILightweightOperation
{
    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            var script = context.Script;
            if (script == null)
                return (context.DocumentText, []);

            var visitor = new InsertStatementCollector();
            script.Accept(visitor);

            if (visitor.Statements.Count == 0)
                return (context.DocumentText, []);

            // Read AppSettings ONCE in the production path — both InsertOptions
            // (gates the operation) and Formatter.InsertColumnsIncludeTypes
            // (controls type comments) come from the same load. Tests can
            // skip disk I/O entirely by pre-populating context.IntelliSense.
            AppSettings? loaded = null;
            if (context.IntelliSense == null)
            {
                try { loaded = ConfigManager.Load(); }
                catch { /* fall through to defaults below */ }
            }

            var insertOptions = context.IntelliSense?.InsertOptions
                ?? loaded?.IntelliSense.InsertOptions
                ?? new InsertOptionsSettings();

            // IntelliSense.InsertOptions.IncludeColumns gates the entire operation.
            // When false, leave the INSERT untouched (no warning — user opted out).
            if (!insertOptions.IncludeColumns)
                return (context.DocumentText, []);

            var warnings = new List<string>();

            // Only process INSERT ... VALUES with no column list
            var candidates = visitor.Statements
                .Where(s => (s.InsertSpecification?.Columns == null || s.InsertSpecification.Columns.Count == 0)
                         && s.InsertSpecification?.InsertSource is ValuesInsertSource)
                .OrderByDescending(s => s.StartOffset)
                .ToList();

            if (candidates.Count == 0)
                return (context.DocumentText, []);

            // Same loaded AppSettings — no second ConfigManager.Load() call.
            // Default true on failure / test path (matches legacy behaviour).
            var includeTypes = loaded?.Formatter.InsertColumnsIncludeTypes ?? true;
            var includeDefaults = insertOptions.IncludeDefaultsAsComments;

            var text = context.DocumentText;

            foreach (var insert in candidates)
            {
                if (context.HasSelection)
                {
                    if (insert.StartOffset < context.SelectionStart ||
                        insert.StartOffset >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                var spec = insert.InsertSpecification;
                var target = spec?.Target;
                if (target == null) continue;

                if (target is not NamedTableReference tableRef) continue;

                var name = tableRef.SchemaObject;
                var tableName  = name?.BaseIdentifier?.Value ?? string.Empty;
                var schemaName = name?.SchemaIdentifier?.Value ?? "dbo";

                if (string.IsNullOrEmpty(tableName)) continue;

                if (context.SchemaCache == null)
                {
                    warnings.Add($"Could not resolve columns for: {schemaName}.{tableName}");
                    continue;
                }

                var dbObj = context.SchemaCache.FindObject(schemaName, tableName);
                if (dbObj == null || !dbObj.ColumnsLoaded || dbObj.Columns.Count == 0)
                {
                    warnings.Add($"Could not resolve columns for: {schemaName}.{tableName}");
                    continue;
                }

                var insertionText = includeTypes
                    ? BuildColumnListWithTypes(dbObj.Columns, includeDefaults)
                    : BuildColumnListSimple(dbObj.Columns);

                // Insertion point: just after the table reference (including alias if any)
                var insertOffset = tableRef.Alias != null
                    ? tableRef.Alias.StartOffset + tableRef.Alias.FragmentLength
                    : tableRef.StartOffset + tableRef.FragmentLength;

                // Skip any trailing whitespace before VALUES
                while (insertOffset < text.Length && char.IsWhiteSpace(text[insertOffset]))
                    insertOffset++;

                text = text.Insert(insertOffset, insertionText);
            }

            return (text, [.. warnings]);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }

    /// <summary>
    /// Builds a simple inline column list without type annotations (original behaviour).
    /// </summary>
    private static string BuildColumnListSimple(List<Column> columns)
    {
        var names = columns
            .Where(c => !c.IsIdentity && !c.IsComputed)
            .Select(c => c.ColumnName);
        return $"({string.Join(", ", names)}) ";
    }

    /// <summary>
    /// Builds a multi-line column list with inline type-metadata comments.
    /// Identity columns are included but annotated as IDENTITY; computed columns are skipped.
    /// </summary>
    public static string BuildColumnListWithTypes(List<Column> columns)
        => BuildColumnListWithTypes(columns, includeDefaults: true);

    /// <summary>
    /// Builds a multi-line column list with inline type-metadata comments.
    /// When <paramref name="includeDefaults"/> is false, the trailing
    /// <c>default (...)</c> segment is omitted (per <c>InsertOptions.IncludeDefaultsAsComments</c>).
    /// </summary>
    public static string BuildColumnListWithTypes(List<Column> columns, bool includeDefaults)
    {
        var lines = new List<string>();

        foreach (var col in columns)
        {
            if (col.IsComputed) continue;

            if (col.IsIdentity)
            {
                lines.Add($"    {col.ColumnName}    -- IDENTITY, auto-generated");
                continue;
            }

            var nullable = col.IsNullable ? "null" : "not null";
            var comment = $"-- {col.TypeDisplay}, {nullable}";

            if (includeDefaults && !string.IsNullOrEmpty(col.DefaultValue))
                comment += $", default ({col.DefaultValue})";

            lines.Add($"    {col.ColumnName},    {comment}");
        }

        // Remove the trailing comma from the last line (if it has one)
        if (lines.Count > 0)
        {
            var last = lines[^1];
            var commaIdx = last.IndexOf(',');
            var commentIdx = last.IndexOf("--");
            // Only strip the column-separator comma (the one before the comment), not commas inside comments
            if (commaIdx >= 0 && commentIdx >= 0 && commaIdx < commentIdx)
            {
                last = last.Remove(commaIdx, 1);
                // Pad with a space to keep alignment
                last = last.Insert(commaIdx, " ");
                lines[^1] = last;
            }
        }

        return "\n(\n" + string.Join("\n", lines) + "\n) ";
    }

    private sealed class InsertStatementCollector : TSqlFragmentVisitor
    {
        public List<InsertStatement> Statements { get; } = [];
        public override void Visit(InsertStatement node)
        {
            Statements.Add(node);
        }
    }

}
