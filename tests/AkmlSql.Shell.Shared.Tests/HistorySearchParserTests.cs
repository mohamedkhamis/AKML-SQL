#nullable enable
using System;
using System.Linq;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T117, HIS-07) — SQL History's search box understands SQL Prompt's prefixes, including
    /// the new <c>path:</c> and <c>date:[… TO …]</c>; every example in the help popup parses; unknown
    /// prefixes stay free text.
    /// </summary>
    public sealed class HistorySearchParserTests
    {
        [Fact]
        public void Path_prefix_sets_the_path_filter()
        {
            var parsed = HistorySearchParser.Parse(@"path:Reports orders");
            Assert.Equal("Reports", parsed.PathFilter);
            Assert.Equal("orders", parsed.PlainTextQuery);
            Assert.True(parsed.HasPrefixes);
        }

        [Fact]
        public void Quoted_path_keeps_its_spaces()
            => Assert.Equal(@"C:\My Queries", HistorySearchParser.Parse("path:\"C:\\My Queries\"").PathFilter);

        [Fact]
        public void Date_range_sets_from_start_of_day_to_end_of_day()
        {
            var parsed = HistorySearchParser.Parse("date:[20260901 TO 20260927] customers");

            Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Local), parsed.DateFrom);
            Assert.Equal(new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Local).AddTicks(-1), parsed.DateTo);
            Assert.Equal("customers", parsed.PlainTextQuery);
        }

        [Theory]
        [InlineData("date:[20260901 TO *]", true, false)]
        [InlineData("date:[* TO 20260927]", false, true)]
        public void An_open_ended_date_range_sets_one_side(string query, bool hasFrom, bool hasTo)
        {
            var parsed = HistorySearchParser.Parse(query);
            Assert.Equal(hasFrom, parsed.DateFrom.HasValue);
            Assert.Equal(hasTo, parsed.DateTo.HasValue);
        }

        [Theory]
        [InlineData("date:[2026 TO soon]")]
        [InlineData("date:[20261399 TO 20261401]")]
        public void An_invalid_date_range_stays_free_text(string query)
        {
            var parsed = HistorySearchParser.Parse(query);
            Assert.Null(parsed.DateFrom);
            Assert.Null(parsed.DateTo);
            Assert.Contains("date:", parsed.PlainTextQuery);
        }

        [Fact]
        public void Unknown_prefixes_stay_free_text()
        {
            var parsed = HistorySearchParser.Parse("owner:dbo size:big");
            Assert.Equal("owner:dbo size:big", parsed.PlainTextQuery);
            Assert.False(parsed.HasPrefixes);
        }

        [Fact]
        public void Existing_prefixes_still_parse()
        {
            var parsed = HistorySearchParser.Parse("server:srv1 db:Northwind name:Monthly starred:true open:false sql:orders");
            Assert.Equal("srv1", parsed.ServerFilter);
            Assert.Equal("Northwind", parsed.DatabaseFilter);
            Assert.Equal("Monthly", parsed.NameFilter);
            Assert.True(parsed.StarredFilter);
            Assert.False(parsed.OpenFilter);
            Assert.Equal("orders", parsed.SqlFilter);
        }

        [Fact]
        public void Every_help_row_parses_to_something()
        {
            Assert.NotEmpty(HistorySearchParser.HelpRows);
            foreach (var (syntax, meaning) in HistorySearchParser.HelpRows)
            {
                Assert.False(string.IsNullOrWhiteSpace(meaning), syntax);
                var parsed = HistorySearchParser.Parse(syntax);
                var parsedSomething = parsed.HasPrefixes || parsed.PlainTextQuery.Length > 0 || parsed.DateFrom.HasValue;
                Assert.True(parsedSomething, $"'{syntax}' parsed to nothing");
            }
        }

        [Fact]
        public void The_help_lists_every_supported_form()
        {
            var syntaxes = string.Join(" ", HistorySearchParser.HelpRows.Select(r => r.Syntax));
            foreach (var form in new[] { "name:", "path:", "sql:", "server:", "database:", "db:", "starred:", "open:", "date:[", "\"", " OR ", "NOT ", "*" })
                Assert.Contains(form, syntaxes);
        }
    }
}
