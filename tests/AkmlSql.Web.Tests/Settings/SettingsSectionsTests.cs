using AkmlSql.Web.Services;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 041 (T133, research R44, R51, FR-052–FR-057, FR-060, FR-064) — what each section does: changes apply at
/// once with a notification, destructive actions confirm through the in-app dialog, suppressed rules can be
/// undone, imported providers show "Needs key", connections never ask for a password, and loading, filtering or
/// switching sections writes nothing.
/// </summary>
public sealed class SettingsSectionsTests : IDisposable
{
    private readonly SettingsPageHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void General_applies_the_theme_at_once_with_a_notification()
    {
        var cut = _host.Open("settings");

        cut.Find("#setting-theme-control").Change("Dark");

        Assert.Equal(ThemeMode.Dark, _host.Stores.Theme.Current);
        Assert.Contains(_host.Notifications.Sent, n => n.Text == "Theme: Dark.");
    }

    [Fact]
    public async Task Queries_saves_the_default_row_limit_and_clears_the_toolbar_override()
    {
        _host.Stores.Queries.SessionOverride = new SessionOverride { MaxRows = 50 };
        var cut = _host.Open("settings/queries");

        cut.Find("#setting-queries-max-rows-control").Change("500");

        Assert.Equal(500, (await new ExecutionSettingsStore(_host.Stores.Db).GetAsync()).MaxRows);
        Assert.Null(_host.Stores.Queries.SessionOverride);
    }

