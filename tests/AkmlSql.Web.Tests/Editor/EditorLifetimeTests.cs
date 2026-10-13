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
        editor.Inject("Document", new EditorDocumentFlow(session, host.Dialogs, host.Notifications, new NoDownloads()));
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
