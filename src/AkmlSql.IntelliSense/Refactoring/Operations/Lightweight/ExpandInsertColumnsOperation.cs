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

                // Spec 047 installed-product run F6: the script's own line ending (an SSMS script is CRLF).
                if (text.Contains("\r\n")) insertionText = insertionText.Replace("\n", "\r\n");

                // Insertion point: just after the table reference (including alias if any)
                var insertOffset = tableRef.Alias != null
                    ? tableRef.Alias.StartOffset + tableRef.Alias.FragmentLength
                    : tableRef.StartOffset + tableRef.FragmentLength;
                var tableEnd = insertOffset;

                // Skip any trailing whitespace before VALUES
                while (insertOffset < text.Length && char.IsWhiteSpace(text[insertOffset]))
                    insertOffset++;

                // F6: a list that starts on its own line takes the place of the blank after the table name, which was
                // otherwise left at the end of that line.
                if (insertionText.StartsWith("\n", StringComparison.Ordinal) || insertionText.StartsWith("\r\n", StringComparison.Ordinal))
                {
                    text = text.Remove(tableEnd, insertOffset - tableEnd);
                    insertOffset = tableEnd;
                }

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
    /// Identity columns are named in a comment (not listed); computed columns are skipped.
    /// </summary>
    public static string BuildColumnListWithTypes(List<Column> columns)
        => BuildColumnListWithTypes(columns, includeDefaults: true);

    /// <summary>
    /// Builds a multi-line column list with inline type-metadata comments, the comments lined up in one column.
    /// When <paramref name="includeDefaults"/> is false, the trailing
    /// <c>default (...)</c> segment is omitted (per <c>InsertOptions.IncludeDefaultsAsComments</c>).
    /// <para>
    /// Spec 047 installed-product run F7: an IDENTITY column is not part of an INSERT without a column list (the server fills
    /// it and the VALUES carry nothing for it), so it is named on a comment line and never listed. It used to be written as a
    /// bare name with no comma, which made the list invalid and one column longer than the VALUES. F6: the comments line up.
    /// </para>
    /// </summary>
    public static string BuildColumnListWithTypes(List<Column> columns, bool includeDefaults)
    {
        var listed = columns.Where(c => !c.IsComputed && !c.IsIdentity).ToList();
        var width = listed.Count == 0 ? 0 : listed.Max(c => c.ColumnName.Length) + 1;   // room for the comma
        var lines = new List<string>();
        var position = 0;

        foreach (var col in columns)
        {
            if (col.IsComputed) continue;

            if (col.IsIdentity)
            {
                lines.Add($"    -- {col.ColumnName}: IDENTITY, auto-generated (not listed: the server fills it)");
                continue;
            }

            var nullable = col.IsNullable ? "null" : "not null";
            var comment = $"-- {col.TypeDisplay}, {nullable}";

            if (includeDefaults && !string.IsNullOrEmpty(col.DefaultValue))
                comment += $", default ({col.DefaultValue})";

            position++;
            var head = position < listed.Count ? col.ColumnName + "," : col.ColumnName;
            lines.Add($"    {head.PadRight(width)}    {comment}");
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
