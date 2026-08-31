using System.Text.RegularExpressions;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Scans the token stream for inline suppression directives and builds a <see cref="SuppressionMap"/>.
///
/// <para>Two directive families are understood, in <c>--</c> or <c>/* */</c> comments, case-insensitively:</para>
///
/// <list type="bullet">
///   <item><description>
///     <b>akml</b> (the documented form):
///     <c>-- akml-disable-line RULE[, RULE...]</c> suppresses those rules on the comment's own line;
///     <c>-- akml-disable RULE[, ...]</c> opens a range that runs to the matching
///     <c>-- akml-enable RULE[, ...]</c> — or to the end of the document when there is no matching
///     enable, which is how a rule is turned off for a whole script. Omitting the rule ids means
///     "every rule".
///   </description></item>
///   <item><description>
///     <b>noqa</b> (the original form, still supported so existing scripts keep working):
///     <c>-- noqa: RULE[, ...]</c>, bare <c>-- noqa</c>, and <c>-- noqa-begin</c> / <c>-- noqa-end</c>.
///   </description></item>
/// </list>
///
/// <para>
/// An unclosed <c>akml-disable</c> is deliberately NOT a diagnostic: running to end-of-file is its
/// documented whole-script meaning, and the "Disable ... in this script" quick fix emits exactly
/// that. An unclosed <c>noqa-begin</c> still warns (NOQA001) — that form reads as a block that was
/// meant to be closed.
/// </para>
/// </summary>
public static class SuppressionParser
{
    // Anchored to the start of the comment, exactly as the noqa patterns are: a directive is the
    // whole point of the comment it sits in. Without the anchor, prose that merely mentions one
    // ("-- TODO: we could akml-disable PE001 here") would silently switch the rule off for the rest
    // of the file — a suppression nobody asked for and nobody would think to look for.
    //
    // "disable-line" must precede "disable" in the alternation so the longer verb wins.
    // The rule list stops at a newline; a /* ... */ terminator is dropped by ExtractRuleIds
    // because "*/" is not a rule id.
    private static readonly Regex AkmlDirective =
        new(@"^(?:--+|/\*)\s*akml-(?<verb>disable-line|disable|enable)\s*:?\s*(?<rules>[^\r\n]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A rule id is letters + digits (PE001, BP004, DEP001). Anything else in the rule list — a
    // trailing reason such as "-- akml-disable PE001 legacy report" — is ignored rather than
    // mistaken for a rule.
    private static readonly Regex RuleIdToken =
        new(@"\b[A-Za-z]{2,5}[0-9]{2,5}\b", RegexOptions.Compiled);

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
        var map = new SuppressionMap();
        metaDiagnostics = [];

        int? noqaBlockStart = null;

        // Open akml-disable ranges awaiting their akml-enable. One slot for the blanket form and
        // one entry per rule for the scoped form, each remembering the line it was opened on.
        int? openAll = null;
        var openByRule = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in tokens)
        {
            if (token.TokenType is not (TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
                continue;

            var text = token.Text;
            var line = token.Line;

            // -- akml-disable / akml-enable / akml-disable-line -----------------------
            var akml = AkmlDirective.Match(text);
            if (akml.Success)
            {
                var verb = akml.Groups["verb"].Value.ToLowerInvariant();
                var rules = ExtractRuleIds(akml.Groups["rules"].Value);

                switch (verb)
                {
                    case "disable-line":
                        map.SuppressLine(line, rules);
                        break;

                    case "disable":
                        if (rules is null)
                        {
                            openAll ??= line;
                        }
                        else
                        {
                            foreach (var id in rules)
                                if (!openByRule.ContainsKey(id)) openByRule[id] = line;
                        }
                        break;

                    case "enable":
                        if (rules is null)
                        {
                            // A bare enable closes everything currently open.
                            if (openAll.HasValue)
                            {
                                map.SuppressedBlocks.Add(new SuppressionRange(openAll.Value, line, null));
                                openAll = null;
                            }
                            foreach (var pair in openByRule)
                                map.SuppressedBlocks.Add(new SuppressionRange(pair.Value, line, NewRuleSet(pair.Key)));
                            openByRule.Clear();
                        }
                        else
                        {
                            foreach (var id in rules)
                            {
                                if (openByRule.TryGetValue(id, out var start))
                                {
                                    map.SuppressedBlocks.Add(new SuppressionRange(start, line, NewRuleSet(id)));
                                    openByRule.Remove(id);
                                }
                            }
                        }
                        break;
                }

                continue;
            }

            // -- legacy noqa ----------------------------------------------------------
            if (NoqaBegin.IsMatch(text))
            {
                noqaBlockStart = line;
                continue;
            }

            if (NoqaEnd.IsMatch(text))
            {
                if (noqaBlockStart.HasValue)
                    map.SuppressedBlocks.Add(new SuppressionRange(noqaBlockStart.Value, line, null));
                noqaBlockStart = null;
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
                map.SuppressLine(line, set);
                continue;
            }

            if (NoqaAll.IsMatch(text))
            {
                map.SuppressLine(line, null); // null = suppress all
            }
        }

        // An unclosed akml-disable runs to end of file. That is its documented whole-script
        // meaning — the "Disable ... in this script" quick fix writes exactly this — so it raises
        // no diagnostic.
        if (openAll.HasValue)
            map.SuppressedBlocks.Add(new SuppressionRange(openAll.Value, int.MaxValue, null));

        foreach (var pair in openByRule)
            map.SuppressedBlocks.Add(new SuppressionRange(pair.Value, int.MaxValue, NewRuleSet(pair.Key)));

        // Unclosed noqa-begin: suppress to int.MaxValue and emit a warning
        if (noqaBlockStart.HasValue)
        {
            map.SuppressedBlocks.Add(new SuppressionRange(noqaBlockStart.Value, int.MaxValue, null));
            metaDiagnostics.Add(new AnalysisDiagnostic
            {
                RuleId = "NOQA001",
                CategoryCode = "Meta",
                Severity = DiagnosticSeverity.Warning,
                Message = "-- noqa-begin has no matching -- noqa-end; suppression extends to end of file",
                Line = noqaBlockStart.Value
            });
        }

        return map;
    }

    /// <summary>
    /// Pulls the rule ids out of a directive's tail. Returns <see langword="null"/> when the tail
    /// names no rule — the directive's "every rule" form.
    /// </summary>
    private static HashSet<string>? ExtractRuleIds(string tail)
    {
        if (string.IsNullOrWhiteSpace(tail)) return null;

        HashSet<string>? set = null;
        foreach (Match m in RuleIdToken.Matches(tail))
        {
            set ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(m.Value.ToUpperInvariant());
        }
        return set;
    }

    private static HashSet<string> NewRuleSet(string ruleId) =>
        new(StringComparer.OrdinalIgnoreCase) { ruleId };
}
