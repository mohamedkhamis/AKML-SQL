using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// End-to-end proof that each documented way to switch a rule off actually removes the finding
/// from what <see cref="AnalysisEngine"/> returns — the surface the editor renders. The parser
/// tests prove the directives are understood; these prove the diagnostics really disappear.
/// </summary>
public sealed class SuppressionScopeEndToEndTests : IDisposable
{
    // PE002 fires on unqualified table references and needs no schema cache.
    private const string SqlWithPe002 = "SELECT Id FROM Orders";
    private const string TargetRule = "PE002";

    private readonly CaSettingsLoader _loader = new();
    private readonly AnalysisEngine _engine;
    private readonly SessionSuppressionStore _session = new();
    private readonly List<string> _tempDirs = [];

    public SuppressionScopeEndToEndTests()
    {
        _engine = new AnalysisEngine(new TsqlParserService(), new RuleRegistry(), _loader, _session);
    }

    [Fact]
    public async Task Baseline_TheRuleFires()
    {
        var result = await Analyze(SqlWithPe002);
        Assert.Contains(result.Issues, i => i.RuleId == TargetRule);
    }

    [Fact]
    public async Task LineScope_DisableLineDirectiveSilencesIt()
    {
        var result = await Analyze($"{SqlWithPe002}  -- akml-disable-line {TargetRule}");
        Assert.DoesNotContain(result.Issues, i => i.RuleId == TargetRule);
    }

    [Fact]
    public async Task ScriptScope_DisableAtTheTopSilencesTheWholeDocument()
    {
        // Exactly the text the "Disable ... in this script" quick fix inserts.
        var sql = $"-- akml-disable {TargetRule}\n{SqlWithPe002}\nGO\nSELECT Name FROM Customers";
        var result = await Analyze(sql);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == TargetRule);
    }

    [Fact]
    public async Task ScriptScope_LeavesTheRestOfTheScriptReporting()
    {
        var sql = $"-- akml-disable {TargetRule}\n{SqlWithPe002}\nGO\nDELETE FROM dbo.Orders";
        var result = await Analyze(sql);

        Assert.DoesNotContain(result.Issues, i => i.RuleId == TargetRule);
        Assert.Contains(result.Issues, i => i.RuleId == "PE003");
    }

    [Fact]
    public async Task BlockScope_OnlySilencesInsideTheBlock()
    {
        var sql = $"-- akml-disable {TargetRule}\n{SqlWithPe002}\n-- akml-enable {TargetRule}\nGO\nSELECT Name FROM Customers";
        var result = await Analyze(sql);

        // The occurrence after the enable still reports.
        Assert.Contains(result.Issues, i => i.RuleId == TargetRule);
        Assert.All(
            result.Issues.Where(i => i.RuleId == TargetRule),
            i => Assert.True(i.Line > 3, $"expected the surviving {TargetRule} to be after the enable, was line {i.Line}"));
    }

    [Fact]
    public async Task SessionScope_SilencesEveryDocument()
    {
        _session.Add(TargetRule);

        var one = await Analyze(SqlWithPe002, sessionId: "doc-1");
        var two = await Analyze("SELECT Name FROM Customers", sessionId: "doc-2");

        Assert.DoesNotContain(one.Issues, i => i.RuleId == TargetRule);
        Assert.DoesNotContain(two.Issues, i => i.RuleId == TargetRule);
    }

    [Fact]
    public async Task GlobalScope_RuleOverrideInConfigSilencesIt()
    {
        var settings = new CodeAnalysisSettings
        {
            RuleOverrides = { [TargetRule] = new RuleOverride { Enabled = false } }
        };

        var result = await Analyze(SqlWithPe002, settings: settings);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == TargetRule);
    }

    [Fact]
    public async Task GlobalScope_ARuleOverrideKeyedInLowercaseStillApplies()
    {
        // config.json documents that a hand-edited lowercase id is equivalent to the canonical one.
        var settings = new CodeAnalysisSettings
        {
            RuleOverrides = { ["pe002"] = new RuleOverride { Enabled = false } }
        };

        var result = await Analyze(SqlWithPe002, settings: settings);
        Assert.DoesNotContain(result.Issues, i => i.RuleId == TargetRule);
    }

    [Fact]
    public async Task ProjectScope_CaSettingsGlobalSuppressionUsingTheDocumentedRuleIdKey()
    {
        // The .casettings reference has always shown "ruleId"; the model only bound "rule", so a
        // file written from the docs parsed cleanly and suppressed nothing.
        var dir = NewTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, ".casettings"),
            $$"""
            { "globalSuppressions": [ { "ruleId": "{{TargetRule}}", "reason": "documented key" } ] }
            """);

        var result = await Analyze(SqlWithPe002, filePath: Path.Combine(dir, "q.sql"));
        Assert.DoesNotContain(result.Issues, i => i.RuleId == TargetRule);
    }

    [Fact]
    public async Task ProjectScope_CaSettingsGlobalSuppressionUsingTheRuleKeyStillWorks()
    {
        var dir = NewTempDir();
        await File.WriteAllTextAsync(Path.Combine(dir, ".casettings"),
            $$"""
            { "globalSuppressions": [ { "rule": "{{TargetRule}}", "reason": "original key" } ] }
            """);

        var result = await Analyze(SqlWithPe002, filePath: Path.Combine(dir, "q.sql"));
        Assert.DoesNotContain(result.Issues, i => i.RuleId == TargetRule);
    }

    // -- helpers --------------------------------------------------------------

    private Task<CodeAnalysisResponse> Analyze(
        string sql,
        string sessionId = "scope-e2e",
        string? filePath = null,
        CodeAnalysisSettings? settings = null)
    {
        var request = new CodeAnalysisRequest
        {
            SessionId = sessionId,
            RequestId = "r1",
            DocumentText = sql,
            FilePath = filePath ?? string.Empty,
        };

        return _engine.AnalyzeAsync(
            request, 160, null, settings ?? new CodeAnalysisSettings { Enabled = true }, CancellationToken.None);
    }

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "akml-suppress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        _loader.Dispose();
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
