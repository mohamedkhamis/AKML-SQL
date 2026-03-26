using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE008 — xp_cmdshell allows execution of OS commands from SQL Server — critical security risk.</summary>
public sealed class Se008XpCmdshell : IAnalysisRule
{
    public string RuleId => "SE008";
    public string Category => "Security";
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

        public override void Visit(ExecuteStatement node)
        {
            var entity = node.ExecuteSpecification?.ExecutableEntity;
            if (entity is not ExecutableProcedureReference procRef) return;

            // ExecutableProcedureReference.ProcedureReference → ProcedureReferenceName
            // ProcedureReferenceName.ProcedureReference       → ProcedureReference
            // ProcedureReference.Name                         → SchemaObjectName
            var name = procRef.ProcedureReference?.ProcedureReference?.Name;
            if (name == null) return;

            var baseName = name.BaseIdentifier?.Value;
            if (!string.Equals(baseName, "xp_cmdshell", StringComparison.OrdinalIgnoreCase)) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE008",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE008", DiagnosticSeverity.Error),
                Message      = "xp_cmdshell allows execution of OS commands from SQL Server — this is a critical security risk",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
