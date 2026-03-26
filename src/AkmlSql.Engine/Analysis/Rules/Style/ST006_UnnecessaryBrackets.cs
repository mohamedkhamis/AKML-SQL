using System.Collections.Generic;
using System.Text.RegularExpressions;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST006 — Identifier wrapped in square brackets that don't need escaping.</summary>
public sealed class ST006_UnnecessaryBrackets : IAnalysisRule
{
    public string RuleId => "ST006";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Simple identifier: starts with letter or underscore, then letters/digits/underscores only
    private static readonly Regex SimpleIdentifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // Common SQL Server reserved words that require quoting
    private static readonly HashSet<string> ReservedWords = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "add","all","alter","and","any","as","asc","authorization","backup","begin","between",
        "break","browse","bulk","by","cascade","case","check","checkpoint","close","clustered",
        "coalesce","collate","column","commit","compute","constraint","contains","containstable",
        "continue","convert","create","cross","current","current_date","current_time",
        "current_timestamp","current_user","cursor","database","dbcc","deallocate","declare",
        "default","delete","deny","desc","disk","distinct","distributed","double","drop","dump",
        "else","end","errlvl","escape","except","exec","execute","exists","exit","external",
        "fetch","file","fillfactor","for","foreign","freetext","freetexttable","from","full",
        "function","goto","grant","group","having","holdlock","identity","identity_insert",
        "identitycol","if","in","index","inner","insert","intersect","into","is","join","key",
        "kill","left","like","lineno","load","merge","national","nocheck","nonclustered","not",
        "null","nullif","of","off","offsets","on","open","opendatasource","openquery","openrowset",
        "openxml","option","or","order","outer","over","percent","pivot","plan","precision",
        "primary","print","proc","procedure","public","raiserror","read","readtext","reconfigure",
        "references","replication","restore","restrict","return","revert","revoke","right",
        "rollback","rowcount","rowguidcol","rule","save","schema","securityaudit","select",
        "semantickeyphrasetable","semanticsimilaritydetailstable","semanticsimilaritytable",
        "session_user","set","setuser","shutdown","some","statistics","system_user","table",
        "tablesample","textsize","then","to","top","tran","transaction","trigger","truncate",
        "try_convert","tsequal","union","unique","unpivot","update","updatetext","use","user",
        "values","varying","view","waitfor","when","where","while","with","within","writetext"
    };

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var diags      = new List<AnalysisDiagnostic>();
        var batchStart = ctx.CurrentBatch.StartOffset;
        var batchEnd   = ctx.CurrentBatch.StartOffset + ctx.CurrentBatch.FragmentLength;

        foreach (var token in ctx.Tokens)
        {
            if (token.Offset < batchStart || token.Offset >= batchEnd) continue;
            var text = token.Text;
            if (string.IsNullOrEmpty(text)) continue;
            if (!text.StartsWith("[") || !text.EndsWith("]")) continue;

            // Extract inner name (strip brackets)
            var innerName = text.Substring(1, text.Length - 2);
            if (string.IsNullOrEmpty(innerName)) continue;

            // Only flag if inner name is a simple alphanumeric identifier
            if (!SimpleIdentifier.IsMatch(innerName)) continue;

            // Don't flag if it's a reserved word (those DO need brackets)
            if (ReservedWords.Contains(innerName)) continue;

            diags.Add(new AnalysisDiagnostic
            {
                RuleId       = "ST006",
                CategoryCode = "ST",
                Severity     = ctx.Settings.GetSeverity("ST006", DiagnosticSeverity.Hint),
                Message      = $"[{innerName}] — unnecessary square brackets around identifier",
                StartOffset  = token.Offset,
                EndOffset    = token.Offset + text.Length,
                Line         = token.Line,
                Column       = token.Column,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = $"Remove unnecessary brackets",
                        FixType          = FixType.Transform,
                        ReplacementStart = token.Offset,
                        ReplacementEnd   = token.Offset + text.Length,
                        ReplacementText  = innerName
                    }
                ]
            });
        }

        return diags;
    }
}
