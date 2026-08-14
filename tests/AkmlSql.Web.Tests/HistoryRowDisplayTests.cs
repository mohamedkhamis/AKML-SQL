using AkmlSql.Core.Ipc.Messages;
using Xunit;

namespace AkmlSql.Web.Tests;

// NOTE: "History" is fully qualified below (AkmlSql.Web.Pages.History), not brought in via
// `using AkmlSql.Web.Pages;`. This test project already declares a nested namespace
// AkmlSql.Web.Tests.History (see tests/AkmlSql.Web.Tests/History/*.cs), and C# resolves an
// unqualified `History` to that sibling namespace before it ever consults using-directives —
// an unqualified reference here would fail with CS0234 ("DisplayNameFor does not exist in the
// namespace AkmlSql.Web.Tests.History").
public class HistoryRowDisplayTests
{
    [Fact]
    public void Display_name_is_the_session_name()
        => Assert.Equal("query-01", AkmlSql.Web.Pages.History.DisplayNameFor(new HistoryEntryDto
        {
            TabTitle = "query-01",
            SqlText = "SELECT * FROM dbo.Customers"
        }));

    [Fact]
    public void Falls_back_to_sql_only_when_unnamed()
        => Assert.StartsWith("SELECT", AkmlSql.Web.Pages.History.DisplayNameFor(new HistoryEntryDto
        {
            TabTitle = null,
            SqlText = "SELECT * FROM dbo.Customers"
        }));

    [Fact]
    public void Sql_fallback_collapses_whitespace_and_truncates_around_sixty_chars()
    {
        // A raw-SQL fallback only fires for a sessionless row; it must never dump multi-line,
        // untruncated SQL into the list (this regressed when DisplayNameFor stopped routing through
        // the old HistoryDisplayName.Of and returned the raw 500-char preview verbatim). Must match
        // AkmlSql.Shell.Shared.Tests.HistoryRowDisplayTests' twin test exactly — same helper contract.
        var name = AkmlSql.Web.Pages.History.DisplayNameFor(new HistoryEntryDto
        {
            TabTitle = null,
            SqlText = "SELECT   *\r\n  FROM   dbo.Customers\r\n  WHERE   CustomerId  =  @id  -- a comment that pushes this well past sixty characters"
        });

        Assert.DoesNotContain('\n', name);
        Assert.DoesNotContain('\r', name);
        Assert.DoesNotContain("  ", name); // no collapsed-double-space remnants
        Assert.True(name.Length <= 61, $"expected ~60 chars + ellipsis, got {name.Length}: '{name}'");
        Assert.EndsWith("…", name);
    }

    [Theory]
    [InlineData(1, 1, "")]                     // single run, single version — no noise
    [InlineData(276, 1, "×276")]
    [InlineData(276, 12, "×276 · 12 versions")]
    [InlineData(3, 2, "×3 · 2 versions")]
    public void Meta_line_summarises_runs_and_versions(int runs, int versions, string expected)
        => Assert.Equal(expected, AkmlSql.Web.Pages.History.MetaFor(runs, versions));
}
