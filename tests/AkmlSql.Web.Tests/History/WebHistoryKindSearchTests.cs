using AkmlSql.Core;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.History;

/// <summary>
/// Spec 042 (task T112, FR-070, contracts/web-ui.md): the <c>kind:</c> term of the History search box and the saved connection
/// a restored entry is matched to. Pure logic; the page tests are in <see cref="HistoryPageKindTests"/>.
/// </summary>
public sealed class WebHistoryKindSearchTests
{
    [Theory]
    [InlineData("kind:postgresql", 1)]
    [InlineData("kind:pg", 1)]
    [InlineData("kind:postgres", 1)]
    [InlineData("KIND:PostgreSQL", 1)]
    [InlineData("Kind:PG", 1)]
    [InlineData("kind:sqlserver", 0)]
    [InlineData("kind:mssql", 0)]
    [InlineData("kind:tsql", 0)]
    [InlineData("kind:\"postgresql\"", 1)]
    public void A_kind_term_becomes_the_kind_filter_and_leaves_no_text(string search, int expected)
    {
        var parsed = WebHistoryLogic.ParseSearch(search);

        Assert.Equal(expected, parsed.Kind);
        Assert.Null(parsed.Text);
    }

    [Fact]
    public void The_rest_of_the_search_stays_as_the_text_with_single_spaces()
    {
        var parsed = WebHistoryLogic.ParseSearch("  orders   kind:pg  customers ");

        Assert.Equal(1, parsed.Kind);
        Assert.Equal("orders customers", parsed.Text);
    }

    [Fact]
    public void The_last_kind_term_wins()
    {
        Assert.Equal(0, WebHistoryLogic.ParseSearch("kind:pg kind:mssql").Kind);
    }

    [Theory]
    [InlineData("kind:oracle")]
    [InlineData("kind:")]
    [InlineData("kind")]
    [InlineData("orders")]
    [InlineData("kindness:pg")]
    public void Anything_that_is_not_a_known_kind_term_is_plain_text(string search)
    {
        var parsed = WebHistoryLogic.ParseSearch(search);

        Assert.Null(parsed.Kind);
        Assert.Equal(search, parsed.Text);
    }

    [Fact]
    public void A_kind_term_inside_a_quoted_phrase_is_part_of_the_phrase()
    {
        var parsed = WebHistoryLogic.ParseSearch("\"find kind:pg here\" orders");

        Assert.Null(parsed.Kind);
        Assert.Equal("\"find kind:pg here\" orders", parsed.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_search_has_neither_text_nor_kind(string? search)
    {
        var parsed = WebHistoryLogic.ParseSearch(search);

        Assert.Null(parsed.Text);
        Assert.Null(parsed.Kind);
    }

    // ---- the saved connection a restored entry points back to ----------------------------------------------------

    private static SavedSqlConnection Sql(string name, string server, string database) =>
        new() { Name = name, Kind = DatabaseKind.SqlServer, Server = server, Database = database };

    private static SavedSqlConnection Pg(string name, string host, int? port, string database) =>
        new() { Name = name, Kind = DatabaseKind.PostgreSql, Server = host, Port = port, Database = database };

    [Fact]
    public void A_PostgreSQL_entry_matches_the_saved_connection_by_host_port_and_database()
    {
        var saved = new[] { Sql("sql", "db1", "northwind"), Pg("other port", "db1", 5433, "northwind"), Pg("right", "DB1", null, "Northwind") };

        var match = WebHistoryLogic.FindConnection(saved, DatabaseKind.PostgreSql, "db1:5432", "northwind");

        Assert.Equal("right", match?.Name);
    }

    [Fact]
    public void A_SQL_Server_entry_matches_a_SQL_Server_connection_only()
    {
        var saved = new[] { Pg("pg", "db1", 5432, "northwind"), Sql("sql", "DB1", "NORTHWIND") };

        Assert.Equal("sql", WebHistoryLogic.FindConnection(saved, DatabaseKind.SqlServer, "db1", "northwind")?.Name);
    }

    [Fact]
    public void The_same_server_name_of_the_other_kind_is_never_a_match()
    {
        var saved = new[] { Sql("sql", "db1:5432", "northwind") };

        Assert.Null(WebHistoryLogic.FindConnection(saved, DatabaseKind.PostgreSql, "db1:5432", "northwind"));
    }

    [Fact]
    public void When_only_the_database_differs_the_server_still_matches_and_the_exact_one_wins_when_there_is_one()
    {
        var onlyServer = new[] { Pg("a", "db1", 5432, "postgres") };
        Assert.Equal("a", WebHistoryLogic.FindConnection(onlyServer, DatabaseKind.PostgreSql, "db1:5432", "northwind")?.Name);

        var both = new[] { Pg("a", "db1", 5432, "postgres"), Pg("b", "db1", 5432, "northwind") };
        Assert.Equal("b", WebHistoryLogic.FindConnection(both, DatabaseKind.PostgreSql, "db1:5432", "northwind")?.Name);
    }

    [Theory]
    [InlineData(null, "northwind")]
    [InlineData("", "northwind")]
    [InlineData("nowhere:5432", "northwind")]
    public void No_server_or_an_unknown_one_matches_nothing(string? server, string database)
    {
        var saved = new[] { Pg("a", "db1", 5432, "northwind") };

        Assert.Null(WebHistoryLogic.FindConnection(saved, DatabaseKind.PostgreSql, server, database));
    }

    [Theory]
    [InlineData(DatabaseKind.SqlServer, "SQL Server")]
    [InlineData(DatabaseKind.PostgreSql, "PostgreSQL")]
    public void The_kind_is_named_the_way_the_rest_of_the_product_names_it(DatabaseKind kind, string label)
    {
        Assert.Equal(label, WebHistoryLogic.KindLabel((int)kind));
    }
}
