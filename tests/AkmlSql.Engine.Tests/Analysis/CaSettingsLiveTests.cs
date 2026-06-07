using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// Spec 030 (T049/T051) — per-project .casettings must apply in the LIVE editor path (not only the
/// CLI). Before this, AnalysisEngine.AnalyzeAsync passed a null directory to CaSettingsLoader, so the
/// editor always used defaults. Now the request carries the document FilePath and the engine resolves
/// its directory. Engine-side / dotnet-testable; the shell populating FilePath is a small follow-up.
///
/// `DELETE FROM dbo.Foo` fires PE003 (DML without WHERE) + ST004 (missing semicolon) with no schema.
/// Disabling PE003 must drop PE003 while ST004 still fires — proving the .casettings did the work and
/// analysis still ran (guards against a false-positive empty result).
/// </summary>
public sealed class CaSettingsLiveTests : IDisposable
{
    private readonly string _dir;

    public CaSettingsLiveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"akml_ca_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static AnalysisEngine NewEngine() =>
        new(new TsqlParserService(), new RuleRegistry(), new CaSettingsLoader());

    private static Task<CodeAnalysisResponse> Analyze(AnalysisEngine engine, string sql, string? filePath)
    {
        var ca = new AppSettings().CodeAnalysis;
        ca.Enabled = true;
        return engine.AnalyzeAsync(
            new CodeAnalysisRequest { RequestId = "r", DocumentText = sql, DocumentVersion = 1, FilePath = filePath },
            serverVersion: 17, schemaCache: null, globalSettings: ca, CancellationToken.None);
    }

    private const string DmlNoWhere = "DELETE FROM dbo.Foo";

    private static System.Collections.Generic.List<string> Rules(CodeAnalysisResponse r) =>
        r.Issues.Select(i => i.RuleId).Distinct().ToList();

    [Fact]
    public async Task Baseline_NoFilePath_FiresPe003AndSt004()
    {
        var rules = Rules(await Analyze(NewEngine(), DmlNoWhere, filePath: null));
        Assert.Contains("PE003", rules);
        Assert.Contains("ST004", rules);
    }

    [Fact]
    public async Task CaSettings_DisablingRule_AppliesInLiveAnalysis()
    {
        File.WriteAllText(Path.Combine(_dir, ".casettings"), "{\"rules\":{\"PE003\":{\"enabled\":false}}}");

        var rules = Rules(await Analyze(NewEngine(), DmlNoWhere, filePath: Path.Combine(_dir, "query.sql")));

        Assert.DoesNotContain("PE003", rules);   // disabled by the project .casettings
        Assert.Contains("ST004", rules);          // other rules still run
    }

    [Fact]
    public async Task GlobalSuppression_InCaSettings_AppliesInLiveAnalysis()
    {
        File.WriteAllText(Path.Combine(_dir, ".casettings"), "{\"globalSuppressions\":[{\"rule\":\"PE003\"}]}");

        var rules = Rules(await Analyze(NewEngine(), DmlNoWhere, filePath: Path.Combine(_dir, "query.sql")));

        Assert.DoesNotContain("PE003", rules);
        Assert.Contains("ST004", rules);
    }
}
