using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE002 — Hard-coded credential in a variable or declaration — password, secret, key, or token assigned a string literal.</summary>
public sealed class SE002_HardcodedPassword : IAnalysisRule
{
    public string RuleId => "SE002";
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

        private static readonly string[] Keywords = ["password", "pwd", "secret", "key", "token"];

        private static bool IsCredentialName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            foreach (var kw in Keywords)
                if (lower.Contains(kw)) return true;
            return false;
        }

        // SET @password = 'value'
        public override void Visit(SetVariableStatement node)
        {
            if (!IsCredentialName(node.Variable?.Name)) return;
            if (node.Expression is not StringLiteral lit) return;
            if (string.IsNullOrEmpty(lit.Value)) return;

            Emit(node, node.Variable!.Name);
        }

        // UPDATE SET col = 'value' (AssignmentSetClause)
        public override void Visit(AssignmentSetClause node)
        {
            if (!IsCredentialName(node.Variable?.Name)) return;
            if (node.NewValue is not StringLiteral lit) return;
            if (string.IsNullOrEmpty(lit.Value)) return;

            Emit(node, node.Variable!.Name);
        }

        // DECLARE @password VARCHAR(50) = 'value'
        public override void Visit(DeclareVariableElement node)
        {
            if (!IsCredentialName(node.VariableName?.Value)) return;
            if (node.Value is not StringLiteral lit) return;
            if (string.IsNullOrEmpty(lit.Value)) return;

            Emit(node, node.VariableName!.Value);
        }

        private void Emit(TSqlFragment node, string varName) =>
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE002",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE002", DiagnosticSeverity.Error),
                Message      = $"Hard-coded credential '{varName}' — use a parameter, environment variable, or secret store instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
    }
}
