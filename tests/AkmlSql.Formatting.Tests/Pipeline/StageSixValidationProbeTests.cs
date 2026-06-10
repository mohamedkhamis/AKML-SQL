using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 — DIAGNOSTIC PROBE for the Stage-6 (SemanticValidator) gap: GROUP BY/HAVING-aggregate
/// + CTE statements fail validation through the full pipeline and the formatter returns the
/// ORIGINAL unformatted SQL (research.md §R1 caveat). This probe maps the failure surface over the
/// whole corpus under the real <c>default.akmlstyle</c>:
/// <list type="bullet">
///   <item>which corpus items fail validation (ValidationPassed=false / WasModified=false);</item>
///   <item>for each failure: does the formatted output PARSE? (the validator returns false with no
///   diagnostic on a null parse) — and if it parses, the first differing region of the two
///   <c>Sql170ScriptGenerator</c> normalisations the validator compares.</item>
/// </list>
/// Not a pass/fail gate — it asserts true and dumps evidence via ITestOutputHelper.
/// Run: dotnet test tests/AkmlSql.Formatting.Tests --filter StageSixValidationProbe -l "console;verbosity=detailed"
/// </summary>
public class StageSixValidationProbeTests
{
    private readonly ITestOutputHelper _output;
    public StageSixValidationProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Map_StageSix_Validation_Failures_Across_Corpus()
    {
        var repoRoot = FindRepoRoot();
        var corpusDir = Path.Combine(repoRoot, "tests", "format-parity", "corpus");
        var sb = new StringBuilder();

        foreach (var path in Directory.EnumerateFiles(corpusDir, "*.sql").OrderBy(p => p))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            var sql = File.ReadAllText(path);

            // 1) Real pipeline (validation ON) — does it fail + return original?
            var profileOn = LoadDefaultStyle();
            var resOn = new FormatterPipeline().Format(sql, profileOn);

            sb.AppendLine("================================================================");
            sb.AppendLine($"{id}: Success={resOn.Success} ValidationPassed={resOn.ValidationPassed} WasModified={resOn.WasModified}");
            foreach (var d in resOn.Diagnostics)
                sb.AppendLine($"    diag[{d.Severity}] {d.Message}");

            if (resOn.ValidationPassed)
            {
                sb.AppendLine("    -> validation PASSES (formatted output kept).");
                continue;
            }

            // 2) Validation FAILED. Re-run with validation OFF to capture the formatted text the
            //    pipeline produced + discarded, then replicate the validator manually.
            var profileOff = LoadDefaultStyle();
            profileOff.Metadata.SkipValidation = true;
            var resOff = new FormatterPipeline().Format(sql, profileOff);
            var formatted = resOff.FormattedText;

            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            TSqlScript? origScript, fmtScript;
            IList<ParseError> origErrors, fmtErrors;
            using (var r = new StringReader(sql))
                origScript = parser.Parse(r, out origErrors) as TSqlScript;
            using (var r = new StringReader(formatted))
                fmtScript = parser.Parse(r, out fmtErrors) as TSqlScript;

            sb.AppendLine($"    [validation-off] formatted PARSES: {(fmtScript != null)} (errors={fmtErrors.Count})");
            if (fmtScript == null)
            {
                sb.AppendLine("    ROOT CAUSE = formatted output does NOT parse. First parse errors:");
                foreach (var e in fmtErrors.Take(5))
                    sb.AppendLine($"        line {e.Line} col {e.Column}: {e.Message}");
                sb.AppendLine("    --- formatted output ---");
                AppendNumbered(sb, formatted);
                continue;
            }

            var gen = new Sql170ScriptGenerator();
            gen.GenerateScript(origScript, out var origNorm);
            gen.GenerateScript(fmtScript, out var fmtNorm);
            if (origNorm == fmtNorm)
            {
                sb.AppendLine("    (normalisations EQUAL — failure was a parse error or transient)");
                continue;
            }

            bool caseOnly = string.Equals(origNorm, fmtNorm, StringComparison.OrdinalIgnoreCase);
            sb.AppendLine($"    ROOT CAUSE = normalised ASTs DIFFER. CASE-ONLY (OrdinalIgnoreCase equal) = {caseOnly}. First divergence:");
            AppendFirstDiff(sb, origNorm, fmtNorm);
            sb.AppendLine("    --- formatted output (validation-off) ---");
            AppendNumbered(sb, formatted);
        }

        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    [Fact]
    public void Isolate_Join_OverCollapse()
    {
        var repoRoot = FindRepoRoot();
        var sql = File.ReadAllText(Path.Combine(repoRoot, "tests", "format-parity", "corpus", "02-multi-join.sql"));
        var configs = new (string label, IRuleSet[]? rules)[]
        {
            ("OFF (base)",          null),
            ("Dml only",            new IRuleSet[] { new DmlRules() }),
            ("List only",           new IRuleSet[] { new ListRules() }),
            ("Join only",           new IRuleSet[] { new JoinRules() }),
            ("Parenthesis only",    new IRuleSet[] { new ParenthesisRules() }),
            ("ControlFlow only",    new IRuleSet[] { new ControlFlowRules() }),
            ("ALL (DefaultOrder)",  new List<IRuleSet>(RuleEngine.DefaultOrder).ToArray()),
        };
        var sb = new StringBuilder();
        foreach (var (label, rules) in configs)
        {
            var p = LoadDefaultStyle();
            p.Metadata.SkipValidation = true;
            var r = new FormatterPipeline { LayoutRules = rules }.Format(sql, p);
            var lines = r.FormattedText.Replace("\r\n", "\n").Split('\n');
            int joinLines = 0;
            foreach (var l in lines) if (l.ToUpperInvariant().Contains("JOIN")) joinLines++;
            sb.AppendLine($"--- {label,-20} lineCount={lines.Length} linesWithJOIN={joinLines} ---");
            foreach (var l in lines) if (l.ToUpperInvariant().Contains("FROM") || l.ToUpperInvariant().Contains("JOIN")) sb.AppendLine($"   |{l}");
        }
        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    [Fact]
    public void Dump_Statement_Structure_11()
    {
        var repoRoot = FindRepoRoot();
        var sql = File.ReadAllText(Path.Combine(repoRoot, "tests", "format-parity", "corpus", "11-stored-procedure.sql"));
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var script = parser.Parse(reader, out _) as TSqlScript;
        var sb = new StringBuilder();
        foreach (var batch in script!.Batches)
        {
            sb.AppendLine($"BATCH: {batch.Statements.Count} top-level statement(s)");
            foreach (var stmt in batch.Statements)
                DumpStmt(sb, stmt, 1);
        }
        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    private static void DumpStmt(StringBuilder sb, TSqlStatement stmt, int depth)
    {
        sb.AppendLine($"{new string(' ', depth * 2)}{stmt.GetType().Name} startOffset={stmt.StartOffset}");
        // recurse into the common control-flow containers
        if (stmt is BeginEndBlockStatement b)
            foreach (var s in b.StatementList.Statements) DumpStmt(sb, s, depth + 1);
        else if (stmt is IfStatement ifs)
        {
            if (ifs.ThenStatement != null) DumpStmt(sb, ifs.ThenStatement, depth + 1);
            if (ifs.ElseStatement != null) DumpStmt(sb, ifs.ElseStatement, depth + 1);
        }
        else if (stmt is WhileStatement w && w.Statement != null)
            DumpStmt(sb, w.Statement, depth + 1);
        else if (stmt is TryCatchStatement tc)
        {
            foreach (var s in tc.TryStatements.Statements) DumpStmt(sb, s, depth + 1);
            foreach (var s in tc.CatchStatements.Statements) DumpStmt(sb, s, depth + 1);
        }
    }

    [Fact]
    public void Dump_Operator_Tokenization()
    {
        const string sql = "select * from t where a >= 1 and b <> 2 and c <= 3 and d != 4 and e !< 5;";
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out _);
        var sb = new StringBuilder();
        foreach (var t in fragment.ScriptTokenStream)
        {
            if (t.TokenType == TSqlTokenType.WhiteSpace || t.TokenType == TSqlTokenType.EndOfFile) continue;
            sb.AppendLine($"  off={t.Offset,3} type={t.TokenType,-22} text='{t.Text}'");
        }
        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    [Fact]
    public void Isolate_TinyProc_BlockCram()
    {
        const string sql = "create procedure dbo.p as begin set nocount on; select 1; end";
        var configs = new (string label, IRuleSet[]? rules)[]
        {
            ("OFF (base)",          null),
            ("Dml only",            new IRuleSet[] { new DmlRules() }),
            ("Ddl only",            new IRuleSet[] { new DdlRules() }),
            ("List only",           new IRuleSet[] { new ListRules() }),
            ("Parenthesis only",    new IRuleSet[] { new ParenthesisRules() }),
            ("ControlFlow only",    new IRuleSet[] { new ControlFlowRules() }),
            ("ALL (DefaultOrder)",  new List<IRuleSet>(RuleEngine.DefaultOrder).ToArray()),
        };
        var sb = new StringBuilder();
        foreach (var (label, rules) in configs)
        {
            var p = LoadDefaultStyle();
            p.Metadata.SkipValidation = true;
            var r = new FormatterPipeline { LayoutRules = rules }.Format(sql, p);
            sb.AppendLine($"--- {label} ---");
            AppendNumbered(sb, r.FormattedText);
        }
        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    [Fact]
    public void Isolate_13_SubqueryBody_Collapse()
    {
        var repoRoot = FindRepoRoot();
        var sql = File.ReadAllText(Path.Combine(repoRoot, "tests", "format-parity", "corpus", "13-subqueries.sql"));
        var configs = new (string label, IRuleSet[]? rules)[]
        {
            ("OFF (base)",          null),
            ("Dml only",            new IRuleSet[] { new DmlRules() }),
            ("List only",           new IRuleSet[] { new ListRules() }),
            ("Parenthesis only",    new IRuleSet[] { new ParenthesisRules() }),
            ("ControlFlow only",    new IRuleSet[] { new ControlFlowRules() }),
            ("ALL (DefaultOrder)",  new List<IRuleSet>(RuleEngine.DefaultOrder).ToArray()),
        };
        var sb = new StringBuilder();
        foreach (var (label, rules) in configs)
        {
            var p = LoadDefaultStyle();
            p.Metadata.SkipValidation = true;
            var r = new FormatterPipeline { LayoutRules = rules }.Format(sql, p).FormattedText;
            var lines = r.Replace("\r\n", "\n").Split('\n');
            var countLine = lines.FirstOrDefault(l => l.ToUpperInvariant().Contains("COUNT"));
            sb.AppendLine($"{label,-20} COUNT-line: |{countLine}|");
        }
        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    [Fact]
    public void Dump_TwoPass_CteAlignment()
    {
        const string sql =
            "with a as (select customerid, customername from customers where active = 1), " +
            "b as (select orderid from orders) " +
            "select a.customername, b.orderid from a join b on a.customerid = b.orderid " +
            "where a.customername like 'X%' order by b.orderid desc;";
        var p = LoadDefaultStyle();
        p.Metadata.SkipValidation = true;
        var pass1 = new FormatterPipeline().Format(sql, p).FormattedText;
        var pass2 = new FormatterPipeline().Format(pass1, p).FormattedText;
        var pass3 = new FormatterPipeline().Format(pass2, p).FormattedText;
        var sb = new StringBuilder();
        sb.AppendLine("--- PASS 1 ---"); AppendNumbered(sb, pass1);
        sb.AppendLine("--- PASS 2 ---"); AppendNumbered(sb, pass2);
        sb.AppendLine($"--- pass2==pass3: {pass2 == pass3} ---");
        if (pass2 != pass3) { sb.AppendLine("--- PASS 3 ---"); AppendNumbered(sb, pass3); }

        // Which rule subset introduces the pass-1-only col-11 alignment?
        var sb2 = new StringBuilder();
        foreach (var (label, rules) in new (string, IRuleSet[])[]
        {
            ("Join only", new IRuleSet[] { new JoinRules() }),
            ("List only", new IRuleSet[] { new ListRules() }),
            ("Join+List", new IRuleSet[] { new JoinRules(), new ListRules() }),
        })
        {
            foreach (var (src, srcLabel) in new[] { (sql, "input=ORIG"), (pass1, "input=PASS1") })
            {
                var pp = LoadDefaultStyle();
                pp.Metadata.SkipValidation = true;
                var r = new FormatterPipeline { LayoutRules = rules }.Format(src, pp).FormattedText;
                var fromLine = r.Replace("\r\n", "\n").Split('\n')
                    .FirstOrDefault(l => l.TrimStart().StartsWith("FROM", StringComparison.OrdinalIgnoreCase) && l.Contains(" a"));
                sb2.AppendLine($"{label,-10} {srcLabel,-12} FROM-line: |{fromLine}|");
            }
        }
        _output.WriteLine(sb.ToString());
        _output.WriteLine(sb2.ToString());
        Assert.True(true);
    }

    [Fact]
    public void Compare_RulesOff_vs_RulesOn_For_NewlyFormatting()
    {
        var repoRoot = FindRepoRoot();
        var sb = new StringBuilder();
        foreach (var id in new[] { "02-multi-join", "11-stored-procedure", "12-merge-statement", "03-cte-with-columns", "04-multiple-ctes", "13-subqueries" })
        {
            var sql = File.ReadAllText(Path.Combine(repoRoot, "tests", "format-parity", "corpus", id + ".sql"));
            foreach (var (label, rules) in new (string, IReadOnlyList<IRuleSet>?)[] { ("RULES OFF", null), ("RULES ON (DefaultOrder)", AkmlSql.Formatting.Rules.RuleEngine.DefaultOrder) })
            {
                var p = LoadDefaultStyle();
                p.Metadata.SkipValidation = true;
                var r = new FormatterPipeline { LayoutRules = rules }.Format(sql, p);
                sb.AppendLine($"========= {id} — {label} =========");
                AppendNumbered(sb, r.FormattedText);
            }
        }
        _output.WriteLine(sb.ToString());
        Assert.True(true);
    }

    private static void AppendNumbered(StringBuilder sb, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
            sb.AppendLine($"      {i,2}|{lines[i]}");
    }

    private static void AppendFirstDiff(StringBuilder sb, string a, string b)
    {
        var la = a.Replace("\r\n", "\n").Split('\n');
        var lb = b.Replace("\r\n", "\n").Split('\n');
        int max = Math.Max(la.Length, lb.Length);
        int shown = 0;
        for (int i = 0; i < max && shown < 12; i++)
        {
            var x = i < la.Length ? la[i] : "<EOF>";
            var y = i < lb.Length ? lb[i] : "<EOF>";
            if (x == y) continue;
            sb.AppendLine($"        L{i}  orig: {x}");
            sb.AppendLine($"        L{i}  fmt : {y}");
            shown++;
        }
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
