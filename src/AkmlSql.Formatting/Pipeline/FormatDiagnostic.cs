namespace AkmlSql.Formatting.Pipeline;

public enum DiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public class FormatDiagnostic
{
    public DiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Offset { get; set; } = -1;
    public int Line { get; set; } = -1;
}
