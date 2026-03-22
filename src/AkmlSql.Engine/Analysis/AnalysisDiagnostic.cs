using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis;

public class AnalysisDiagnostic
{
    public string RuleId { get; set; } = string.Empty;
    public string CategoryCode { get; set; } = string.Empty;
    public DiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public AnalysisFixAction[] FixActions { get; set; } = [];
}
