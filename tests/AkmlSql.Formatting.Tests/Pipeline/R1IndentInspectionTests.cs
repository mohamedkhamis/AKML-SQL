using System;
using System.Collections.Generic;
using System.Text;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 — empirical confirmation of the rollout workflow's headline hazard claim:
/// that DmlRules' absolute IndentLevel writes de-dent NESTED AND/OR/SET clauses (inside
/// subqueries / BEGIN-END) to global column 0 — a default-profile indentation regression that
/// passes Stage-6 (semantics) + Stage-7 (idempotency) and so would ship invisibly. Dumps the
/// rules-off vs rules-on output (Dml-only and ALL) for nested cases so the indent can be eyeballed.
/// Run: dotnet test tests/AkmlSql.Formatting.Tests --filter R1IndentInspection -l "console;verbosity=detailed"
/// </summary>
public class R1IndentInspectionTests
{
    private readonly ITestOutputHelper _output;
    public R1IndentInspectionTests(ITestOutputHelper output) => _output = output;

    private static readonly string[] NestedCases =
    {
        "SELECT * FROM dbo.Orders o WHERE o.Active = 1 AND o.Id IN (SELECT s.Id FROM dbo.Sub s WHERE s.A = 1 AND s.B = 2 OR s.C = 3)",
        "UPDATE dbo.T SET v = 1, w = 2 WHERE id IN (SELECT id FROM dbo.S WHERE a = 1 AND b = 2)",
        "IF @x > 0 BEGIN UPDATE dbo.T SET v = 1 WHERE id = @x AND active = 1 END",
    };

    [Fact]
    public void R1IndentInspection_DumpsNestedIndentRulesOffVsOn()
    {
        var dmlOnly = new IRuleSet[] { new DmlRules() };
        var all = new IRuleSet[] { new DmlRules(), new JoinRules(), new ListRules(), new ParenthesisRules(), new DdlRules(), new ControlFlowRules() };

        var sb = new StringBuilder();
        foreach (var sql in NestedCases)
        {
            sb.AppendLine("################################################################");
            sb.AppendLine("INPUT: " + sql);
            sb.AppendLine(Dump("rules OFF (baseline)", sql, null));
            sb.AppendLine(Dump("Dml ONLY", sql, dmlOnly));
            sb.AppendLine(Dump("ALL rules", sql, all));
        }
        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    private static string Dump(string label, string sql, IRuleSet[]? rules)
    {
        var r = new FormatterPipeline { LayoutRules = rules }.Format(sql, new FormattingProfile());
        var sb = new StringBuilder();
        sb.AppendLine($"--- {label}  (valid={r.ValidationPassed}, modified={r.WasModified}) ---");
        var lines = r.FormattedText.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
            sb.AppendLine($"{i,2}|{lines[i]}");   // leading '|' makes indentation visible
        return sb.ToString();
    }
}
