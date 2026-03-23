namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Result returned by <see cref="FormatterPipeline.Format"/>. Contains the formatted (or original)
/// SQL, semantic validation status, any pipeline diagnostics, and elapsed time.
/// </summary>
public class FormatResult
{
    /// <summary><c>true</c> if the pipeline completed without an unhandled exception.</summary>
    public bool Success { get; set; }

    /// <summary>
    /// The formatted SQL text. If semantic validation failed, this equals the original input.
    /// </summary>
    public string FormattedText { get; set; } = string.Empty;

    /// <summary><c>true</c> if the formatted text differs from the original input.</summary>
    public bool WasModified { get; set; }

    /// <summary><c>true</c> if stage 6 (semantic validation) passed successfully.</summary>
    public bool ValidationPassed { get; set; }

    /// <summary>Pipeline diagnostics (e.g. FMT001 semantic failure, FMT002 idempotency warning).</summary>
    public FormatDiagnostic[] Diagnostics { get; set; } = [];

    /// <summary>Total elapsed wall-clock time for the format operation in milliseconds.</summary>
    public long ElapsedMs { get; set; }
}
