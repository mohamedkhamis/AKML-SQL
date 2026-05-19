using System;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T042. Enforces FR-011: "The web edition MUST display a
/// clear error message and stop processing when the editor content exceeds the established
/// 10 MB per-document size limit." The limit matches the engine-side <c>MaxDocumentSizeChars</c>
/// in <c>SessionManager</c>.
///
/// Used by surfaces that accept arbitrary user input (FormatterService, AnalyserService,
/// EditorComponent.Paste handlers). Producing a single shared check keeps the behaviour
/// consistent across the M2 surface.
/// </summary>
public static class DocumentSizeLimit
{
    /// <summary>10 MiB in chars. Matches the engine's <c>MaxDocumentSizeChars</c> constant.</summary>
    public const int MaxDocumentSizeChars = 10 * 1024 * 1024;

    /// <summary>
    /// Throw <see cref="DocumentTooLargeException"/> if <paramref name="content"/> exceeds the
    /// 10 MB limit. Null and empty inputs are accepted (they are within the limit).
    /// </summary>
    public static void EnsureWithinLimit(string? content)
    {
        if (content is null) return;
        if (content.Length > MaxDocumentSizeChars)
        {
            throw new DocumentTooLargeException(content.Length, MaxDocumentSizeChars);
        }
    }
}

/// <summary>
/// Spec 021 M2 T042. Raised when a SQL document fed to a web-edition surface exceeds the
/// 10 MB per-document size limit (FR-011). Carries both the offending size and the limit
/// so the UI can render an informative error.
/// </summary>
public sealed class DocumentTooLargeException : Exception
{
    public int ActualSizeChars { get; }
    public int LimitChars { get; }

    public DocumentTooLargeException(int actualSizeChars, int limitChars)
        : base($"Document size {actualSizeChars:N0} chars exceeds the {limitChars:N0}-char limit (10 MB).")
    {
        ActualSizeChars = actualSizeChars;
        LimitChars = limitChars;
    }
}
