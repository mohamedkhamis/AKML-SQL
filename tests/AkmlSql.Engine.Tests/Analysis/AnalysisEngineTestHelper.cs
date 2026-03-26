using System.Collections.Generic;
using System.Linq;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// Creates a minimal AnalysisContext for unit-testing individual analysis rules.
/// No schema cache, no session, default settings, TSql160 parser.
/// </summary>
public static class AnalysisEngineTestHelper
{
    private static readonly TsqlParserService Parser = new();

    static AnalysisEngineTestHelper()
    {
        Parser.SetServerVersion(160);
    }

    /// <summary>
    /// Runs all enabled rules (or just <paramref name="ruleId"/> if specified) against <paramref name="sql"/>
    /// and returns the resulting diagnostics.
    /// </summary>
    public static IReadOnlyList<AnalysisDiagnostic> Analyze(string sql, string? ruleId = null)
    {
        var script = Parser.Parse(sql, out _);
        if (script == null) return [];

        var tokens = Parser.GetTokenStream(sql);
        var suppressions = SuppressionParser.Parse(tokens, out var meta);

        var settings = new ResolvedAnalysisSettings { Enabled = true };

        var registry = new RuleRegistry();
        var rules = ruleId != null
            ? registry.AllRules.Where(r => r.RuleId == ruleId).ToList()
            : registry.GetEnabledRules(settings);

        var results = new List<AnalysisDiagnostic>(meta);

        foreach (var batch in script.Batches)
        {
            var ctx = new AnalysisContext
            {
                Script       = script,
                CurrentBatch = batch,
                Tokens       = tokens,
                DocumentText = sql,
                SessionId    = "test",
                SchemaCache  = null,
                Settings     = settings,
                Suppressions = suppressions
            };

            foreach (var rule in rules)
            {
                if (rule.RequiresSchema) continue;
                results.AddRange(rule.Analyze(ctx));
            }
        }

        return results
            .Where(d => !suppressions.IsSuppressed(d.Line, d.RuleId))
            .ToList();
    }
}
