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

    /// <summary>
    /// As <see cref="Format"/>, for a script of the active connection's language. A SQL Server script is formatted in the browser; a PostgreSQL
    /// script is sent to the engine (spec 042, FR-140). With no engine to ask, a PostgreSQL script comes back unchanged with the reason.
    /// </summary>
    Task<FormatResult> FormatAsync(string sql, FormattingProfile? profile = null, CancellationToken ct = default);

    /// <summary>The Format Styles page's preview of <paramref name="sample"/> with the style being edited, in the active connection's language.</summary>
    Task<FormatResult> PreviewAsync(string sample, FormattingProfile profile, CancellationToken ct = default);
}

internal sealed class FormatterService : IFormatterService
{
    private readonly FormatterPipeline _pipeline = new();
    private readonly FormattingProfile _defaultProfile = new();
    private readonly IDialectGate? _gate;
    private readonly IPostgresFormatEngine? _postgres;

    public FormatterService(IDialectGate? gate = null, IPostgresFormatEngine? postgres = null)
    {
        _gate = gate;
        _postgres = postgres;
    }

    public async Task<FormatResult> FormatAsync(string sql, FormattingProfile? profile = null, CancellationToken ct = default)
    {
        if (_gate?.IsPostgres == true && _postgres != null)
        {
            DocumentSizeLimit.EnsureWithinLimit(sql);
            return await _postgres.FormatAsync(sql ?? string.Empty, profile ?? _defaultProfile, ct).ConfigureAwait(false);
        }
        return Format(sql, profile);
    }

    public async Task<FormatResult> PreviewAsync(string sample, FormattingProfile profile, CancellationToken ct = default)
    {
        if (_gate?.IsPostgres == true && _postgres != null)
            return await _postgres.PreviewAsync(sample ?? string.Empty, profile, ct).ConfigureAwait(false);
        return Format(sample, profile);
    }

    public FormatResult Format(string sql, FormattingProfile? profile = null)
    {
        // Spec 021 FR-011 (T042): refuse documents larger than the 10 MB limit.
        DocumentSizeLimit.EnsureWithinLimit(sql);

        // Spec 042 (FR-092): this is the T-SQL formatter; it never sees PostgreSQL text. PostgreSQL formatting runs in
        // the engine (see FormatAsync); a caller that still asks here gets the text back unchanged with the reason.
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
