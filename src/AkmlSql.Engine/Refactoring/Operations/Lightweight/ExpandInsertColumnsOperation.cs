using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Engine.Refactoring.Operations;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Expands INSERT statements that have no column list by adding explicit column names
/// sourced from the schema cache.
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

            var warnings = new List<string>();

            // Only process INSERT ... VALUES with no column list
            var candidates = visitor.Statements
                .Where(s => (s.InsertSpecification?.Columns == null || s.InsertSpecification.Columns.Count == 0)
                         && s.InsertSpecification?.InsertSource is ValuesInsertSource)
                .OrderByDescending(s => s.StartOffset)
                .ToList();

            if (candidates.Count == 0)
                return (context.DocumentText, []);

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

                var columnList    = string.Join(", ", dbObj.Columns.Select(c => c.ColumnName));
                var insertionText = $"({columnList}) ";

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

    private sealed class InsertStatementCollector : TSqlFragmentVisitor
    {
        public List<InsertStatement> Statements { get; } = [];
        public override void Visit(InsertStatement node) => Statements.Add(node);
    }
}
