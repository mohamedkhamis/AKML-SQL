using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Expands the SET clause of UPDATE statements to include all columns from the target table,
/// appending missing columns with NULL as the default value.
/// </summary>
public class ExpandUpdateColumnsOperation : ILightweightOperation
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

            var collector = new UpdateStatementCollector();
            script.Accept(collector);

            if (collector.Statements.Count == 0)
                return (context.DocumentText, []);

            var warnings = new List<string>();
            var candidates = collector.Statements
                .OrderByDescending(s => s.StartOffset)
                .ToList();

            var text = context.DocumentText;

            foreach (var update in candidates)
            {
                if (context.HasSelection)
                {
                    if (update.StartOffset < context.SelectionStart ||
                        update.StartOffset >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                var spec = update.UpdateSpecification;
                if (spec?.Target == null) continue;

                string schemaName = "dbo";
                string tableName = string.Empty;

                if (spec.Target is NamedTableReference namedRef)
                {
                    var name = namedRef.SchemaObject;
                    tableName = name?.BaseIdentifier?.Value ?? string.Empty;
                    if (name?.SchemaIdentifier?.Value != null)
                        schemaName = name.SchemaIdentifier.Value;
                }

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

                // Find columns already in SET clause
                var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (spec.SetClauses != null)
                {
                    foreach (var clause in spec.SetClauses)
                    {
                        if (clause is AssignmentSetClause asc)
                        {
                            var colName = asc.Column?.MultiPartIdentifier?.Identifiers
                                .LastOrDefault()?.Value;
                            if (!string.IsNullOrEmpty(colName))
                                existingCols.Add(colName);
                        }
                    }
                }

                // Find missing columns (skip computed and identity)
                var missingCols = dbObj.Columns
                    .Where(c => !c.IsComputed && !existingCols.Contains(c.ColumnName))
                    .ToList();

                if (missingCols.Count == 0) continue;

                // Append missing columns after the last SET clause
                if (spec.SetClauses == null || spec.SetClauses.Count == 0) continue;

                var lastClause = spec.SetClauses.OrderByDescending(c => c.StartOffset).First();
                var insertOffset = lastClause.StartOffset + lastClause.FragmentLength;

                var sb = new StringBuilder();
                foreach (var col in missingCols)
                    sb.Append($",\r\n    {col.ColumnName} = NULL");

                text = text.Insert(insertOffset, sb.ToString());
            }

            return (text, [.. warnings]);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }

    private sealed class UpdateStatementCollector : TSqlFragmentVisitor
    {
        public List<UpdateStatement> Statements { get; } = [];
        public override void Visit(UpdateStatement node)
        {
            Statements.Add(node);
        }
    }
}
