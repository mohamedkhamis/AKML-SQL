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
/// Spec 030 — R1.0 de-risk spike (tasks T004 + T005). Runs a representative SQL corpus through the
/// FULL <see cref="FormatterPipeline"/> with the dormant layout <see cref="IRuleSet"/>s wired in (via
/// the off-by-default <c>LayoutRules</c> hook), one group at a time and all together, and reports —
/// per group — how many statements fail Stage 6 (semantic validation) or idempotency (format-twice
/// equality), how many EXCEPT, and — crucially — how many produce output that actually DIFFERS from
/// the no-rules baseline (to prove the rules are doing real work, not silently no-opping at default
/// options). This is a REPORTER, not a gate: it always passes and emits the table via ITestOutputHelper.
/// Run with:
///   dotnet test tests/AkmlSql.Formatting.Tests --filter R1RuleWiringSpike -l "console;verbosity=detailed"
/// The table is copied into research.md (R1 spike results) to make the T006 go/no-go decision.
/// </summary>
public class R1RuleWiringSpikeTests
{
    private readonly ITestOutputHelper _output;
    public R1RuleWiringSpikeTests(ITestOutputHelper output) => _output = output;

    // T004 corpus seed — varied, unformatted, valid T-SQL across the constructs the rules touch.
    private static readonly string[] Corpus =
    {
        "select col1, col2, col3 from dbo.Orders where col1 = 1 and col2 > 2 or col3 is null",
        "select o.OrderId, c.Name from dbo.Orders o inner join dbo.Customers c on c.Id = o.CustomerId left join dbo.Regions r on r.Id = c.RegionId where o.Active = 1",
        "select CategoryId, count(*) as Cnt, sum(Total) as Tot from dbo.Sales group by CategoryId having count(*) > 5 order by Tot desc",
        "insert into dbo.Orders (CustomerId, OrderDate, Total) values (1, '2026-01-01', 99.95)",
        "insert into dbo.Archive (Id, Name) select Id, Name from dbo.Orders where Active = 0",
        "update dbo.Orders set Total = Total * 1.1, Active = 1 where OrderDate < '2026-01-01' and CustomerId = 42",
        "delete from dbo.Orders where Active = 0 and OrderDate < '2020-01-01'",
        "merge dbo.Target t using dbo.Source s on t.Id = s.Id when matched then update set t.Val = s.Val when not matched then insert (Id, Val) values (s.Id, s.Val);",
        "with Totals as (select OrderId, sum(Qty) as TotalQty from dbo.OrderDetails group by OrderId) select o.OrderDate, t.TotalQty from dbo.Orders o join Totals t on t.OrderId = o.OrderId",
        "select OrderId, case when Total > 100 then 'big' when Total > 10 then 'mid' else 'small' end as Bucket from dbo.Orders",
        "create table dbo.NewThing (Id int identity(1,1) not null, Name nvarchar(100) not null, CreatedAt datetime null, constraint PK_NewThing primary key clustered (Id))",
        "select * from dbo.Orders where CustomerId in (select Id from dbo.Customers where RegionId = 3) and Total between 10 and 100",
    };

    private static (string Name, IRuleSet[] Rules)[] Groups() => new (string, IRuleSet[])[]
    {
        ("Dml",         new IRuleSet[] { new DmlRules() }),
        ("Ddl",         new IRuleSet[] { new DdlRules() }),
        ("Join",        new IRuleSet[] { new JoinRules() }),
        ("List",        new IRuleSet[] { new ListRules() }),
        ("Parenthesis", new IRuleSet[] { new ParenthesisRules() }),
        ("ControlFlow", new IRuleSet[] { new ControlFlowRules() }),
        ("ALL",         new IRuleSet[] { new DmlRules(), new JoinRules(), new ListRules(), new ParenthesisRules(), new DdlRules(), new ControlFlowRules() }),
    };

    [Fact]
    public void R1RuleWiringSpike_ReportsValidationAndIdempotencyPerGroup()
    {
        // Baseline: no rules (current production path). Capture validity + formatted text per item.
        var baselineOk = new bool[Corpus.Length];
        var baselineText = new string[Corpus.Length];
        for (int i = 0; i < Corpus.Length; i++)
        {
            var r = new FormatterPipeline().Format(Corpus[i], new FormattingProfile());
            baselineOk[i] = r.Success && r.ValidationPassed;
            baselineText[i] = r.FormattedText;
        }

        var report = new StringBuilder();
        report.AppendLine($"=== R1 spike: {Corpus.Length} statements through the full pipeline (default profile) ===");
        report.AppendLine("group        | valFail | idemFail | exc | changedVsBaseline | clean");
        report.AppendLine("-------------|---------|----------|-----|-------------------|------");
        foreach (var (name, rules) in Groups())
            report.AppendLine(RunGroup(name, rules, baselineOk, baselineText));

        _output.WriteLine(report.ToString());
        Assert.True(true); // reporter spike — the table above is the deliverable
    }

    private static string RunGroup(string name, IRuleSet[] rules, bool[] baselineOk, string[] baselineText)
    {
        int vFail = 0, idFail = 0, exc = 0, clean = 0, changed = 0;
        var notes = new StringBuilder();

        for (int i = 0; i < Corpus.Length; i++)
        {
            var profile = new FormattingProfile();
            var pipeline = new FormatterPipeline { LayoutRules = rules };
            try
            {
                var r1 = pipeline.Format(Corpus[i], profile);
                if (!r1.Success) { exc++; notes.Append($" [crash:{Trim(Corpus[i])}]"); continue; }
                if (!r1.ValidationPassed)
                {
                    // Only count as a rule-caused failure if the baseline validated this item.
                    if (baselineOk[i]) { vFail++; notes.Append($" [NEWval:{Trim(Corpus[i])}]"); }
                    continue;
                }
                var r2 = pipeline.Format(r1.FormattedText, profile);
                if (!r2.Success || !r2.ValidationPassed || r2.FormattedText != r1.FormattedText)
                {
                    idFail++; notes.Append($" [idem:{Trim(Corpus[i])}]"); continue;
                }
                if (baselineOk[i] && r1.FormattedText != baselineText[i]) changed++;
                clean++;
            }
            catch (Exception ex)
            {
                exc++; notes.Append($" [EX {ex.GetType().Name}:{Trim(Corpus[i])}]");
            }
        }

        var line = $"{name,-12} | {vFail,7} | {idFail,8} | {exc,3} | {changed,17} | {clean}/{Corpus.Length}";
        return notes.Length > 0 ? line + Environment.NewLine + "   " + notes.ToString().Trim() : line;
    }

    private static string Trim(string sql) => sql.Length <= 28 ? sql : sql.Substring(0, 28) + "…";
}
