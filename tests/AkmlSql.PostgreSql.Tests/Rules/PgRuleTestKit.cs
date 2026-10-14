using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Schema;
using AkmlSql.PostgreSql.Analysis;
using AkmlSql.PostgreSql.Tests.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Rules;

/// <summary>
/// Spec 042 (tasks T157 to T159) - what the rule tests share: the registry, a way to run one rule on a script, and the marker
/// notation for the positive scripts. In a positive script, <c>[[...]]</c> wraps the exact text a finding must underline; the
/// markers are removed before the script is analysed and the rule must report exactly those spans.
/// </summary>
internal static class PgRuleTestKit
{
    public static readonly PgRuleRegistry Registry = new();

    /// <summary>The Northwind cache the completion corpus uses, with the lower-case tables in <c>public</c>.</summary>
    public static DatabaseCache Northwind(params string[] searchPath) => NorthwindPgTestCacheFactory.Create(searchPath);

    /// <summary>Removes the markers and returns the script with the (start, end) of each marked span.</summary>
    public static (string Sql, List<(int Start, int End)> Spans) Unmark(string marked)
    {
        var spans = new List<(int, int)>();
        var plain = new System.Text.StringBuilder();
        var open = -1;
        for (var i = 0; i < marked.Length; i++)
        {
            if (string.CompareOrdinal(marked, i, "[[", 0, 2) == 0)
            {
                open = plain.Length;
                i++;
            }
            else if (string.CompareOrdinal(marked, i, "]]", 0, 2) == 0 && open >= 0)
            {
                spans.Add((open, plain.Length));
                open = -1;
                i++;
            }
            else
            {
                plain.Append(marked[i]);
            }
        }
        return (plain.ToString(), spans);
    }

    /// <summary>Runs one rule on a script that must parse, so a rule test can never pass on a syntax error.</summary>
    public static List<AnalysisDiagnostic> Run(string ruleId, string sql, DatabaseCache? cache = null)
    {
        var context = PgAnalysisContext.Create(sql, cache);
        Assert.True(context.FirstSyntaxError == null, "The test script does not parse: " + context.FirstSyntaxError?.Error?.Message + "\n" + sql);

        var rule = Registry.Find(ruleId);
        Assert.NotNull(rule);
        return rule!.Analyze(context).ToList();
    }

    public static string Underlined(string sql, AnalysisDiagnostic d) => sql.Substring(d.StartOffset, d.EndOffset - d.StartOffset);
}
