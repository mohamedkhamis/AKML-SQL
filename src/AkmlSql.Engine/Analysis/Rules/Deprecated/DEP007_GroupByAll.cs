using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Deprecated;

/// <summary>DEP007 — GROUP BY ALL is deprecated and may be removed in future SQL Server versions.</summary>
public sealed class Dep007GroupByAll : IAnalysisRule
{
    public string RuleId => "DEP007";
    public string Category => "Deprecated";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var diags      = new List<AnalysisDiagnostic>();
        var tokens     = ctx.Tokens;
        var batchStart = ctx.CurrentBatch.StartOffset;
        var batchEnd   = ctx.CurrentBatch.StartOffset + ctx.CurrentBatch.FragmentLength;

        for (var i = 0; i < tokens.Count - 2; i++)
        {
            if (tokens[i].Offset < batchStart || tokens[i].Offset >= batchEnd) continue;
            if (!tokens[i].Text.Equals("GROUP", StringComparison.OrdinalIgnoreCase)) continue;

            // Find the next non-whitespace token
            var j = i + 1;
            while (j < tokens.Count && tokens[j].TokenType == TSqlTokenType.WhiteSpace) j++;
            if (j >= tokens.Count || !tokens[j].Text.Equals("BY", StringComparison.OrdinalIgnoreCase)) continue;

            // Find the next non-whitespace token after BY
            var k = j + 1;
            while (k < tokens.Count && tokens[k].TokenType == TSqlTokenType.WhiteSpace) k++;
            if (k >= tokens.Count || !tokens[k].Text.Equals("ALL", StringComparison.OrdinalIgnoreCase)) continue;

            var token = tokens[i];
            diags.Add(new AnalysisDiagnostic
            {
                RuleId       = "DEP007",
                CategoryCode = "DEP",
                Severity     = ctx.Settings.GetSeverity("DEP007", DiagnosticSeverity.Warning),
                Message      = "GROUP BY ALL is deprecated and may be removed in future SQL Server versions — use explicit column groupings",
                StartOffset  = token.Offset,
                EndOffset    = tokens[k].Offset + tokens[k].Text.Length,
                Line         = token.Line,
                Column       = token.Column,
                FixActions   = []
            });
        }

        return diags;
    }
}
