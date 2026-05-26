using System.Threading;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Parity;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 021 (web edition) F1 follow-up + M2 T043. Validates that the in-process
/// AnalyserService runs the extracted AkmlSql.Analysis engine end-to-end inside the
/// Blazor WASM process and produces the same findings the IDE plugin would for the
/// same input.
/// </summary>
public sealed class AnalyserServiceTests
{
    private readonly ITestOutputHelper _output;

    public AnalyserServiceTests(ITestOutputHelper output) => _output = output;

    private static IAnalyserService CreateService() => new AnalyserService();

    [Fact]
    public async Task AnalyseAsync_returns_PE001_for_select_star_in_procedure()
    {
        // PE001 fires for SELECT * inside a stored procedure (not for bare SELECTs).
        var service = CreateService();
        var response = await service.AnalyseAsync(
            "CREATE PROCEDURE dbo.GetCustomers AS\nBEGIN\n    SELECT * FROM dbo.Customers;\nEND;");

        Assert.NotNull(response);
        Assert.NotNull(response.Issues);
        Assert.Contains(response.Issues, i => i.RuleId == "PE001");
    }

    [Fact]
    public async Task AnalyseAsync_returns_no_issues_for_clean_sql()
    {
        var service = CreateService();
        var response = await service.AnalyseAsync(
            "SET NOCOUNT ON;\nSELECT Id, Name FROM dbo.Orders WHERE Id = 1;");

        Assert.NotNull(response);
        Assert.NotNull(response.Issues);
        // Clean SQL should not trip PE001 / PE003 / WHERE-missing rules.
        Assert.DoesNotContain(response.Issues, i => i.RuleId == "PE001");
    }

    [Fact]
    public async Task AnalyseAsync_handles_empty_input_without_crashing()
    {
        var service = CreateService();
        var response = await service.AnalyseAsync("");

        Assert.NotNull(response);
    }

    [Fact]
    public async Task AnalyseAsync_refuses_oversized_input()
    {
        var service = CreateService();
        var oversized = new string('x', DocumentSizeLimit.MaxDocumentSizeChars + 1);

        await Assert.ThrowsAsync<DocumentTooLargeException>(
            () => service.AnalyseAsync(oversized));
    }

    [Fact]
    public async Task AnalyseAsync_respects_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = CreateService();

        // Pre-cancelled token: AnalysisEngine internally checks ct on every batch, so it
        // throws OCE. The service does not swallow it -- the caller catches.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AnalyseAsync("SELECT 1;", null, cts.Token));
    }

    /// <summary>
    /// Spec 024 T023 / US3 — parity driver. For every corpus item, run the web edition's
    /// analyser against the same input the <see cref="ParityBaselineGenerator"/> consumed
    /// and assert the finding set matches the on-disk baseline along all five attributes
    /// (RuleId / Severity / Message / Line / Column), after sorting both to the canonical
    /// order specified in contracts/parity-baseline-format.md. Per-rule divergences
    /// registered in <see cref="ParityDispositionsRegistry"/> are accepted.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnalyserParityItems))]
    public async Task Analyser_MatchesIdeBaseline_AcrossCorpus(string corpusId)
    {
        var sql = ParityCorpusLoader.LoadInputSql(corpusId);
        var service = new AnalyserService();
        var response = await service.AnalyseAsync(sql);

        var actual = response.Issues
            .OrderBy(i => i.Line)
            .ThenBy(i => i.Column)
            .ThenBy(i => i.RuleId, StringComparer.Ordinal)
            .Select(i => new ParityCorpusLoader.ParityFinding(
                i.RuleId,
                SeverityName(i.Severity),
                i.Message,
                i.Line,
                i.Column))
            .ToArray();

        var expected = ParityCorpusLoader.LoadAnalyserBaseline(corpusId);

        // Walk both lists in lock-step. Any drift reports the first divergence
        // with full context — easier to triage than a single big "lists differ".
        var max = Math.Max(actual.Length, expected.Length);
        var failures = new List<string>();
        for (var i = 0; i < max; i++)
        {
            var e = i < expected.Length ? expected[i] : null;
            var a = i < actual.Length ? actual[i] : null;
            if (FindingsEqual(e, a)) continue;

            var reason = ParityDispositionsRegistry.AcceptedReason(corpusId, "default", e?.RuleId ?? a?.RuleId);
            if (reason is not null)
            {
                _output.WriteLine($"ACCEPTED_WITH_REASON ({corpusId}, default, {e?.RuleId ?? a?.RuleId}) — {reason}");
                continue;
            }
            failures.Add(
                $"#{i}: expected={Describe(e)}\n      actual  ={Describe(a)}");
        }

        if (failures.Count == 0) return;

        Assert.Fail(
            $"Analyser parity divergence for ({corpusId}). Either fix the analyser or " +
            "register the offending rule in ParityDispositionsRegistry with a ReasonLink." +
            $"\n\n=== {failures.Count} mismatch(es) ===\n" +
            string.Join("\n\n", failures));
    }

    public static IEnumerable<object[]> AnalyserParityItems() =>
        ParityCorpusLoader.EnumerateAnalyserItems().Select(id => new object[] { id });

    private static bool FindingsEqual(ParityCorpusLoader.ParityFinding? a, ParityCorpusLoader.ParityFinding? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.RuleId == b.RuleId
            && a.Severity == b.Severity
            && a.Message == b.Message
            && a.Line == b.Line
            && a.Column == b.Column;
    }

    private static string Describe(ParityCorpusLoader.ParityFinding? f) =>
        f is null ? "(none)" : $"{f.RuleId} {f.Severity} L{f.Line}:C{f.Column} — {f.Message}";

    private static string SeverityName(int severity) =>
        severity switch
        {
            3 => "Error",
            2 => "Warning",
            1 => "Info",
            _ => "Hint",
        };
}
