using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T008 — isolates which layout rule (or rule interaction) drops the line break
/// before <c>ORDER BY</c> under composition, merging it onto the preceding <c>WHERE</c> line
/// (<c>WHERE country = 'USA' ORDER BY customername;</c>) on the <c>01-simple-select</c> corpus.
///
/// <para>Faithful repro: loads the real <c>default.akmlstyle</c> profile (NOT <c>new
/// FormattingProfile()</c> POCO defaults) so the bug-relevant options
/// (<c>collapseShortLists: true</c>, <c>collapseThreshold: 60</c>, <c>orderByOnNewLine: true</c>)
/// match the golden oracle exactly. Dumps the rules-OFF baseline, each rule in isolation, and
/// the full <see cref="RuleEngine.DefaultOrder"/>, flagging ORDER-BY-MERGED per config.</para>
///
/// Run: dotnet test tests/AkmlSql.Formatting.Tests --filter T008OrderByMergeIsolation -l "console;verbosity=detailed"
/// </summary>
public class T008OrderByMergeIsolationTests
{
    private readonly ITestOutputHelper _output;
    public T008OrderByMergeIsolationTests(ITestOutputHelper output) => _output = output;

    // Exact 01-simple-select corpus content.
    private const string Sql =
        "select customerid, customername, country from customers where country = 'USA' order by customername;";

    [Fact]
    public void Isolate_OrderBy_Merge_Across_RuleSubsets()
    {
        var profile = LoadDefaultStyle();
        profile.Metadata.EnableIdempotencyCheck = false;

        var configs = new (string label, IRuleSet[]? rules)[]
        {
            ("OFF (baseline)",      null),
            ("Dml only",           new IRuleSet[] { new DmlRules() }),
            ("Join only",          new IRuleSet[] { new JoinRules() }),
            ("List only",          new IRuleSet[] { new ListRules() }),
            ("Parenthesis only",   new IRuleSet[] { new ParenthesisRules() }),
            ("Ddl only",           new IRuleSet[] { new DdlRules() }),
            ("ControlFlow only",   new IRuleSet[] { new ControlFlowRules() }),
            ("Dml+List",           new IRuleSet[] { new DmlRules(), new ListRules() }),
            ("ALL (DefaultOrder)", new List<IRuleSet>(RuleEngine.DefaultOrder).ToArray()),
        };

        var sb = new StringBuilder();
        sb.AppendLine("INPUT: " + Sql);
        foreach (var (label, rules) in configs)
        {
            var r = new FormatterPipeline { LayoutRules = rules }.Format(Sql, profile);
            var text = r.FormattedText.Replace("\r\n", "\n");
            bool merged = MergesOrderByOntoWhere(text);
            sb.AppendLine("================================================");
            sb.AppendLine($"--- {label,-20} (valid={r.ValidationPassed}, modified={r.WasModified}, ORDER-BY-MERGED={merged}) ---");
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                sb.AppendLine($"{i,2}|{lines[i]}");
        }
        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    /// <summary>
    /// T008 regression lock: with <c>ListRules</c> active (alone and as part of the full
    /// <see cref="RuleEngine.DefaultOrder"/>), <c>ORDER BY</c> must stay on its own clause line at
    /// column 0 — never merged onto the preceding <c>WHERE</c> line, never indented. Guards the
    /// <c>FindListEnd</c>/<c>ApplyIndentListItems</c> ORDER/GROUP boundary fix.
    /// </summary>
    [Theory]
    [MemberData(nameof(RuleConfigsThatRunListRules))]
    public void OrderBy_StaysOnOwnClauseLine_WhenListRulesActive(string label, IRuleSet[] rules)
    {
        var profile = LoadDefaultStyle();
        profile.Metadata.EnableIdempotencyCheck = false;

        var result = new FormatterPipeline { LayoutRules = rules }.Format(Sql, profile);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');

        Assert.False(MergesOrderByOntoWhere(result.FormattedText),
            $"[{label}] ORDER BY was merged onto the WHERE line:\n{result.FormattedText}");

        // ORDER BY must appear as its own line, flush at column 0 (no leading whitespace).
        Assert.Contains(lines, l => l.StartsWith("ORDER BY ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l =>
            l.TrimStart() != l && l.TrimStart().StartsWith("ORDER BY ", StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> RuleConfigsThatRunListRules()
    {
        yield return new object[] { "List only", new IRuleSet[] { new ListRules() } };
        yield return new object[] { "ALL (DefaultOrder)", new List<IRuleSet>(RuleEngine.DefaultOrder).ToArray() };
    }

    /// <summary>True if any single emitted line contains both WHERE and ORDER BY (the merge bug).</summary>
    private static bool MergesOrderByOntoWhere(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var u = line.ToUpperInvariant();
            if (u.Contains("WHERE") && u.Contains("ORDER BY")) return true;
        }
        return false;
    }

    private static FormattingProfile LoadDefaultStyle()
    {
        var repoRoot = FindRepoRoot();
        var stylePath = Path.Combine(repoRoot, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "default.akmlstyle");
        return ProfileSerializer.Deserialize(File.ReadAllText(stylePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate AKML-SQL.slnx from " + AppContext.BaseDirectory);
    }
}
