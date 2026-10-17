using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Results;

/// <summary>
/// Spec 042 (task T172, FR-150): a finding that carries a documentation page - every PostgreSQL rule does - links to it from the Problems
/// tab. Only an http(s) address becomes a link; a finding without one looks as it did.
/// </summary>
public sealed class ProblemsListDocLinkTests : BunitContext
{
    private static CodeIssueInfo Issue(string rule, string url) => new()
    {
        RuleId = rule, Severity = 2, Message = "m", Line = 1, Column = 1, ReferenceUrl = url,
    };

    private IRenderedComponent<ProblemsListComponent> Render(params CodeIssueInfo[] issues) =>
        Render<ProblemsListComponent>(p => p.Add(c => c.Findings, new CodeAnalysisResponse { Issues = issues }));

    [Fact]
    public void A_finding_with_a_manual_page_links_to_it_in_a_new_tab_without_a_referrer()
    {
        var cut = Render(Issue("PGBP002", "https://www.postgresql.org/docs/18/sql-update.html"));

        var link = cut.Find("[data-testid=problem-doc]");

        Assert.Equal("https://www.postgresql.org/docs/18/sql-update.html", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Contains("PGBP002", link.GetAttribute("title"));
    }

    [Fact]
    public void A_finding_without_a_page_has_no_link()
    {
        var cut = Render(Issue("PE001", string.Empty));

        Assert.Empty(cut.FindAll("[data-testid=problem-doc]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=suppress-line]"));      // the row is otherwise as before
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("not a url")]
    [InlineData("/docs/relative")]
    public void Only_an_http_address_becomes_a_link(string url)
    {
        var cut = Render(Issue("PGBP002", url));

        Assert.Empty(cut.FindAll("[data-testid=problem-doc]"));
    }
}
