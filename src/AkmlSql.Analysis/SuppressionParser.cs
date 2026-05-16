using System.Text.RegularExpressions;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Scans the token stream for -- noqa: RULEID and -- noqa-begin/end markers
/// and builds a <see cref="SuppressionMap"/>.
/// </summary>
public static class SuppressionParser
{
    private static readonly Regex NoqaRule =
        new(@"--\s*noqa\s*:\s*([A-Z0-9,\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NoqaAll =
        new(@"--\s*noqa\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NoqaBegin =
        new(@"--\s*noqa-begin", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NoqaEnd =
        new(@"--\s*noqa-end", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SuppressionMap Parse(IList<TSqlParserToken> tokens, out List<AnalysisDiagnostic> metaDiagnostics)
    {
        var map   = new SuppressionMap();
        metaDiagnostics = [];
        int? blockStart = null;

        foreach (var token in tokens)
        {
            if (token.TokenType is not (TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
                continue;

            var text = token.Text;
            var line = token.Line;

            if (NoqaBegin.IsMatch(text))
            {
                blockStart = line;
                continue;
            }

            if (NoqaEnd.IsMatch(text))
            {
                if (blockStart.HasValue)
                    map.SuppressedBlocks.Add((blockStart.Value, line));
                blockStart = null;
                continue;
            }

            var ruleMatch = NoqaRule.Match(text);
            if (ruleMatch.Success)
            {
                var ruleIds = ruleMatch.Groups[1].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in ruleIds)
                {
                    var trimmed = id.Trim().ToUpperInvariant();
                    if (!string.IsNullOrEmpty(trimmed))
                        set.Add(trimmed);
                }

                // Inline suppression: suppress the line the comment appears on
                map.SuppressedLines[line] = set;
                continue;
            }

            if (NoqaAll.IsMatch(text))
            {
                map.SuppressedLines[line] = null; // null = suppress all
            }
        }

        // Unclosed block: suppress to int.MaxValue and emit a warning
        if (blockStart.HasValue)
        {
            map.SuppressedBlocks.Add((blockStart.Value, int.MaxValue));
            metaDiagnostics.Add(new AnalysisDiagnostic
            {
                RuleId   = "NOQA001",
                CategoryCode = "Meta",
                Severity = DiagnosticSeverity.Warning,
                Message  = "-- noqa-begin has no matching -- noqa-end; suppression extends to end of file",
                Line     = blockStart.Value
            });
        }

        return map;
    }
}
