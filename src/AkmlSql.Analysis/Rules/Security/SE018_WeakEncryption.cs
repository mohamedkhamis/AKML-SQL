using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE018 — ENCRYPTBYPASSPHRASE / DECRYPTBYPASSPHRASE uses DES which is considered weak.</summary>
public sealed class Se018WeakEncryption : IAnalysisRule
{
    public string RuleId => "SE018";
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

        private static readonly string[] WeakFunctions = ["ENCRYPTBYPASSPHRASE", "DECRYPTBYPASSPHRASE"];

        public override void Visit(FunctionCall node)
        {
            var name = node.FunctionName?.Value;
            if (name == null) return;

            bool isWeak = false;
            foreach (var fn in WeakFunctions)
            {
                if (string.Equals(name, fn, StringComparison.OrdinalIgnoreCase))
                {
                    isWeak = true;
                    break;
                }
            }

            if (!isWeak) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE018",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE018", DiagnosticSeverity.Warning),
                Message      = $"{name.ToUpperInvariant()} uses Triple-DES which is considered weak — use certificate-based encryption (ENCRYPTBYCERT / DECRYPTBYCERT) or asymmetric key encryption instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
