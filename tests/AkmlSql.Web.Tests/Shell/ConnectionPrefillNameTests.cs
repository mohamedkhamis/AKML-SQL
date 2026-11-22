using AkmlSql.Core;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 047 (WEB-004, contracts/connections-and-storage.md §2): reconnecting a restored tab that has no saved row used to
/// open the dialog with an empty Display name. It now starts from the name Save would give it, and says it is not saved.
/// </summary>
public sealed class ConnectionPrefillNameTests : BunitContext
{
    private readonly FakeSaveConnectionService _sql = new();
    private readonly SavedSqlConnectionStore _store = new(new InMemoryIndexedDbAdapter());
    private readonly ConnectionManagerController _controller = new();

    public ConnectionPrefillNameTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISqlConnectionService>(_sql);
        Services.AddSingleton<ISavedSqlConnectionStore>(_store);
        Services.AddSingleton<IConnectionManagerController>(_controller);
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
    }

    private async Task<IRenderedComponent<ConnectionManagerModal>> OpenForTabAsync(ConnectionBinding binding)
    {
        var cut = Render<ConnectionManagerModal>();
        await cut.InvokeAsync(() => _controller.Open(new SqlSessionState(), binding));
        cut.WaitForElement("[data-testid=connection-manager]");
        return cut;
    }

    [Fact]
    public async Task A_SQL_Server_tab_with_no_saved_row_starts_from_server_and_database()
    {
        var cut = await OpenForTabAsync(new ConnectionBinding
        {
            Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "master", WindowsAuth = false, Login = "sa",
        });

        Assert.Equal("localhost / master", cut.Find("[data-testid=conn-name-input]").GetAttribute("value"));
        Assert.Contains("Not a saved connection yet", cut.Find("[data-testid=conn-not-saved-note]").TextContent);
    }

    [Fact]
    public async Task A_PostgreSQL_tab_with_no_saved_row_starts_from_host_and_database()
    {
        _sql.EngineSupportsPostgres = true;
        var cut = await OpenForTabAsync(new ConnectionBinding
        {
            Kind = DatabaseKind.PostgreSql, Server = "db1", Port = 5432, Database = "sales", Login = "app",
        });

        Assert.Equal("db1 / sales", cut.Find("[data-testid=conn-name-input]").GetAttribute("value"));
    }

    [Fact]
    public async Task Save_keeps_the_proposed_name_and_the_note_goes_away()
    {
        var cut = await OpenForTabAsync(new ConnectionBinding
        {
            Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "master", WindowsAuth = true,
        });

        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal("localhost / master", Assert.Single(_store.ListAsync().GetAwaiter().GetResult()).Name));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=conn-not-saved-note]")));
    }

    [Fact]
    public async Task A_tab_whose_saved_row_still_matches_shows_the_rows_own_name_without_a_note()
    {
        var row = new SavedSqlConnection { Name = "Northwind (local)", Server = "localhost", Database = "Northwind", WindowsAuth = true };
        await _store.AddAsync(row);

        var cut = await OpenForTabAsync(new ConnectionBinding
        {
            SavedConnectionId = row.Id, Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "Northwind", WindowsAuth = true,
        });

        Assert.Equal("Northwind (local)", cut.Find("[data-testid=conn-name-input]").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid=conn-not-saved-note]"));
    }
}
