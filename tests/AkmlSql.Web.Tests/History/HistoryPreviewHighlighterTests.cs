using System.Linq;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.History;

public class HistoryPreviewHighlighterTests
{
    [Fact]
    public void Segments_ConcatenateToInputVerbatim()
    {
        const string sql = "SELECT a /* c */ FROM t WHERE x = 'lit' -- tail";
        var segs = HistoryPreviewHighlighter.BuildSegments(sql, "x");
        Assert.Equal(sql, string.Concat(segs.Select(s => s.Text)));
    }

    [Fact]
    public void Classifies_Keyword_String_Comment()
    {
        const string sql = "SELECT 'abc' -- note";
        var segs = HistoryPreviewHighlighter.BuildSegments(sql, null);
        Assert.Contains(segs, s => s.Kind == HistoryPreviewHighlighter.KindKeyword && s.Text == "SELECT");
        Assert.Contains(segs, s => s.Kind == HistoryPreviewHighlighter.KindString && s.Text == "'abc'");
        Assert.Contains(segs, s => s.Kind == HistoryPreviewHighlighter.KindComment && s.Text.StartsWith("--"));
    }

    [Fact]
    public void SearchTerm_ProducesHitSegment()
    {
        var segs = HistoryPreviewHighlighter.BuildSegments("SELECT Orders FROM Orders", "ord");
        Assert.Contains(segs, s => s.Hit && s.Text.Equals("Ord", System.StringComparison.Ordinal));
    }

    [Fact]
    public void NoSearch_HasNoHits()
    {
        var segs = HistoryPreviewHighlighter.BuildSegments("SELECT 1", null);
        Assert.All(segs, s => Assert.False(s.Hit));
    }

    [Fact]
    public void ExtractTerms_DropsPrefixFiltersAndBooleans()
    {
        var terms = HistoryPreviewHighlighter.ExtractTerms("server:db1 orders AND customers NOT foo name:q");
        Assert.Equal(new[] { "orders", "customers", "foo" }, terms);
    }

    [Fact]
    public void ExtractTerms_KeepsQuotedPhraseIntact()
    {
        var terms = HistoryPreviewHighlighter.ExtractTerms("\"two words\"");
        Assert.Equal(new[] { "two words" }, terms);
    }

    [Fact]
    public void ExtractTerms_DropsServerPrefixValue()
    {
        var terms = HistoryPreviewHighlighter.ExtractTerms("server:prod customers");
        Assert.DoesNotContain("prod", terms);
        Assert.Contains("customers", terms);
    }

    [Fact]
    public void ExtractTerms_DropsBooleanOperatorsCaseInsensitive()
    {
        var terms = HistoryPreviewHighlighter.ExtractTerms("and or not real And Or Not");
        Assert.Equal(new[] { "real" }, terms);
    }

    [Fact]
    public void ExtractTerms_KeepsSqlPrefixValueAndQuotedPrefixValue()
    {
        Assert.Equal(new[] { "orders" }, HistoryPreviewHighlighter.ExtractTerms("sql:orders"));
        Assert.Equal(new[] { "two words" }, HistoryPreviewHighlighter.ExtractTerms("sql:\"two words\""));
    }

    [Fact]
    public void ExtractTerms_DropsQuotedNonTextPrefixValue()
    {
        var terms = HistoryPreviewHighlighter.ExtractTerms("name:\"two words\" orders");
        Assert.Equal(new[] { "orders" }, terms);
    }

    [Fact]
    public void ExtractTerms_StripsTrailingWildcard()
    {
        var terms = HistoryPreviewHighlighter.ExtractTerms("orders*");
        Assert.Equal(new[] { "orders" }, terms);
    }
}
