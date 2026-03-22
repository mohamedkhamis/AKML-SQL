using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST001 — SQL keyword is not fully uppercase — use consistent ALL_CAPS keywords.</summary>
public sealed class ST001_InconsistentKeywordCasing : IAnalysisRule
{
    public string RuleId => "ST001";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    private static readonly HashSet<string> SqlKeywords = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "join", "inner", "outer", "left", "right", "on",
        "and", "or", "not", "null", "insert", "update", "delete", "create", "alter",
        "drop", "table", "index", "view", "procedure", "begin", "end", "if", "else",
        "while", "return", "set", "declare", "exec", "execute", "into", "values",
        "group", "order", "by", "having", "distinct", "top", "with", "as", "case",
        "when", "then", "cross", "full", "union", "all", "exists", "in", "between",
        "like", "is", "asc", "desc", "merge", "using", "matched", "output",
        "go", "use", "database", "schema", "function", "trigger", "cursor",
        "fetch", "open", "close", "deallocate", "print", "raiserror", "throw",
        "try", "catch", "transaction", "commit", "rollback", "save", "waitfor",
        "backup", "restore", "grant", "deny", "revoke"
    };

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var diags = new List<AnalysisDiagnostic>();
        var batchStart = ctx.CurrentBatch.StartOffset;
        var batchEnd   = ctx.CurrentBatch.StartOffset + ctx.CurrentBatch.FragmentLength;

        foreach (var token in ctx.Tokens)
        {
            if (token.Offset < batchStart || token.Offset >= batchEnd) continue;
            if (token.TokenType == TSqlTokenType.WhiteSpace
                || token.TokenType == TSqlTokenType.SingleLineComment
                || token.TokenType == TSqlTokenType.MultilineComment)
                continue;

            var text = token.Text;
            if (string.IsNullOrEmpty(text)) continue;

            if (!SqlKeywords.Contains(text)) continue;

            // Already fully uppercase — no issue
            if (text == text.ToUpperInvariant()) continue;

            var severity = ctx.Settings.GetSeverity("ST001", DiagnosticSeverity.Hint);
            diags.Add(new AnalysisDiagnostic
            {
                RuleId       = "ST001",
                CategoryCode = "ST",
                Severity     = severity,
                Message      = $"SQL keyword '{text}' is not uppercase — use consistent ALL_CAPS keywords",
                StartOffset  = token.Offset,
                EndOffset    = token.Offset + text.Length,
                Line         = token.Line,
                Column       = token.Column,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Convert to uppercase",
                        FixType          = FixType.Transform,
                        ReplacementStart = token.Offset,
                        ReplacementEnd   = token.Offset + text.Length,
                        ReplacementText  = text.ToUpperInvariant()
                    }
                ]
            });
        }

        return diags;
    }
}
