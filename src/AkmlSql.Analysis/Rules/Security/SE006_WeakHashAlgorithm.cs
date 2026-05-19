using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE006 — HASHBYTES called with a cryptographically weak algorithm (MD5, SHA, SHA1).</summary>
public sealed class Se006WeakHashAlgorithm : IAnalysisRule
{
    public string RuleId => "SE006";
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

        private static readonly string[] WeakAlgorithms = ["MD5", "SHA", "SHA1"];

        public override void Visit(FunctionCall node)
        {
            if (!string.Equals(node.FunctionName?.Value, "HASHBYTES", StringComparison.OrdinalIgnoreCase))
                return;

            if (node.Parameters == null || node.Parameters.Count == 0) return;

            var firstArg = node.Parameters[0];
            if (firstArg is not StringLiteral algoLit) return;

            var algo = algoLit.Value;
            bool isWeak = false;
            foreach (var weak in WeakAlgorithms)
            {
                if (string.Equals(algo, weak, StringComparison.OrdinalIgnoreCase))
                {
                    isWeak = true;
                    break;
                }
            }

            if (!isWeak) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE006",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE006", DiagnosticSeverity.Warning),
                Message      = $"HASHBYTES with {algo} is cryptographically weak — use 'SHA2_256' or 'SHA2_512' instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Replace with SHA2_256",
                        FixType          = FixType.Transform,
                        ReplacementStart = firstArg.StartOffset,
                        ReplacementEnd   = firstArg.StartOffset + firstArg.FragmentLength,
                        ReplacementText  = "'SHA2_256'"
                    }
                ]
            });
        }
    }
}
