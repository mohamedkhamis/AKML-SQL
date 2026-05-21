using System.Text.Json;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Pages;

// Spec 023 (M1 ScriptDom-in-WASM spike) -- T011.
// Small in-memory record types backing Spike.razor. Declared `internal` so the
// desktop golden generator in AkmlSql.Web.Tests can reach them through the
// project's existing InternalsVisibleTo("AkmlSql.Web.Tests").
//
// Note on the finding type: data-model.md Entity 2 referred to `AnalysisDiagnostic`.
// The real web-edition surface -- IAnalyserService.AnalyseAsync -- returns a
// CodeAnalysisResponse whose Issues array is CodeIssueInfo[]. The spike uses that
// actual type; CodeIssueInfo carries RuleId / Severity / Message / Line / Column,
// every field the spike-page contract renders.

/// <summary>One entry in the T-SQL spike corpus; deserialised from spike-corpus/corpus.json.</summary>
internal sealed record SpikeCorpusItem
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Construct { get; init; } = string.Empty;
    public string SqlPath { get; init; } = string.Empty;
    public string ExpectedFormattedPath { get; init; } = string.Empty;
    public string ExpectedAnalysisPath { get; init; } = string.Empty;
}

/// <summary>A single timed operation (parse+format, or analyse) and whether it threw.</summary>
internal sealed record OperationOutcome
{
    public string Operation { get; init; } = string.Empty;
    public bool Success { get; init; }
    public double ElapsedMs { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Full <see cref="Exception.ToString()"/> -- type, message, stack and inner exceptions. The spike's core evidence on failure.</summary>
    public string? ErrorDetail { get; init; }

    public static OperationOutcome Ok(string operation, double elapsedMs) => new()
    {
        Operation = operation,
        Success = true,
        ElapsedMs = elapsedMs,
    };

    public static OperationOutcome Fail(string operation, Exception ex) => new()
    {
        Operation = operation,
        Success = false,
        ErrorType = ex.GetType().FullName,
        ErrorMessage = ex.Message,
        ErrorDetail = ex.ToString(),
    };
}

/// <summary>The outcome of running parse+format and analyse over one input (a corpus item).</summary>
internal sealed record SpikeRunResult
{
    public string InputId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public OperationOutcome? ParseAndFormat { get; init; }
    public OperationOutcome? Analyse { get; init; }
    public string? FormattedOutput { get; init; }
    public IReadOnlyList<CodeIssueInfo> Findings { get; init; } = Array.Empty<CodeIssueInfo>();
    public int RulesDiscovered { get; init; }
    public bool? FormattedMatchesGolden { get; init; }
    public bool? AnalysisMatchesGolden { get; init; }
}

/// <summary>A normalised analyser finding -- the stable projection used for golden-file comparison.</summary>
internal sealed record SpikeFinding(string RuleId, int Severity, int Line, int Column, string Message);

/// <summary>
/// Golden-comparison helpers shared by BOTH the desktop generator
/// (SpikeCorpusGoldenTests, desktop .NET) and Spike.razor (browser WASM), so the
/// only variable between a golden file and a spike result is the runtime itself.
/// </summary>
internal static class SpikeGolden
{
    private static readonly JsonSerializerOptions GoldenJson = new() { WriteIndented = true };

    /// <summary>Newline-normalise and trim trailing whitespace so on-disk vs in-memory text compares cleanly.</summary>
    public static string Normalize(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

    /// <summary>
    /// Project an analyser response to a stable, sorted JSON document. The same set of
    /// findings produces byte-identical output regardless of the order they were discovered.
    /// </summary>
    public static string FindingsToJson(IEnumerable<CodeIssueInfo> issues)
    {
        var projected = issues
            .Select(i => new SpikeFinding(i.RuleId, i.Severity, i.Line, i.Column, i.Message))
            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.Line)
            .ThenBy(f => f.Column)
            .ThenBy(f => f.Message, StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(projected, GoldenJson);
    }
}
