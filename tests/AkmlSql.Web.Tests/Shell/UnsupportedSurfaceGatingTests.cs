using AkmlSql.Core;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 042 (task T124, FR-092, SC-010). A surface with no PostgreSQL implementation (the refactorings, Find invalid objects) is
/// disabled on a PostgreSQL connection with the tooltip "Not available for PostgreSQL"; a command id that reaches it anyway, a
/// palette entry or a shortcut, is answered with the same words in the status area instead of running. SQL Server is untouched.
/// The page wiring is proven in the browser (<c>PostgresWorkspaceTests</c>); the in-browser formatter, analyser, offline scanner
/// and batch splitter are proven to stay out of a PostgreSQL session by <c>DialectGateTests</c>.
/// </summary>
public sealed class UnsupportedSurfaceGatingTests
{
    private sealed class Gate : IDialectGate
    {
        public Gate(DatabaseKind kind) => Kind = kind;
        public DatabaseKind Kind { get; }
        public bool IsPostgres => Kind == DatabaseKind.PostgreSql;
    }

    private static (SurfaceGate Surfaces, FakeNotificationService Notes) Create(DatabaseKind kind)
    {
        var notes = new FakeNotificationService();
        return (new SurfaceGate(new Gate(kind), notes), notes);
    }

    // ---- the policy -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkspaceSurface.Refactor)]
    [InlineData(WorkspaceSurface.FindInvalidObjects)]
    public void Every_unsupported_surface_is_off_for_PostgreSQL_and_on_for_SQL_Server(WorkspaceSurface surface)
    {
        Assert.False(UnsupportedSurfaces.IsAvailable(surface, DatabaseKind.PostgreSql));
        Assert.True(UnsupportedSurfaces.IsAvailable(surface, DatabaseKind.SqlServer));
        Assert.Equal("Not available for PostgreSQL", UnsupportedSurfaces.Reason(surface, DatabaseKind.PostgreSql));
        Assert.Null(UnsupportedSurfaces.Reason(surface, DatabaseKind.SqlServer));
    }

    [Fact]
    public void The_words_are_the_ones_the_engine_and_the_rest_of_the_product_use()
    {
        Assert.Equal(DialectMessages.NotAvailable, UnsupportedSurfaces.Reason(WorkspaceSurface.Refactor, DatabaseKind.PostgreSql));
        Assert.Equal("postgresql", UnsupportedSurfaces.Marker);
    }

    // ---- the page's helper ------------------------------------------------------------------------------------

    [Fact]
    public void On_a_PostgreSQL_connection_the_refactor_button_is_disabled_with_the_tooltip()
    {
        var (surfaces, _) = Create(DatabaseKind.PostgreSql);

        Assert.False(surfaces.IsEnabled(WorkspaceSurface.Refactor));
        Assert.Equal("Not available for PostgreSQL", surfaces.Tooltip(WorkspaceSurface.Refactor, "Refactor"));
        Assert.Equal("postgresql", surfaces.Unavailable(WorkspaceSurface.Refactor));
    }

    [Fact]
    public void On_a_SQL_Server_connection_the_button_keeps_its_own_title_and_nothing_is_marked()
    {
        var (surfaces, _) = Create(DatabaseKind.SqlServer);

        Assert.True(surfaces.IsEnabled(WorkspaceSurface.Refactor));
        Assert.Equal("Refactor (Ctrl+R)", surfaces.Tooltip(WorkspaceSurface.Refactor, "Refactor (Ctrl+R)"));
        Assert.Null(surfaces.Unavailable(WorkspaceSurface.Refactor));
    }

    [Fact]
    public void A_refactor_command_that_reaches_a_PostgreSQL_connection_says_why_it_did_not_run()
    {
        var (surfaces, notes) = Create(DatabaseKind.PostgreSql);

        var explained = surfaces.ExplainRefusal("editor:refactor");

        Assert.True(explained);
        var note = Assert.Single(notes.Sent);
        Assert.Equal("Not available for PostgreSQL", note.Text);
        Assert.Equal(NotificationKind.Info, note.Kind);
    }

    [Theory]
    [InlineData("editor:execute")]
    [InlineData("editor:format")]
    [InlineData("view:toggle-ai")]
    [InlineData("")]
    public void A_command_that_is_not_a_gated_surface_is_left_alone(string commandId)
    {
        var (surfaces, notes) = Create(DatabaseKind.PostgreSql);

        Assert.False(surfaces.ExplainRefusal(commandId));
        Assert.Empty(notes.Sent);
    }

    [Fact]
    public void On_SQL_Server_nothing_is_ever_explained()
    {
        var (surfaces, notes) = Create(DatabaseKind.SqlServer);

        Assert.False(surfaces.ExplainRefusal("editor:refactor"));
        Assert.Empty(notes.Sent);
    }

    [Fact]
    public void The_command_ids_map_to_their_surface()
    {
        Assert.Equal(WorkspaceSurface.Refactor, UnsupportedSurfaces.SurfaceOf("editor:refactor"));
        Assert.Null(UnsupportedSurfaces.SurfaceOf("editor:execute"));
    }
}
