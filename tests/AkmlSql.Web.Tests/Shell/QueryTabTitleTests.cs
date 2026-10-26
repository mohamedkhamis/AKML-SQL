using AkmlSql.Core;
using EditorPage = AkmlSql.Web.Pages.Editor;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 043 (edge case "a saved connection that a tab uses is deleted"; review of US1, gap G3). A query tab's tooltip says
/// where it connects; a connection that is not one of the saved ones (its entry was deleted, or it was never saved) says
/// "not saved". The binding itself is untouched, so the tab still reconnects to the same place.
/// </summary>
public sealed class QueryTabTitleTests
{
    private static SqlSessionState Connected(string? savedId) => new()
    {
        IsConnected = true,
        Server = "localhost",
        Database = "Sales",
        Binding = new ConnectionBinding { SavedConnectionId = savedId, Server = "localhost", Database = "Sales" },
    };

    private static SavedSqlConnection Row(string id, string server = "localhost", string database = "Sales") =>
        new() { Id = id, Name = id, Server = server, Database = database, WindowsAuth = true };

    [Fact]
    public void A_connection_from_a_saved_entry_that_still_exists_says_nothing_more()
    {
        var title = EditorPage.TabTitle("SQLQuery1", Connected("c1"), new[] { Row("c1") });

        Assert.StartsWith("SQLQuery1 — localhost.Sales", title);
        Assert.DoesNotContain("not saved", title);
    }

    [Fact]
    public void A_connection_whose_saved_entry_was_deleted_says_not_saved_and_keeps_its_binding()
    {
        var session = Connected("c1");

        var title = EditorPage.TabTitle("SQLQuery1", session, new[] { Row("c2", server: "sql02") });

        Assert.StartsWith("SQLQuery1 — localhost.Sales (not saved)", title);
        Assert.Equal("c1", session.Binding!.SavedConnectionId);
        Assert.Equal("localhost", session.Binding.Server);
    }

    [Fact]
    public void A_connection_that_was_never_saved_says_not_saved()
    {
        Assert.StartsWith("SQLQuery1 — localhost.Sales (not saved)", EditorPage.TabTitle("SQLQuery1", Connected(null), Array.Empty<SavedSqlConnection>()));
    }

    [Fact]
    public void A_connection_saved_after_it_was_connected_says_nothing_more_even_in_another_database()
    {
        // Saved from the dialog after the connect (the tab's binding has no saved id), and the tab has moved database since.
        var title = EditorPage.TabTitle("SQLQuery1", Connected(null), new[] { Row("later", database: "master") });

        Assert.DoesNotContain("not saved", title);
    }

    [Fact]
    public void A_deleted_row_does_not_count_as_saved_through_a_row_for_another_database()
    {
        // "Local Sales" (the tab's row) was deleted; "Local master" is the same server and sign-in, another database.
        var title = EditorPage.TabTitle("SQLQuery1", Connected("local-sales"), new[] { Row("local-master", database: "master") });

        Assert.Contains("(not saved)", title);
    }

    [Fact]
    public void A_deleted_row_with_another_row_for_the_same_database_still_counts_as_saved()
    {
        Assert.DoesNotContain("not saved", EditorPage.TabTitle("SQLQuery1", Connected("gone"), new[] { Row("twin", database: "Sales") }));
    }

    [Theory]
    [InlineData("[::1]", null, "::1", 5432)]
    [InlineData("db1:5433", null, "db1", 5433)]
    [InlineData("DB1", 5433, "db1", 5433)]
    public void A_PostgreSQL_host_typed_with_brackets_or_a_port_is_its_saved_row(string typed, int? port, string savedHost, int savedPort)
    {
        var binding = new ConnectionBinding { Kind = DatabaseKind.PostgreSql, Server = typed, Port = port, Login = "app", Database = "sales" };
        var row = new SavedSqlConnection { Kind = DatabaseKind.PostgreSql, Server = savedHost, Port = savedPort, Login = "app", Database = "sales" };

        Assert.True(binding.SameConnectionAs(row));
    }

    [Fact]
    public void A_saved_entry_changed_to_another_sign_in_no_longer_counts()
    {
        var changed = Row("c1");
        changed.WindowsAuth = false;
        changed.Login = "app";

        Assert.Contains("(not saved)", EditorPage.TabTitle("SQLQuery1", Connected("c1"), new[] { changed }));
    }

    [Fact]
    public void A_restored_tab_not_connected_yet_says_both()
    {
        var session = new SqlSessionState { Binding = new ConnectionBinding { SavedConnectionId = "gone", Server = "sql02", Database = "Ops" } };

        Assert.StartsWith("Ops report — sql02.Ops (not connected) (not saved)", EditorPage.TabTitle("Ops report", session, Array.Empty<SavedSqlConnection>()));
    }

    [Fact]
    public void Nothing_is_said_while_the_saved_list_is_unknown_or_the_tab_has_no_connection()
    {
        Assert.DoesNotContain("not saved", EditorPage.TabTitle("SQLQuery1", Connected("c1"), null));
        Assert.StartsWith("SQLQuery1 · ", EditorPage.TabTitle("SQLQuery1", new SqlSessionState(), Array.Empty<SavedSqlConnection>()));
    }
}
