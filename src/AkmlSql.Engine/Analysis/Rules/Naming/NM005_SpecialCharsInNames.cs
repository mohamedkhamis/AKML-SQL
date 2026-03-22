using System.Collections.Generic;
using System.Text.RegularExpressions;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Naming;

/// <summary>NM005 — Quoted identifier contains spaces or special characters; use standard alphanumeric names.</summary>
public sealed class NM005_SpecialCharsInNames : IAnalysisRule
{
    public string RuleId => "NM005";
    public string Category => "Naming";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    private static readonly Regex SpecialCharPattern =
        new(@"[ \-!@#$%^&*()+={}\[\]|:;<>?,./\\]", RegexOptions.Compiled);

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var diags = new List<AnalysisDiagnostic>();

        foreach (var token in ctx.Tokens)
        {
            var text = token.Text;
            if (string.IsNullOrEmpty(text)) continue;

            // Only check bracket-quoted or double-quoted identifiers
            string innerName;
            if (text.Length >= 2 && text[0] == '[' && text[text.Length - 1] == ']')
                innerName = text.Substring(1, text.Length - 2);
            else if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                innerName = text.Substring(1, text.Length - 2);
            else
                continue;

            if (!SpecialCharPattern.IsMatch(innerName)) continue;

            diags.Add(new AnalysisDiagnostic
            {
                RuleId       = "NM005",
                CategoryCode = "NM",
                Severity     = ctx.Settings.GetSeverity("NM005", DiagnosticSeverity.Warning),
                Message      = $"Identifier '{innerName}' contains special characters — use standard alphanumeric names without spaces or special characters",
                StartOffset  = token.Offset,
                EndOffset    = token.Offset + text.Length,
                Line         = token.Line,
                Column       = token.Column,
                FixActions   = []
            });
        }

        return diags;
    }
}
