using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 041 (T024, research R38, FR-027/FR-042) — a command can say it is not available right now
/// (Execute while a query runs) and whether it is on (a visible panel). The command palette shows a
/// disabled command but never runs it, and marks a checked command. Commands that say neither behave
/// exactly as before.
/// </summary>
public sealed class CommandRegistryStateTests : BunitContext
{
    private readonly CommandRegistry _registry = new();

    public CommandRegistryStateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<ICommandRegistry>(_registry);
        Services.AddSingleton<IThemeService>(new ThemeService(adapter, new NoopApplier()));
        _schemaCache = new SchemaCacheStore(adapter);
        Services.AddSingleton<ISchemaCacheStore>(_schemaCache);
        Services.AddSingleton<IConnectionManagerController>(new ConnectionManagerController());
        Services.AddSingleton<IDialogService>(_dialogs);
    }

    private readonly FakeDialogService _dialogs = new();
    private readonly SchemaCacheStore _schemaCache;

    private sealed class NoopApplier : IThemeApplier
    {
        public Task ApplyAsync(ThemeMode mode) => Task.CompletedTask;
        public Task ForgetAsync() => Task.CompletedTask;
    }

    private IRenderedComponent<CommandPalette> OpenPalette(string query)
    {
        var cut = Render<CommandPalette>();
        cut.InvokeAsync(() => cut.Instance.Open(commandMode: true));
        cut.WaitForElement("[data-testid=command-palette-input]").Input(query);
        return cut;
    }

    [Fact]
    public void A_command_with_no_state_is_enabled_and_unchecked_by_default()
    {
        var action = new CommandAction { Id = "x", Title = "Plain", Group = CommandGroup.Action, Run = () => Task.CompletedTask };

        Assert.True(action.IsAvailable);
        Assert.False(action.IsOn);
    }

    [Fact]
    public void A_disabled_command_is_shown_disabled_and_Enter_does_not_run_it()
    {
        int runs = 0;
        _registry.Register(new[]
        {
            new CommandAction
            {
                Id = "editor:execute", Title = "Execute", Group = CommandGroup.Action, Shortcut = "F5",
                IsEnabled = () => false,
                Run = () => { runs++; return Task.CompletedTask; },
            },
        });

        var cut = OpenPalette("Execute");

        var row = cut.WaitForElement("[data-testid=command-palette-row]");
        Assert.Equal("true", row.GetAttribute("aria-disabled"));
        cut.Find("[data-testid=command-palette-input]").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        row.Click();

        Assert.Equal(0, runs);
        Assert.NotEmpty(cut.FindAll("[data-testid=command-palette]"));   // still open
    }

    [Fact]
    public void An_enabled_command_runs_from_Enter()
    {
        int runs = 0;
        _registry.Register(new[]
        {
            new CommandAction
            {
                Id = "editor:execute", Title = "Execute", Group = CommandGroup.Action,
                IsEnabled = () => true,
                Run = () => { runs++; return Task.CompletedTask; },
            },
        });

        var cut = OpenPalette("Execute");
        cut.WaitForElement("[data-testid=command-palette-row]");
        cut.Find("[data-testid=command-palette-input]").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal(1, runs));
    }

    [Fact]
    public void A_checked_command_shows_a_check_mark()
    {
        _registry.Register(new[]
        {
            new CommandAction { Id = "view:toggle-schema", Title = "Schema panel", Group = CommandGroup.Action, IsChecked = () => true, Run = () => Task.CompletedTask },
            new CommandAction { Id = "view:toggle-ai", Title = "AI panel", Group = CommandGroup.Action, IsChecked = () => false, Run = () => Task.CompletedTask },
        });

        var cut = OpenPalette("panel");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[data-testid=command-palette-row]");
            Assert.Equal(2, rows.Count);
            var schema = rows.Single(r => r.TextContent.Contains("Schema panel"));
            var ai = rows.Single(r => r.TextContent.Contains("AI panel"));
            Assert.Equal("true", schema.GetAttribute("aria-checked"));
            Assert.Equal("false", ai.GetAttribute("aria-checked"));
            Assert.NotNull(schema.QuerySelector("use[href$='akml-icon-check']"));
            Assert.Null(ai.QuerySelector("use[href$='akml-icon-check']"));
        });
    }

    [Fact]
    public void A_command_without_IsChecked_carries_no_checked_state()
    {
        _registry.Register(new[]
        {
            new CommandAction { Id = "editor:format", Title = "Format SQL", Group = CommandGroup.Action, Run = () => Task.CompletedTask },
        });

        var cut = OpenPalette("Format");

        var row = cut.WaitForElement("[data-testid=command-palette-row]");
        Assert.Null(row.GetAttribute("aria-checked"));
        Assert.Null(row.GetAttribute("aria-disabled"));
    }

    [Fact]
    public void A_throwing_state_delegate_reads_as_disabled_and_unchecked()
    {
        var action = new CommandAction
        {
            Id = "x", Title = "x", Group = CommandGroup.Action,
            IsEnabled = () => throw new InvalidOperationException(),
            IsChecked = () => throw new InvalidOperationException(),
            Run = () => Task.CompletedTask,
        };

        Assert.False(action.IsAvailable);
        Assert.False(action.IsOn);
    }

    // Spec 041 (T185, R59, SC-012) — clearing every cached schema asks first, in the app.
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Clear_schema_cache_asks_first_and_declining_keeps_the_cache(bool answer, int left)
    {
        await _schemaCache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "srv", DatabaseName = "db", Checksum = "x" });
        _dialogs.ConfirmAnswer = _ => answer;
        var cut = OpenPalette("Clear schema cache");

        cut.WaitForElement("[data-testid=command-palette-row]").Click();

        cut.WaitForAssertion(() => Assert.Single(_dialogs.Confirms));
        var confirm = _dialogs.Confirms[0];
        Assert.Equal("Clear schema cache", confirm.Title);
        Assert.Equal("Clear", confirm.ConfirmLabel);
        Assert.True(confirm.Destructive);
        await cut.InvokeAsync(() => Task.Delay(50));
        Assert.Equal(left, (await _schemaCache.ListAsync()).Count);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier is "confirm" or "prompt" or "alert");
    }
}
