using AkmlSql.Site.Docs;
using Xunit;

namespace AkmlSql.Site.Tests.Docs;

/// <summary>
/// DOC-004: previous/next navigation. The order must be the SIDEBAR's order — section order,
/// then document order within a section — so "next" always means "the next entry down the tree"
/// rather than some other sequence a reader never sees.
/// </summary>
public sealed class DocsNavigationTests
{
    private static Document Doc(string slug, string section, int order = int.MaxValue) =>
        new()
        {
            Title = slug,
            Slug = slug,
            SourcePath = slug + ".md",
            Section = section,
            Order = order,
        };

    /// <summary>Two sections, pinned in a non-alphabetical order, each with ordered documents.</summary>
    private static DocsContentService Corpus() =>
        DocsContentService.Create(
            [
                Doc("guides/second", "User Guides", 2),
                Doc("guides/first", "User Guides", 1),
                Doc("reference/beta", "Reference", 2),
                Doc("reference/alpha", "Reference", 1),
            ],
            sectionOrder: ["User Guides", "Reference"]);

    [Fact]
    public void ReadingOrder_FollowsSectionOrder_ThenDocumentOrder()
    {
        var order = Corpus().ReadingOrder.Select(d => d.Slug).ToArray();

        Assert.Equal(["guides/first", "guides/second", "reference/alpha", "reference/beta"], order);
    }

    [Fact]
    public void Neighbours_LinkAcrossASectionBoundary()
    {
        var docs = Corpus();
        var last = docs.FindBySlug("guides/second")!;

        var (previous, next) = docs.Neighbours(last);

        Assert.Equal("guides/first", previous?.Slug);
        // The reader continues into the next section rather than hitting a dead end.
        Assert.Equal("reference/alpha", next?.Slug);
    }

    [Fact]
    public void Neighbours_OmitPreviousAtTheStartAndNextAtTheEnd()
    {
        var docs = Corpus();

        var (firstPrev, firstNext) = docs.Neighbours(docs.FindBySlug("guides/first")!);
        Assert.Null(firstPrev);
        Assert.Equal("guides/second", firstNext?.Slug);

        var (lastPrev, lastNext) = docs.Neighbours(docs.FindBySlug("reference/beta")!);
        Assert.Equal("reference/alpha", lastPrev?.Slug);
        Assert.Null(lastNext);
    }

    [Fact]
    public void Neighbours_OfASingleDocumentCorpus_AreBothNull()
    {
        var docs = DocsContentService.Create([Doc("only", "Guides")]);

        var (previous, next) = docs.Neighbours(docs.FindBySlug("only")!);

        Assert.Null(previous);
        Assert.Null(next);
    }

    [Fact]
    public void Neighbours_OfADocumentNotInTheCorpus_AreBothNull()
    {
        var (previous, next) = Corpus().Neighbours(Doc("stranger", "Elsewhere"));

        Assert.Null(previous);
        Assert.Null(next);
    }
}
