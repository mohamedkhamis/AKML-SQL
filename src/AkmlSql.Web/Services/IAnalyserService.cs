namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) — M1 scaffold (T028).
/// Real wiring to <c>AkmlSql.Analyzer.AnalysisEngine</c> via <c>InProcessTransport</c>
/// lands at T043 in Phase 3 (M2).
/// </summary>
public interface IAnalyserService
{
    /// <summary>Stub returns no findings.</summary>
    IReadOnlyList<AnalysisFinding> Analyse(string sql);
}

public sealed record AnalysisFinding(string RuleId, string Severity, string Message, int Line, int Column);

internal sealed class StubAnalyserService : IAnalyserService
{
    public IReadOnlyList<AnalysisFinding> Analyse(string sql) => Array.Empty<AnalysisFinding>();
}
