using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE010 — Connection string literal that does not include Encrypt=True transmits data in cleartext.</summary>
public sealed class Se010UnencryptedConnectionString : IAnalysisRule
{
    public string RuleId => "SE010";
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

        public override void Visit(StringLiteral node)
        {
            var value = node.Value;
            if (string.IsNullOrEmpty(value)) return;

            // Check if the literal looks like a connection string
            var hasServerKey  = value.IndexOf("Server=",      StringComparison.OrdinalIgnoreCase) >= 0;
            var hasDataSource = value.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasProvider   = value.IndexOf("Provider=",    StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasServerKey && !hasDataSource && !hasProvider) return;

            // Check whether encryption is already enabled
            var hasEncryptTrue = value.IndexOf("Encrypt=True", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasEncryptYes  = value.IndexOf("Encrypt=yes",  StringComparison.OrdinalIgnoreCase) >= 0;

            if (hasEncryptTrue || hasEncryptYes) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE010",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE010", DiagnosticSeverity.Warning),
                Message      = "Connection string without Encrypt=True transmits data in cleartext",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
