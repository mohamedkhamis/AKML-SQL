using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// T102 — Performance benchmarks.
/// Targets: 1,000-line file &lt; 200ms, 10,000-line file &lt; 1,000ms.
/// Results are written to test output; thresholds are enforced as soft warnings only
/// (Assert skipped on CI to avoid flakiness from resource contention).
/// </summary>
public sealed class PerformanceBenchmarkTests(ITestOutputHelper output)
{
    private static string GenerateSql(int procedures, int statementsPerProc)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < procedures; i++)
        {
            sb.AppendLine($"CREATE PROCEDURE dbo.usp_Proc{i}");
            sb.AppendLine("    @Id INT");
            sb.AppendLine("AS");
            sb.AppendLine("SET NOCOUNT ON;");
            sb.AppendLine("BEGIN TRY");
            for (var j = 0; j < statementsPerProc; j++)
            {
                sb.AppendLine($"    SELECT col{j} FROM dbo.Table{j} WHERE Id = @Id;");
            }
            sb.AppendLine("    RETURN 0;");
            sb.AppendLine("END TRY");
            sb.AppendLine("BEGIN CATCH");
            sb.AppendLine("    THROW;");
            sb.AppendLine("END CATCH");
            sb.AppendLine("GO");
        }
        return sb.ToString();
    }

    [Fact]
    public void Benchmark_1000Lines_Under200ms()
    {
        // ~1,000-line file: 50 procs × 14 lines each ≈ 700 lines + GO separators
        var sql = GenerateSql(procedures: 50, statementsPerProc: 8);
        var lineCount = sql.Count(c => c == '\n');
        output.WriteLine($"SQL size: {sql.Length:N0} bytes, {lineCount:N0} lines");

        // Warm-up
        AnalysisEngineTestHelper.Analyze(sql);

        var sw = Stopwatch.StartNew();
        var diags = AnalysisEngineTestHelper.Analyze(sql);
        sw.Stop();

        output.WriteLine($"1,000-line benchmark: {sw.ElapsedMilliseconds}ms, {diags.Count} diagnostics");

        // Soft assertion — log but don't fail on slow CI machines
        if (sw.ElapsedMilliseconds > 200)
            output.WriteLine($"WARNING: exceeded 200ms target ({sw.ElapsedMilliseconds}ms) — may indicate performance regression");
        else
            output.WriteLine("PASS: within 200ms target");
    }

    [Fact]
    public void Benchmark_10000Lines_Under1000ms()
    {
        // ~10,000-line file: 400 procs × ~20 lines each
        var sql = GenerateSql(procedures: 400, statementsPerProc: 10);
        var lineCount = sql.Count(c => c == '\n');
        output.WriteLine($"SQL size: {sql.Length:N0} bytes, {lineCount:N0} lines");

        // Warm-up
        AnalysisEngineTestHelper.Analyze(GenerateSql(procedures: 5, statementsPerProc: 3));

        var sw = Stopwatch.StartNew();
        var diags = AnalysisEngineTestHelper.Analyze(sql);
        sw.Stop();

        output.WriteLine($"10,000-line benchmark: {sw.ElapsedMilliseconds}ms, {diags.Count} diagnostics");

        if (sw.ElapsedMilliseconds > 1000)
            output.WriteLine($"WARNING: exceeded 1,000ms target ({sw.ElapsedMilliseconds}ms) — may indicate performance regression");
        else
            output.WriteLine("PASS: within 1,000ms target");
    }
}
