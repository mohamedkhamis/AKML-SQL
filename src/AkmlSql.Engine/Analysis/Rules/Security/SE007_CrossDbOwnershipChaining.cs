using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE007 — Cross-database object reference (three-part or four-part name) detected.</summary>
public sealed class Se007CrossDbOwnershipChaining : IAnalysisRule
{
    public string RuleId => "SE007";
    public string Category => "Security";
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

        public override void Visit(NamedTableReference node)
        {
            var schemaObj = node.SchemaObject;
            if (schemaObj == null) return;

            // A DatabaseIdentifier present means three-part (db.schema.obj) or four-part (server.db.schema.obj)
            if (schemaObj.DatabaseIdentifier == null) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE007",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE007", DiagnosticSeverity.Warning),
                Message      = "Cross-database object reference detected — ensure cross-database ownership chaining is intentional",
                StartOffset  = schemaObj.StartOffset,
                EndOffset    = schemaObj.StartOffset + schemaObj.FragmentLength,
                Line         = schemaObj.StartLine,
                Column       = schemaObj.StartColumn,
                FixActions   = []
            });
        }
    }
}
