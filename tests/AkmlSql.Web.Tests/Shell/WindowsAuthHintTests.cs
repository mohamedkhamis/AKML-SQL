using AkmlSql.Core;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Owner request of 2026-11-23 ("automatic like SSMS"): the Connect dialog says who Windows authentication signs in as — the
/// name the engine gives in its handshake: the Windows user of this browser on the engine's computer, else the engine's account.
/// Before, it never said, and Windows authentication silently meant NT AUTHORITY\SYSTEM for the installed service.
/// </summary>
public sealed class WindowsAuthHintTests : BunitContext
{
    private readonly FakeSaveConnectionService _sql = new();
    private readonly ConnectionManagerController _controller = new();

    public WindowsAuthHintTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISqlConnectionService>(_sql);
        Services.AddSingleton<ISavedSqlConnectionStore>(new SavedSqlConnectionStore(new InMemoryIndexedDbAdapter()));
        Services.AddSingleton<IConnectionManagerController>(_controller);
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
    }

    private async Task<string> HintAsync()
    {
        var cut = Render<ConnectionManagerModal>();
        await cut.InvokeAsync(() => _controller.Open(new SqlSessionState(), new ConnectionBinding
        {
            Kind = DatabaseKind.SqlServer, Server = "localhost", Database = "aqmar", WindowsAuth = true,
        }));
        return cut.WaitForElement("[data-testid=conn-auth-hint]").TextContent.Trim();
    }

    // The wording is tested on its own: rendering the dialog with a bridge registered started background work that slowed the
    // timing-based ReconnectLoopTests running beside it in the same suite.
    [Fact]
    public void It_names_who_the_engine_says_Windows_authentication_signs_in_as()
        => Assert.StartsWith(@"Windows authentication signs in as 1442-MOHAMEDKHA\Administrator.",
            ConnectionManagerModal.WindowsAuthHintFor(@"1442-MOHAMEDKHA\Administrator"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_name_it_keeps_the_general_advice(string? runsAs)
        => Assert.Equal("Windows authentication works for a SQL Server on the engine's own computer. For a remote server, use SQL Server authentication.",
            ConnectionManagerModal.WindowsAuthHintFor(runsAs));

    [Fact]
    public async Task The_dialog_shows_the_hint_when_Windows_authentication_is_chosen()
    {
        var hint = await HintAsync();

        Assert.Equal(ConnectionManagerModal.WindowsAuthHintFor(null), hint);
    }
}
