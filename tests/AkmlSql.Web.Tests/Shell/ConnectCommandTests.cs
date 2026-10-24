using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 043 (T077, FR-023, research R15). The product connects to SQL Server and PostgreSQL through one dialog, so the
/// command palette offers one way in: "Connect to database…" (id <c>sql:connect</c>). Spec 042's per-kind
/// "Connect to PostgreSQL…" entry is gone; the kind is chosen inside the dialog.
/// </summary>
public sealed class ConnectCommandTests : BunitContext
{
    private readonly CommandRegistry _registry = new();
    private readonly ConnectionManagerController _controller = new();

    public ConnectCommandTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<ICommandRegistry>(_registry);
        Services.AddSingleton<IThemeService>(new ThemeService(adapter, new NoopApplier()));
        Services.AddSingleton<ISchemaCacheStore>(new SchemaCacheStore(adapter));
        Services.AddSingleton<IConnectionManagerController>(_controller);
        Services.AddSingleton<IDialogService>(new FakeDialogService());
    }

    private sealed class NoopApplier : IThemeApplier
    {
        public Task ApplyAsync(ThemeMode mode) => Task.CompletedTask;
        public Task ForgetAsync() => Task.CompletedTask;
    }

    [Fact]
    public void Exactly_one_command_connects_and_it_is_Connect_to_database()
    {
        Render<CommandPalette>();

        var connect = _registry.Snapshot().Where(a => a.Title.StartsWith("Connect to", StringComparison.Ordinal)).ToList();

        var only = Assert.Single(connect);
        Assert.Equal("sql:connect", only.Id);
        Assert.Equal("Connect to database…", only.Title);
        Assert.DoesNotContain(_registry.Snapshot(), a => a.Id == "sql:connect-postgresql");
    }

    // The palette forces no kind: the dialog opens on the active tab's connection, SQL Server when it has none
    // (ConnectionManagerModalKindTests pins that side).
    [Fact]
    public async Task Running_it_opens_the_dialog_without_forcing_a_kind()
    {
        Render<CommandPalette>();
        bool opened = false;
        _controller.OpenRequested += () => opened = true;

        await _registry.Snapshot().Single(a => a.Id == "sql:connect").Run();

        Assert.True(opened);
        Assert.Null(_controller.RequestedKind);
    }
}
