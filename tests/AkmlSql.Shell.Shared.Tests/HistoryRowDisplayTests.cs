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

        [Fact]
        public void Sql_fallback_collapses_whitespace_and_truncates_around_sixty_chars()
        {
            // A raw-SQL fallback only fires for a sessionless row; it must never dump multi-line,
            // untruncated SQL into the list (this regressed when DisplayNameFor stopped routing
            // through the old HistoryDisplayName.Of and returned the raw 500-char preview verbatim).
            var name = HistoryRowDisplay.DisplayNameFor(new HistoryEntryDto
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
            => Assert.Equal(expected, HistoryRowDisplay.MetaFor(runs, versions));

        // Spec 040 (HIS-13): a query that was never run reads "Not executed".
        [Theory]
        [InlineData(0, 1, 3, "Not executed")]
        [InlineData(0, 4, 3, "Not executed · 4 versions")]
        [InlineData(5, 1, 0, "×5")]
        public void Drafts_read_not_executed(int runs, int versions, int status, string expected)
            => Assert.Equal(expected, HistoryRowDisplay.MetaFor(runs, versions, status));

        // Spec 040 (HIS-05): the row's second line names the server and the database.
        [Theory]
        [InlineData("(local)", "Northwind", "(local) · Northwind")]
        [InlineData("(local)", null, "(local)")]
        [InlineData(null, "Northwind", "Northwind")]
        [InlineData(null, null, "")]
        public void Connection_label_shows_server_and_database(string? server, string? database, string expected)
            => Assert.Equal(expected, HistoryRowDisplay.ConnectionLabel(server, database));

        [Fact]
        public void Rows_are_grouped_like_SQL_Prompt()
        {
            var now = new System.DateTime(2026, 9, 30, 15, 0, 0, System.DateTimeKind.Local); // a Wednesday
            string At(System.DateTime local) => local.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal("Today", HistoryRowDisplay.DateGroupFor(At(now.AddHours(-2)), now));
            Assert.Equal("Yesterday", HistoryRowDisplay.DateGroupFor(At(now.AddDays(-1)), now));
            Assert.Equal("Older", HistoryRowDisplay.DateGroupFor(At(now.AddYears(-1)), now));
            Assert.Equal("Older", HistoryRowDisplay.DateGroupFor("not a date", now));
        }
    }
}
