using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T037. Real implementation of the formatter service
/// for the Blazor WASM surface. Wraps <see cref="FormatterPipeline"/> directly -- no IPC
/// round-trip needed because the web edition runs the formatter in-process.
///
/// Profile management lives in <see cref="IProfileStore"/> (M2 T038). For tests and
/// scaffolding, a built-in default profile is used when no profile is supplied.
/// </summary>
public interface IFormatterService
{
    /// <summary>
    /// Format the given SQL using the supplied <paramref name="profile"/>. If
    /// <paramref name="profile"/> is null, the formatter uses a built-in default
    /// profile (matches the legacy "ANSI default" baseline that the IDE plugin ships).
    /// </summary>
    FormatResult Format(string sql, FormattingProfile? profile = null);
}

internal sealed class FormatterService : IFormatterService
{
    private readonly FormatterPipeline _pipeline = new();
    private readonly FormattingProfile _defaultProfile = new();
    private readonly IDialectGate? _gate;

    public FormatterService(IDialectGate? gate = null)
    {
        _gate = gate;
    }

    public FormatResult Format(string sql, FormattingProfile? profile = null)
    {
        // Spec 021 FR-011 (T042): refuse documents larger than the 10 MB limit.
        DocumentSizeLimit.EnsureWithinLimit(sql);

        // Spec 042 (FR-092): this is the T-SQL formatter; it never sees PostgreSQL text. PostgreSQL formatting runs in
        // the engine (story 7). Until it is routed the text comes back unchanged with the reason.
        if (_gate?.IsPostgres == true)
        {
            return new FormatResult
            {
                Success = true,
                FormattedText = sql ?? string.Empty,
                WasModified = false,
                ValidationPassed = true,
                Diagnostics = [new FormatDiagnostic { Severity = DiagnosticSeverity.Warning, Message = DialectMessages.NotAvailable }],
            };
        }
        return _pipeline.Format(sql ?? string.Empty, profile ?? _defaultProfile);
    }
}
