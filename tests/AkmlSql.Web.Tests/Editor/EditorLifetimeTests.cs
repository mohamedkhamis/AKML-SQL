using System.Reflection;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

public sealed class EditorLifetimeTests
{
    private sealed class InitializingEditor : AkmlSql.Web.Pages.Editor
    {
        public Task InitializeAsync() => OnInitializedAsync();
        public void Inject(string name, object value) => typeof(AkmlSql.Web.Pages.Editor)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(this, value);
    }

    private sealed class DelayedSettings : IExecutionSettingsStore
    {
        public TaskCompletionSource<ExecutionSettings> Pending { get; } = new();
        public SessionOverride? SessionOverride { get; set; }
        public Task<ExecutionSettings> GetEffectiveAsync() => Pending.Task;
        public Task<ExecutionSettings> GetAsync() => Pending.Task;
        public Task SetAsync(ExecutionSettings settings) => Task.CompletedTask;
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
    }

    private sealed class NoDownloads : IFileDownloader
    {
        public Task DownloadTextAsync(string fileName, string mimeType, string text, bool withBom) => Task.CompletedTask;
    }

    private sealed class QuietLayout : IWorkspaceLayoutStore
    {
        public WorkspaceLayout Current => WorkspaceLayout.Default;
        public event Action? Changed { add { } remove { } }
        public Task<WorkspaceLayout> LoadAsync() => Task.FromResult(Current);
        public Task SetAsync<T>(string field, T value) => Task.CompletedTask;
        public Task ResetAsync() => Task.CompletedTask;
    }

    private sealed class ReadySettings : IExecutionSettingsStore
    {
        public SessionOverride? SessionOverride { get; set; }
        public Task<ExecutionSettings> GetEffectiveAsync() => Task.FromResult(new ExecutionSettings());
        public Task<ExecutionSettings> GetAsync() => Task.FromResult(new ExecutionSettings());
        public Task SetAsync(ExecutionSettings settings) => Task.CompletedTask;
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
    }

    /// <summary>The saved connections, read from IndexedDB: the read yields until the test answers it.</summary>
    private sealed class DelayedSavedConnections : ISavedSqlConnectionStore
    {
        public TaskCompletionSource<IReadOnlyList<SavedSqlConnection>> Pending { get; } = new();
        public Task<IReadOnlyList<SavedSqlConnection>> ListAsync() => Pending.Task;
        public Task<SavedSqlConnection?> GetAsync(string id) => Task.FromResult<SavedSqlConnection?>(null);
        public Task AddAsync(SavedSqlConnection connection) => Task.CompletedTask;
        public Task UpdateAsync(SavedSqlConnection connection) => Task.CompletedTask;
        public Task RemoveAsync(string id) => Task.CompletedTask;
        public Task<string?> GetActiveIdAsync() => Task.FromResult<string?>(null);
        public Task SetActiveIdAsync(string? id) => Task.CompletedTask;
    }

    // Review of US1: the saved connections (for the tabs' "not saved" mark) are read in the background. Initialization does not
    // wait for them — the page's commands, and so its keys, are there at once — and a page left meanwhile keeps nothing registered.
    [Fact]
    public async Task The_saved_connections_are_read_without_holding_up_the_commands_and_a_page_left_meanwhile_keeps_none()
    {
        using var host = new SettingsPageHost();
        var registry = new CommandRegistry();
        var saved = new DelayedSavedConnections();
        var session = host.Ctx.Services.GetRequiredService<IEditorSessionStore>();
        var editor = new InitializingEditor();
        editor.Inject("Nav", host.Nav);
        editor.Inject("Theme", host.Stores.Theme);
        editor.Inject("Layout", new QuietLayout());
        editor.Inject("SessionStore", session);
        editor.Inject("QueryTabs", new QueryTabs(session, host.Sql, host.Dialogs, host.Notifications, new NoDownloads()));
        editor.Inject("Workspace", new WorkspaceStatus());
        editor.Inject("ProfileStore", host.Stores.Profiles);
        editor.Inject("AnalysisSettings", host.Stores.Analysis);
        editor.Inject("Diagnostics", host.Diagnostics);
        editor.Inject("SqlConn", host.Sql);
        editor.Inject("ExecSettings", new ReadySettings());
        editor.Inject("Bridge", host.Bridge);
        editor.Inject("ConnManager", new ConnectionManagerController());
        editor.Inject("Services", new ServiceCollection().AddSingleton<ISavedSqlConnectionStore>(saved).BuildServiceProvider());
        editor.Inject("Commands", registry);

        // Completes while the saved connections are still being read. Its very last step hands the reconnect to the renderer,
        // which this bare page has none of; everything before it ran.
        try { await editor.InitializeAsync(); }
        catch (InvalidOperationException) { }
        Assert.NotEmpty(registry.Snapshot());

        await editor.DisposeAsync();
        saved.Pending.SetResult(Array.Empty<SavedSqlConnection>());   // the read ends after the page is gone

        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task Leaving_during_initialization_does_not_register_commands_after_disposal()
    {
        using var host = new SettingsPageHost();
        var registry = new CommandRegistry();
        var settings = new DelayedSettings();
        var session = host.Ctx.Services.GetRequiredService<IEditorSessionStore>();
        var editor = new InitializingEditor();
        editor.Inject("Nav", host.Nav);
        editor.Inject("Theme", host.Stores.Theme);
        editor.Inject("Layout", new QuietLayout());
        editor.Inject("SessionStore", session);
        editor.Inject("QueryTabs", new QueryTabs(session, host.Sql, host.Dialogs, host.Notifications, new NoDownloads()));   // spec 043
        editor.Inject("Workspace", new WorkspaceStatus());
        editor.Inject("ProfileStore", host.Stores.Profiles);
        editor.Inject("AnalysisSettings", host.Stores.Analysis);
        editor.Inject("Diagnostics", host.Diagnostics);
        editor.Inject("SqlConn", host.Sql);
        editor.Inject("ExecSettings", settings);
        editor.Inject("Commands", registry);

        var initialization = editor.InitializeAsync();
        if (initialization.IsFaulted) await initialization;
        Assert.False(initialization.IsCompleted);
        await editor.DisposeAsync();
        settings.Pending.SetResult(new ExecutionSettings());
        await initialization;

        Assert.Empty(registry.Snapshot());
    }
}
