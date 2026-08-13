using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Shell-side twin of AkmlSql.Web.Tests.HistoryRowDisplayTests — same two pure helpers,
    /// duplicated (not shared) because AkmlSql.Shell.Shared is a net472 shared .projitems compiled
    /// into six VS-SDK-specific assemblies and cannot reference the net10.0 AkmlSql.Web Blazor project.
    /// </summary>
    public class HistoryRowDisplayTests
    {
        [Fact]
        public void Display_name_is_the_session_name()
            => Assert.Equal("query-01", HistoryRowDisplay.DisplayNameFor(new HistoryEntryDto
            {
                TabTitle = "query-01",
                SqlText = "SELECT * FROM dbo.Customers"
            }));

        [Fact]
        public void Falls_back_to_sql_only_when_unnamed()
            => Assert.StartsWith("SELECT", HistoryRowDisplay.DisplayNameFor(new HistoryEntryDto
            {
                TabTitle = null,
                SqlText = "SELECT * FROM dbo.Customers"
            }));

        [Theory]
        [InlineData(1, 1, "")]                     // single run, single version — no noise
        [InlineData(276, 1, "×276")]
        [InlineData(276, 12, "×276 · 12 versions")]
        [InlineData(3, 2, "×3 · 2 versions")]
        public void Meta_line_summarises_runs_and_versions(int runs, int versions, string expected)
            => Assert.Equal(expected, HistoryRowDisplay.MetaFor(runs, versions));
    }
}
