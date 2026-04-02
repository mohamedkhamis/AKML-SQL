using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE009 — Stored procedures should begin with SET NOCOUNT ON.</summary>
public sealed class Pe009MissingSetNoCountOn : IAnalysisRule
{
    public string RuleId => "PE009";
    public string Category => "Performance";
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

        public override void Visit(CreateProcedureStatement node)
        {
            Check(node, node.StatementList);
        }

        public override void Visit(AlterProcedureStatement node)
        {
            Check(node, node.StatementList);
        }

        private void Check(TSqlStatement proc, StatementList? body)
        {
            if (body == null || body.Statements.Count == 0) return;

            var first = body.Statements[0];
            if (first is PredicateSetStatement ps &&
                ps.Options == SetOptions.NoCount && ps.IsOn)
                return; // Already has SET NOCOUNT ON

            // Check if any SET NOCOUNT ON exists before other statements
            var hasNoCount = body.Statements
                .OfType<PredicateSetStatement>()
                .Any(s => s.Options == SetOptions.NoCount && s.IsOn);

            if (hasNoCount) return;

            var insertOffset = body.Statements[0].StartOffset;
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE009",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE009", DiagnosticSeverity.Warning),
                Message      = "Stored procedure is missing SET NOCOUNT ON at the beginning",
                StartOffset  = proc.StartOffset,
                EndOffset    = proc.StartOffset + proc.FragmentLength,
                Line         = proc.StartLine,
                Column       = proc.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Insert SET NOCOUNT ON",
                        FixType          = FixType.Insert,
                        ReplacementStart = insertOffset,
                        ReplacementEnd   = insertOffset,
                        ReplacementText  = "SET NOCOUNT ON;\r\n"
                    }
                ]
            });
        }
    }
}
