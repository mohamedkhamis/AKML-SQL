namespace AkmlSql.Formatting.Pipeline;

public class FormatResult
{
    public bool Success { get; set; }
    public string FormattedText { get; set; } = string.Empty;
    public bool WasModified { get; set; }
    public bool ValidationPassed { get; set; }
    public FormatDiagnostic[] Diagnostics { get; set; } = [];
    public long ElapsedMs { get; set; }
}
