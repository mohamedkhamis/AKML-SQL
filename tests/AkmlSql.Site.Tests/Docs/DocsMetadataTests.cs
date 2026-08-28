using AkmlSql.Site.Docs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AkmlSql.Site.Tests.Docs;

/// <summary>
/// Docs freshness metadata (docs-metadata.json → New/Updated badges): tolerant parsing,
/// the inclusive freshness window (today and exactly N days ago count), and the
/// new-beats-updated precedence rule.
/// </summary>
public sealed class DocsMetadataTests
{
    private static readonly DateOnly Today = new(2026, 8, 28);
    private const int Window = 30;

    [Fact]
    public void Parse_ValidPayload_ResolvesDates_CaseInsensitivePathLookup()
    {
        var metadata = DocsMetadata.Parse("""
            {
              "generatedAt": "2026-08-28T16:00:00Z",
              "docs": {
                "topics/getting-started.md": { "added": "2026-08-28", "updated": "2026-08-28" }
              }
            }
            """);

        Assert.True(metadata.IsAvailable);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero), metadata.GeneratedAt);
        Assert.True(metadata.TryGet("TOPICS/Getting-Started.md", out var dates));
        Assert.Equal(new DocDates(Today, Today), dates);
    }

    [Fact]
    public void Parse_MalformedJson_YieldsEmpty()
    {
        Assert.Same(DocsMetadata.Empty, DocsMetadata.Parse("{ not json"));
        Assert.False(DocsMetadata.Parse("{ not json").IsAvailable);
    }

    [Fact]
    public void Parse_NonObjectRoot_YieldsEmpty()
    {
        // "[]" parses fine but TryGetProperty would throw InvalidOperationException —
        // it must collapse to Empty, not escape.
        Assert.False(DocsMetadata.Parse("[]").IsAvailable);
        Assert.False(DocsMetadata.Parse("42").IsAvailable);
    }

    [Fact]
    public void Parse_MissingDocsProperty_YieldsEmpty()
    {
        Assert.False(DocsMetadata.Parse("""{ "generatedAt": "2026-08-28T16:00:00Z" }""").IsAvailable);
    }

    [Fact]
    public void Parse_EntryWithBadDates_Skipped_ValidEntriesSurvive()
    {
        var metadata = DocsMetadata.Parse("""
            {
              "docs": {
                "good.md": { "added": "2026-08-01", "updated": "2026-08-02" },
                "bad-date.md": { "added": "yesterday", "updated": "2026-08-02" },
                "missing-field.md": { "added": "2026-08-01" },
                "not-an-object.md": "2026-08-01"
              }
            }
            """);

        Assert.True(metadata.IsAvailable);
        Assert.True(metadata.TryGet("good.md", out _));
        Assert.False(metadata.TryGet("bad-date.md", out _));
        Assert.False(metadata.TryGet("missing-field.md", out _));
        Assert.False(metadata.TryGet("not-an-object.md", out _));
    }

    [Fact]
    public void Parse_AllEntriesInvalid_YieldsEmpty()
    {
        Assert.Same(DocsMetadata.Empty, DocsMetadata.Parse("""{ "docs": { "a.md": { "added": "x", "updated": "y" } } }"""));
    }

    [Fact]
    public void TryGet_UnknownPath_ReturnsFalse()
    {
        var metadata = DocsMetadata.Parse("""{ "docs": { "a.md": { "added": "2026-08-01", "updated": "2026-08-01" } } }""");

        Assert.False(metadata.TryGet("b.md", out _));
    }

    [Fact]
    public void Load_MissingFile_YieldsEmpty()
    {
        var metadata = DocsMetadata.Load(new StubWebHostEnvironment(null));

        Assert.False(metadata.IsAvailable);
    }

    [Fact]
    public void Load_MalformedFile_YieldsEmpty()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "akml-docs-metadata-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        try
        {
            File.WriteAllText(Path.Combine(webRoot, DocsMetadata.MetadataFileName), "{ broken");

            var metadata = DocsMetadata.Load(new StubWebHostEnvironment(webRoot));

            Assert.False(metadata.IsAvailable);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public void ComputeBadge_AddedToday_IsNew()
    {
        Assert.Equal(DocBadge.New, DocsMetadata.ComputeBadge(new DocDates(Today, Today), Today, Window));
    }

    [Fact]
    public void ComputeBadge_AddedOnWindowEdge_IsNew()
    {
        // Inclusive window: exactly BadgeWindowDays ago still counts as fresh.
        var edge = Today.AddDays(-Window);

        Assert.Equal(DocBadge.New, DocsMetadata.ComputeBadge(new DocDates(edge, edge), Today, Window));
    }

    [Fact]
    public void ComputeBadge_BothDatesJustOutside_IsNone()
    {
        var outside = Today.AddDays(-(Window + 1));

        Assert.Equal(DocBadge.None, DocsMetadata.ComputeBadge(new DocDates(outside, outside), Today, Window));
    }

    [Fact]
    public void ComputeBadge_AddedOutside_UpdatedWithin_IsUpdated()
    {
        var dates = new DocDates(Today.AddDays(-90), Today.AddDays(-5));

        Assert.Equal(DocBadge.Updated, DocsMetadata.ComputeBadge(dates, Today, Window));
    }

    [Fact]
    public void ComputeBadge_UpdatedOnWindowEdge_IsUpdated()
    {
        var dates = new DocDates(Today.AddDays(-90), Today.AddDays(-Window));

        Assert.Equal(DocBadge.Updated, DocsMetadata.ComputeBadge(dates, Today, Window));
    }

    [Fact]
    public void ComputeBadge_NewBeatsUpdated_WhenBothWithinWindow()
    {
        // Precedence: a recently ADDED doc never shows "Updated", even if it also changed.
        var dates = new DocDates(Today.AddDays(-2), Today);

        Assert.Equal(DocBadge.New, DocsMetadata.ComputeBadge(dates, Today, Window));
    }

    /// <summary>Minimal IWebHostEnvironment stub; WebRootFileProvider is null-backed or physical.</summary>
    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string? webRootPath)
        {
            WebRootFileProvider = webRootPath is null ? new NullFileProvider() : new PhysicalFileProvider(webRootPath);
        }

        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "AkmlSql.Site.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; }
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
