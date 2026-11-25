using AkmlSql.Core;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 047 (WEB-002, clarification Q1, contracts/connections-and-storage.md §1): Save with "Remember the password on the
/// engine machine" checks the login first and the engine keeps the password only when the server accepted it. The row is
/// always saved; the message says what happened to the password.
/// </summary>
public sealed class ConnectionSaveRememberTests : BunitContext
{
    private readonly FakeSaveConnectionService _sql = new();
    private readonly SavedSqlConnectionStore _store = new(new InMemoryIndexedDbAdapter());
    private readonly ConnectionManagerController _controller = new();

    public ConnectionSaveRememberTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ISqlConnectionService>(_sql);
        Services.AddSingleton<ISavedSqlConnectionStore>(_store);
        Services.AddSingleton<IConnectionManagerController>(_controller);
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
    }

    private async Task<IRenderedComponent<ConnectionManagerModal>> OpenAsync()
    {
        var cut = Render<ConnectionManagerModal>();
        await cut.InvokeAsync(() => _controller.Open());
        cut.WaitForElement("[data-testid=connection-manager]");
        return cut;
    }

    /// <summary>A new SQL-login form for sa on localhost, with the given password and Remember choice.</summary>
    private async Task<IRenderedComponent<ConnectionManagerModal>> SqlLoginFormAsync(string password, bool remember)
    {
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-server-input]").Input("localhost");
        cut.Find("[data-testid=conn-auth-sql]").Change(true);
        cut.Find("[data-testid=conn-login-input]").Input("sa");
        cut.Find("[data-testid=conn-login-input]").Change("sa");
        if (password.Length > 0) cut.Find("[data-testid=conn-password-input]").Input(password);
        var box = cut.Find("[data-testid=conn-sql-remember]");
        if (box.HasAttribute("checked") != remember) box.Change(remember);
        cut.Find("[data-testid=conn-name-input]").Input("Local sa");
        return cut;
    }

    private async Task<SavedSqlConnection> SavedRowAsync() => Assert.Single(await _store.ListAsync());

    private static string Message(IRenderedComponent<ConnectionManagerModal> cut) =>
        cut.Find("[data-testid=conn-message]").TextContent.Trim();

    [Fact]
    public async Task A_correct_password_with_Remember_is_checked_then_kept_by_the_engine()
    {
        var cut = await SqlLoginFormAsync("Secret#1", remember: true);
        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Saved. The engine machine keeps the password for sa.", Message(cut)));
        var target = Assert.Single(_sql.Remembers);
        Assert.Equal("Secret#1", target.Password);
        Assert.True(target.RememberPassword);
        Assert.Equal("sa", target.User);
        var row = await SavedRowAsync();
        Assert.True(row.RememberPassword);
        Assert.Equal("Local sa", row.Name);
        Assert.Equal("", cut.Find("[data-testid=conn-password-input]").GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task A_refused_login_is_saved_without_the_password_and_says_why()
    {
        _sql.RememberReply = new ConnectResult(false, "Login failed for user 'sa'.", ErrorCode: AkmlSql.Core.Ipc.Messages.DialectErrors.LoginFailed);
        var cut = await SqlLoginFormAsync("wrong", remember: true);
        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            "Saved without the password: the login test failed — Login failed for user 'sa'.", Message(cut)));
        Assert.False((await SavedRowAsync()).RememberPassword);
        Assert.False(_sql.SqlRemembered);
    }

    [Fact]
    public async Task An_accepted_login_the_engine_could_not_store_says_so()
    {
        _sql.RememberReply = new ConnectResult(true, Remembered: false);
        var cut = await SqlLoginFormAsync("Secret#1", remember: true);
        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Saved, but the engine could not keep the password.", Message(cut)));
        Assert.False((await SavedRowAsync()).RememberPassword);
    }

    [Fact]
    public async Task Remember_without_a_typed_password_saves_and_explains_how_to_keep_it()
    {
        var cut = await SqlLoginFormAsync("", remember: true);
        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            "Saved. Type the password once and Save again to keep it on the engine machine.", Message(cut)));
        Assert.Empty(_sql.Remembers);
        Assert.False((await SavedRowAsync()).RememberPassword);
    }

    [Fact]
    public async Task Remember_off_saves_without_asking_the_engine()
    {
        var cut = await SqlLoginFormAsync("Secret#1", remember: false);
        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", Message(cut)));
        Assert.Empty(_sql.Remembers);
        Assert.False((await SavedRowAsync()).RememberPassword);
    }

    [Fact]
    public async Task Unticking_Remember_on_a_row_that_had_it_makes_the_engine_forget_the_password()
    {
        var row = new SavedSqlConnection { Name = "Local sa", Server = "localhost", Database = "master", WindowsAuth = false, Login = "sa", RememberPassword = true };
        await _store.AddAsync(row);
        _sql.SqlRemembered = true;
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=conn-sql-forget]")));

        cut.Find("[data-testid=conn-sql-remember]").Change(false);
        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            "Saved. The engine machine no longer keeps the password for sa.", Message(cut)));
        Assert.Equal(("localhost", "sa"), Assert.Single(_sql.SqlForgets));
        Assert.False((await SavedRowAsync()).RememberPassword);
    }

    [Fact]
    public async Task Windows_authentication_saves_without_asking_the_engine()
    {
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-server-input]").Input("localhost");
        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", Message(cut)));
        Assert.Empty(_sql.Remembers);
        var saved = await SavedRowAsync();
        Assert.True(saved.WindowsAuth);
        Assert.False(saved.RememberPassword);
    }

    [Fact]
    public async Task A_second_Save_while_the_login_is_being_checked_is_ignored()
    {
        _sql.Gate = new TaskCompletionSource();
        var cut = await SqlLoginFormAsync("Secret#1", remember: true);
        cut.Find("[data-testid=conn-save-btn]").Click();
        cut.WaitForAssertion(() => Assert.Single(_sql.Remembers));

        cut.Find("[data-testid=conn-save-btn]").Click();
        _sql.Gate.SetResult();

        cut.WaitForAssertion(() => Assert.Equal("Saved. The engine machine keeps the password for sa.", Message(cut)));
        Assert.Single(_sql.Remembers);
        Assert.Single(await _store.ListAsync());
    }

    [Fact]
    public async Task Without_an_engine_that_keeps_passwords_Save_keeps_the_rows_flag_and_says_nothing_was_checked()
    {
        var row = new SavedSqlConnection { Name = "Local sa", Server = "localhost", Database = "master", WindowsAuth = false, Login = "sa", RememberPassword = true };
        await _store.AddAsync(row);
        _sql.EngineRemembersSqlServerPasswords = false;   // offline, or an engine without credentials.sqlserver.v1
        var cut = await OpenAsync();
        cut.Find("[data-testid=conn-row-pick]").Click();
        cut.Find("[data-testid=conn-password-input]").Input("Secret#1");
        cut.Find("[data-testid=conn-save-btn]").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            "Saved. No engine that keeps passwords is connected, so the password was not checked or kept.", Message(cut)));
        Assert.Empty(_sql.Remembers);
        Assert.True((await SavedRowAsync()).RememberPassword);   // a temporary outage never erases the user's choice
    }

    [Fact]
    public async Task The_remember_option_says_the_login_is_checked_when_saving()
    {
        var cut = await SqlLoginFormAsync("", remember: true);
        var help = cut.Find("[data-testid=conn-sql-remember-help]").TextContent;
        Assert.Contains("checked when you save", help);
        Assert.Contains("never in the browser", help);
    }
}