    [Fact]
    public async Task Code_analysis_lists_suppressed_rules_and_Undo_removes_the_key_as_stored()
    {
        var settings = new WebAnalysisSettings();
        settings.RuleOverrides["pe001"] = "off";      // stored lower-case: Undo removes exactly this key
        settings.RuleOverrides["ST002"] = "error";
        await _host.Stores.Analysis.SetAsync(settings);
        var cut = _host.Open("settings/code-analysis");

        Assert.NotNull(cut.Find("[data-testid=suppressed-rule-pe001]"));
        Assert.NotNull(cut.Find("[data-testid=remapped-rule-ST002]"));
        cut.Find("[data-testid=suppressed-rule-undo-pe001]").Click();

        var stored = await new AnalysisSettingsStore(_host.Stores.Db).GetAsync();
        Assert.False(stored.RuleOverrides.ContainsKey("pe001"));
        Assert.Equal("error", stored.RuleOverrides["ST002"]);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=suppressed-rule-pe001]")));
    }

    [Fact]
    public async Task Code_analysis_switches_off_and_saves_the_default_filters()
    {
        var cut = _host.Open("settings/code-analysis");

        cut.Find("#setting-analysis-enabled-control").Change(false);
        cut.FindAll("[data-testid=setting-analysis-problems-filter] input[type=checkbox]")[0].Change(false);

        var stored = await new AnalysisSettingsStore(_host.Stores.Db).GetAsync();
        Assert.False(stored.Enabled);
        Assert.False(stored.ProblemsFilter.ShowInfo);
        Assert.True(stored.ProblemsFilter.ShowWarning);
    }

    [Fact]
    public async Task AI_shows_Needs_key_for_an_imported_provider_and_Remove_confirms()
    {
        await _host.Stores.Vault.UpsertConfigAsync(new AiProviderConfig { ProviderId = "anthropic", DisplayName = "Claude", Model = "claude-opus-5-5" });
        await _host.Stores.Vault.SetKeyAsync("ollama", new AiProviderConfig { DisplayName = "Ollama", Model = "llama3", Endpoint = "http://localhost:11434" }, string.Empty);
        var cut = _host.Open("settings/ai-assistance");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=provider-needs-key-anthropic]")));
        Assert.Empty(cut.FindAll("[data-testid=provider-needs-key-ollama]"));   // local: no key needed

        _host.Dialogs.ConfirmAnswer = _ => false;
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Remove").Click();

        var confirm = Assert.Single(_host.Dialogs.Confirms);
        Assert.True(confirm.Destructive);
        Assert.Contains("Claude", confirm.Message);
        Assert.NotNull(await _host.Stores.Vault.GetConfigAsync("anthropic"));   // cancelled: kept
    }

    [Fact]
    public void Format_has_the_Style_select_and_the_Format_styles_link()
    {
        var cut = _host.Open("settings/format");

        Assert.Equal("Style", cut.Find("[data-testid=setting-format-style] label").TextContent.Trim());
        var link = cut.Find("[data-testid=settings-format-styles-link]");
        Assert.Equal("Format styles…", link.TextContent.Trim());
        Assert.Equal("styles", link.GetAttribute("href"));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#setting-format-style-control option")));
    }

    [Fact]
    public async Task Editor_says_what_Reset_editor_session_clears_and_names_the_document()
    {
        var session = new EditorSessionStore(_host.Stores.Db);
        await session.SaveAsync(new EditorSessionRecord { DocumentText = "\n  SELECT name FROM sys.tables;\nGO" });
        await session.FlushAsync();   // SaveAsync is debounced
        var cut = _host.Open("settings/editor");

        Assert.Contains("Clears the document kept between visits", cut.Find("[data-testid=setting-editor-reset-session]").TextContent);
        _host.Dialogs.ConfirmAnswer = _ => false;
        cut.Find("#setting-editor-reset-session-control").Click();

        var confirm = Assert.Single(_host.Dialogs.Confirms);
        Assert.Contains("SELECT name FROM sys.tables;", confirm.Message);
        Assert.True(confirm.Destructive);
    }

    [Fact]
    public async Task Editor_options_save_within_their_limits()
    {
        var cut = _host.Open("settings/editor");

        cut.Find("#setting-editor-font-size-control").Change("99");
        cut.Find("#setting-editor-word-wrap-control").Change(true);

        var stored = await new EditorSettingsStore(_host.Stores.Db).GetAsync();
        Assert.Equal(32, stored.FontSize);
        Assert.True(stored.WordWrap);
    }

    [Fact]
    public void Diagnostics_Clear_confirms()
    {
        _host.Diagnostics.Log(DiagnosticLevel.Info, "test", "one");
        var cut = _host.Open("settings/diagnostics");
        _host.Dialogs.ConfirmAnswer = _ => false;

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear…").Click();

        Assert.True(Assert.Single(_host.Dialogs.Confirms).Destructive);
        Assert.Single(_host.Diagnostics.Snapshot());
    }

    [Fact]
    public async Task Schema_cache_Clear_all_names_how_many_databases_and_how_much_space()
    {
        await _host.SchemaCache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "srv", DatabaseName = "Sales", PhaseA = new byte[4096] });
        await _host.SchemaCache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "srv", DatabaseName = "HR", PhaseA = new byte[2048] });
        var cut = _host.Open("settings/schema-cache");
        _host.Dialogs.ConfirmAnswer = _ => false;

        cut.WaitForElement("[data-testid=schema-cache-clear-all]").Click();

        var confirm = Assert.Single(_host.Dialogs.Confirms);
        Assert.Contains("2 databases", confirm.Message);
        Assert.Contains("KB", confirm.Message);
        Assert.Equal(2, (await _host.SchemaCache.ListAsync()).Count);
    }

    [Fact]
    public async Task Connections_confirm_Remove_ask_a_new_PIN_to_re_pair_and_never_ask_for_a_password()
    {
        var token = await _host.Stores.Pairing.StoreAsync("lan-1", "token", DateTimeOffset.UtcNow.AddDays(1));
        await _host.Stores.Engines.AddAsync(new EngineConnection { Id = "lan-1", Name = "Office engine", Host = "10.0.0.5", IsLocalhost = false, BearerTokenWrappedRef = token });
        // A remote server with Windows authentication: stored, but the web app may not use it (the engine would
        // sign in as its own service account).
        await _host.Stores.SavedSql.AddAsync(new SavedSqlConnection { Id = "bad-1", Name = "Remote", Server = "remote-sql", Database = "master", WindowsAuth = true });
        var cut = _host.Open("settings/connections");

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Add").Click();
        Assert.NotNull(cut.Find("[data-testid=engine-add-name]"));
        Assert.NotNull(cut.Find("[data-testid=settings-manage-connections]"));

        _host.Dialogs.ConfirmAnswer = _ => false;
        cut.Find("[data-testid=engine-remove-lan-1]").Click();
        Assert.Contains("Office engine", Assert.Single(_host.Dialogs.Confirms).Message);

        _host.Dialogs.PromptAnswer = _ => null;
        cut.Find("[data-testid=engine-repair-lan-1]").Click();
        Assert.Equal("Pairing PIN", Assert.Single(_host.Dialogs.Prompts).Label);

        // A saved connection the web app cannot use says why, in place, and stays listed.
        var problem = cut.WaitForElement("[data-testid=saved-connection-problem-bad-1]");
        Assert.False(string.IsNullOrWhiteSpace(problem.TextContent));
        Assert.NotNull(cut.Find("[data-testid=saved-connection-bad-1]"));

        Assert.Empty(cut.FindAll("input[type=password]"));
        Assert.DoesNotContain("Remember password", cut.Markup);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("settings/editor")]
    [InlineData("settings/queries")]
    [InlineData("settings/code-analysis")]
    [InlineData("settings/ai-assistance")]
    [InlineData("settings/format")]
    public void Opening_filtering_and_switching_sections_writes_nothing(string route)
    {
        _host.Stores.Db.ResetLog();
        var cut = _host.Open(route);

        cut.Find("[data-testid=settings-filter]").Input("time");
        cut.Find("[data-testid=settings-filter]").Input("");
        cut.Find("[data-testid=settings-nav-queries]").Click();

        Assert.Empty(_host.Stores.Db.Writes);
        Assert.Empty(_host.Stores.Db.Deletes);
        Assert.Empty(_host.Stores.Db.Clears);
    }
}
