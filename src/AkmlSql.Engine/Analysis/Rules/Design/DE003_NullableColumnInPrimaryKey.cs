using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Design;

/// <summary>DE003 — Columns that are part of a primary key cannot be nullable.</summary>
public sealed class DE003_NullableColumnInPrimaryKey : IAnalysisRule
{
    public string RuleId => "DE003";
    public string Category => "Design";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Error;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(CreateTableStatement node)
        {
            if (node.Definition == null) return;

            // Collect column names in inline primary key constraints
            var inlinePkColumns = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var column in node.Definition.ColumnDefinitions)
            {
                foreach (var constraint in column.Constraints)
                {
                    if (constraint is UniqueConstraintDefinition { IsPrimaryKey: true })
                        inlinePkColumns.Add(column.ColumnIdentifier?.Value ?? string.Empty);
                }
            }

            // Collect column names in table-level primary key constraints
            var tablePkColumns = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var constraint in node.Definition.TableConstraints)
            {
                if (constraint is not UniqueConstraintDefinition { IsPrimaryKey: true } pkConstraint) continue;
                foreach (var col in pkConstraint.Columns)
                {
                    var colName = col.Column?.MultiPartIdentifier?.Identifiers is { Count: > 0 } ids
                        ? ids[ids.Count - 1].Value
                        : null;
                    if (colName != null) tablePkColumns.Add(colName);
                }
            }

            // Check each column that is part of the primary key for nullable definition
            foreach (var column in node.Definition.ColumnDefinitions)
            {
                var colName = column.ColumnIdentifier?.Value ?? string.Empty;
                if (!inlinePkColumns.Contains(colName) && !tablePkColumns.Contains(colName)) continue;

                // Check if column is explicitly nullable
                var isNullable = false;
                foreach (var constraint in column.Constraints)
                {
                    if (constraint is NullableConstraintDefinition { Nullable: true })
                    {
                        isNullable = true;
                        break;
                    }
                }

                if (!isNullable) continue;

                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "DE003",
                    CategoryCode = "DE",
                    Severity     = ctx.Settings.GetSeverity("DE003", DiagnosticSeverity.Error),
                    Message      = $"Column '{colName}' in primary key cannot be NULL",
                    StartOffset  = column.StartOffset,
                    EndOffset    = column.StartOffset + column.FragmentLength,
                    Line         = column.StartLine,
                    Column       = column.StartColumn,
                    FixActions   = []
                });
            }
        }
    }
}
