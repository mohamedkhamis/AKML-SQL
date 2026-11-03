using System.Reflection;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// UI scan 2026-11-03 (W1): Format runs the analyser afterwards (Settings › Code analysis › "Analyse after Format"), and that
/// run must never move the results pane off the tab the user was looking at — only an explicit Analyse opens Problems.
/// The page is driven as <see cref="EditorLifetimeTests"/> drives it: initialised without a renderer (its trailing
/// StateHasChanged throws, after the decisions under test were made), with an <see cref="EditorComponent"/> that has no
/// CodeMirror behind it and so acts as an in-memory editor (its text, replace-if-unchanged and diagnostics all work without JS).
/// </summary>
public sealed class EditorFormatAnalysisTests
{
    private const string Sql = "select * from dbo.t";
    private const string Formatted = "SELECT *\nFROM dbo.t;";

    private sealed class DrivableEditor : AkmlSql.Web.Pages.Editor
    {
        private static readonly Type PageType = typeof(AkmlSql.Web.Pages.Editor);
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public Task InitializeAsync() => OnInitializedAsync();

        public void Inject(string name, object value) => PageType.GetProperty(name, Private)!.SetValue(this, value);

        public void SetEditor(EditorComponent editor) => PageType.GetField("_editor", Private)!.SetValue(this, editor);

        public string ResultsTab => (string)PageType.GetField("_resultsTab", Private)!.GetValue(this)!;

        public Task FormatAsync() => (Task)PageType.GetMethod("FormatAsync", Private)!.Invoke(this, null)!;

        public Task AnalyseAsync() => (Task)PageType.GetMethod("AnalyseAsync", Private)!.Invoke(this, null)!;
    }

    /// <summary>Formats every document to <see cref="Formatted"/>.</summary>
    private sealed class CanonicalFormatter : IFormatterService
    {
        public FormatResult Format(string sql, FormattingProfile? profile = null) =>
            new() { Success = true, FormattedText = Formatted, WasModified = sql != Formatted, ValidationPassed = true };

        public Task<FormatResult> FormatAsync(string sql, FormattingProfile? profile = null, CancellationToken ct = default) =>
            Task.FromResult(Format(sql, profile));

        public Task<FormatResult> PreviewAsync(string sample, FormattingProfile profile, CancellationToken ct = default) =>
            Task.FromResult(Format(sample, profile));
    }

    /// <summary>Finds one problem in every document.</summary>
    private sealed class OneFinding : IAnalyserService
    {
        public int Runs { get; private set; }

        public Task<CodeAnalysisResponse> AnalyseAsync(string documentText, AkmlSql.Engine.Schema.DatabaseCache? schemaCache = null, CancellationToken ct = default)
        {
            Runs++;
            return Task.FromResult(new CodeAnalysisResponse
            {
                Issues = new[] { new CodeIssueInfo { RuleId = "PE001", Message = "Avoid SELECT *", Line = 1, Column = 8, Severity = 2 } },
            });
        }
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

    private sealed class Page : IDisposable
    {
        public SettingsPageHost Host { get; } = new();
        public DrivableEditor Editor { get; } = new();
        public QueryTabs Tabs { get; }
        public OneFinding Analyser { get; } = new();
        public QueryTab Tab => Tabs.Active!;

        public Page()
        {
            var session = Host.Ctx.Services.GetRequiredService<IEditorSessionStore>();
            Tabs = new QueryTabs(session, Host.Sql, Host.Dialogs, Host.Notifications, new NoDownloads());
            Editor.Inject("Nav", Host.Nav);
            Editor.Inject("Theme", Host.Stores.Theme);
            Editor.Inject("Layout", new QuietLayout());
            Editor.Inject("SessionStore", session);
            Editor.Inject("QueryTabs", Tabs);
            Editor.Inject("Workspace", new WorkspaceStatus());
            Editor.Inject("ProfileStore", Host.Stores.Profiles);
            Editor.Inject("AnalysisSettings", Host.Stores.Analysis);
            Editor.Inject("Diagnostics", Host.Diagnostics);
            Editor.Inject("SqlConn", Host.Sql);
            Editor.Inject("ExecSettings", new ReadySettings());
            Editor.Inject("Bridge", Host.Bridge);
            Editor.Inject("ConnManager", new ConnectionManagerController());
            Editor.Inject("Services", new ServiceCollection().BuildServiceProvider());
            Editor.Inject("Commands", new CommandRegistry());
            Editor.Inject("Notifications", Host.Notifications);
            Editor.Inject("Formatter", new CanonicalFormatter());
            Editor.Inject("Analyser", Analyser);
        }

        /// <summary>The page as opened, showing <see cref="Sql"/> in its first query tab, with the results pane on Results.</summary>
        public async Task OpenAsync()
        {
            // The last step of initialisation hands the reconnect to the renderer, which this bare page has none of.
            try { await Editor.InitializeAsync(); }
            catch (InvalidOperationException) { }
            Assert.NotNull(Tabs.Active);

            var editor = new EditorComponent();
            await editor.SwitchTabAsync(Tab.Id, Sql, Tab.Session.Kind);
            Tab.Text = Sql;
            Editor.SetEditor(editor);
            Assert.Equal(ResultsPaneComponent.ResultsTab, Editor.ResultsTab);
        }

        /// <summary>Run a page action to its end: the trailing StateHasChanged of a page without a renderer throws, after the work.</summary>
        public static async Task RunAsync(Func<Task> action)
        {
            try { await action(); }
            catch (InvalidOperationException) { }
        }

        public void Dispose() => Host.Dispose();
    }

    [Fact]
    public async Task Format_analyses_the_document_but_keeps_the_results_tab_the_user_was_on()
    {
        using var page = new Page();
        await page.OpenAsync();

        await Page.RunAsync(page.Editor.FormatAsync);

        Assert.Equal(Formatted, page.Tab.Text);                       // the format was applied
        Assert.Equal(1, page.Analyser.Runs);                           // and the analyser ran after it
        Assert.NotNull(page.Tab.Findings);
        Assert.NotEmpty(page.Tab.Findings!.Issues);                    // with findings, which would otherwise open Problems
        Assert.Equal(ResultsPaneComponent.ResultsTab, page.Editor.ResultsTab);
    }

    [Fact]
    public async Task Analyse_itself_still_opens_Problems_when_there_are_findings()
    {
        using var page = new Page();
        await page.OpenAsync();

        await Page.RunAsync(page.Editor.AnalyseAsync);

        Assert.Equal(1, page.Analyser.Runs);
        Assert.NotEmpty(page.Tab.Findings!.Issues);
        Assert.Equal(ResultsPaneComponent.ProblemsTab, page.Editor.ResultsTab);
    }
}
