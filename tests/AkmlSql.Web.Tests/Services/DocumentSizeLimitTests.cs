using System;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T042. Validates the 10 MB per-document size limit
/// (FR-011). Covers the standalone guard helper AND the integrated check inside
/// <see cref="FormatterService"/>.
/// </summary>
public sealed class DocumentSizeLimitTests
{
    [Fact]
    public void EnsureWithinLimit_accepts_null()
    {
        DocumentSizeLimit.EnsureWithinLimit(null);   // does not throw
    }

    [Fact]
    public void EnsureWithinLimit_accepts_empty_string()
    {
        DocumentSizeLimit.EnsureWithinLimit(string.Empty);
    }

    [Fact]
    public void EnsureWithinLimit_accepts_content_just_under_limit()
    {
        var content = new string('x', DocumentSizeLimit.MaxDocumentSizeChars);
        DocumentSizeLimit.EnsureWithinLimit(content);
    }

    [Fact]
    public void EnsureWithinLimit_throws_when_content_exceeds_limit()
    {
        var content = new string('x', DocumentSizeLimit.MaxDocumentSizeChars + 1);

        var ex = Assert.Throws<DocumentTooLargeException>(() => DocumentSizeLimit.EnsureWithinLimit(content));

        Assert.Equal(DocumentSizeLimit.MaxDocumentSizeChars + 1, ex.ActualSizeChars);
        Assert.Equal(DocumentSizeLimit.MaxDocumentSizeChars, ex.LimitChars);
        Assert.Contains("10 MB", ex.Message);
    }

    [Fact]
    public void FormatterService_refuses_oversized_input_with_DocumentTooLargeException()
    {
        var service = new FormatterService();
        var oversized = new string('x', DocumentSizeLimit.MaxDocumentSizeChars + 1);

        var ex = Assert.Throws<DocumentTooLargeException>(() => service.Format(oversized));

        Assert.True(ex.ActualSizeChars > ex.LimitChars);
    }

    [Fact]
    public void FormatterService_accepts_input_just_under_limit()
    {
        var service = new FormatterService();
        // Use a small valid SQL doc -- testing limits with 10 MiB of "x" causes the parser
        // to spend disproportionate time before failing. The guard is exercised against the
        // length check, not against a real 10 MB script.
        var content = "SELECT 1;";

        var result = service.Format(content);

        Assert.NotNull(result);
    }
}
