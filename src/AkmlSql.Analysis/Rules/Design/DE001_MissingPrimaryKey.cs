using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Design;

/// <summary>DE001 — Every table should have a primary key defined.</summary>
public sealed class De001MissingPrimaryKey : IAnalysisRule
{
    public string RuleId => "DE001";
    public string Category => "Design";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
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

            // Check table-level PRIMARY KEY constraint
            foreach (var constraint in node.Definition.TableConstraints)
            {
                if (constraint is UniqueConstraintDefinition { IsPrimaryKey: true })
                    return;
            }

            // Check inline PRIMARY KEY constraint on any column
            foreach (var column in node.Definition.ColumnDefinitions)
            {
                foreach (var inline in column.Constraints)
                {
                    if (inline is UniqueConstraintDefinition { IsPrimaryKey: true })
                        return;
                }
            }

            var tableName = node.SchemaObjectName?.BaseIdentifier?.Value ?? "unknown";

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DE001",
                CategoryCode = "DE",
                Severity     = ctx.Settings.GetSeverity("DE001", DiagnosticSeverity.Warning),
                Message      = $"Table '{tableName}' has no primary key — every table should have a primary key",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
