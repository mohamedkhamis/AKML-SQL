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

    public FormatResult Format(string sql, FormattingProfile? profile = null)
    {
        // Spec 021 FR-011 (T042): refuse documents larger than the 10 MB limit.
        DocumentSizeLimit.EnsureWithinLimit(sql);
        return _pipeline.Format(sql ?? string.Empty, profile ?? _defaultProfile);
    }
}
