using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis;

public class AnalysisFixAction
{
    public string Label { get; set; } = string.Empty;
    public FixType FixType { get; set; }
    public int ReplacementStart { get; set; }
    public int ReplacementEnd { get; set; }
    public string ReplacementText { get; set; } = string.Empty;
    public string? SuppressRuleId { get; set; }
    public SuppressScope? SuppressScope { get; set; }
}
