using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 041 (T134, research R46, R47, FR-058, FR-059) — Restore defaults per section and for everything: what the
/// confirmation says, what is reset, and what is never touched (connections, pairing tokens, the schema cache,
/// snippets, history, chat, the document).
/// </summary>
public sealed class SettingsResetTests
{
    [Fact]
    public async Task Each_section_says_what_Restore_defaults_removes()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();

        Assert.Contains("System", Assert.Single(await host.Section("general").DescribeResetAsync()));
        var editor = Assert.Single(await host.Section("editor").DescribeResetAsync());
        Assert.Contains("13 px", editor);
        Assert.Contains("default font", editor);   // spec 043 (FR-017)
        Assert.Contains("Khamis", Assert.Single(await host.Section("format").DescribeResetAsync()));
        Assert.Contains(await host.Section("queries").DescribeResetAsync(), l => l.Contains("1,000 rows"));
        var analysis = await host.Section("code-analysis").DescribeResetAsync();
        Assert.Contains("1 suppressed rule comes back.", analysis);
        Assert.Contains("1 rule gets its own severity back.", analysis);
        var ai = await host.Section("ai-assistance").DescribeResetAsync();
        Assert.Contains("This removes your 3 providers and their keys.", ai);
        Assert.Contains("Chat history is kept.", ai);
    }

    [Fact]
    public async Task Describing_a_reset_changes_nothing()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();

        foreach (var s in host.Sections) await s.DescribeResetAsync();
        await host.Portability.DescribeRestoreAllAsync();

        Assert.Equal(0, host.Db.ChangeCount);
    }

    [Fact]
    public async Task Resetting_one_section_leaves_the_other_stores_untouched()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();

        await host.Section("queries").ResetAsync();

        Assert.Equal(new[] { (StoreNames.AnalysisSettings, "executionSettings") }, host.Db.Deletes);
        Assert.Empty(host.Db.Writes);
        Assert.Empty(host.Db.Clears);
        Assert.False((await new AnalysisSettingsStore(host.Db).GetAsync()).Enabled);   // code analysis kept
        Assert.Equal(18, (await new EditorSettingsStore(host.Db).GetAsync()).FontSize);  // editor kept
    }

    [Fact]
    public async Task The_AI_reset_removes_every_provider_and_key_and_the_active_one_but_keeps_chat()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();

        await host.Section("ai-assistance").ResetAsync();

        Assert.Empty(await host.Vault.ListAsync());
        Assert.False(await host.Vault.HasKeyAsync("openai"));
        Assert.Equal(string.Empty, await host.Preference.GetActiveAsync());
        Assert.Equal(AiPrivacyMode.FullSchema, (await new AiFeatureSettingsStore(host.Db).GetAsync()).GlobalDefaultMode);
        Assert.NotNull(await host.Db.GetAsync(StoreNames.ChatHistory, "current"));
        // Each provider went through RemoveAsync's zeroise-then-delete.
        Assert.Contains(host.Db.Deletes, d => d == (StoreNames.AiKeys, "openai"));
        Assert.Contains(host.Db.Deletes, d => d == (StoreNames.AiKeys, "anthropic"));
    }

    [Fact]
    public async Task Restore_all_lists_every_line_once_and_covers_the_seven_sections()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();

        var lines = await host.Portability.DescribeRestoreAllAsync();

        foreach (var s in host.Sections)
            foreach (var line in await s.DescribeResetAsync())
                Assert.Contains(line, lines);
        Assert.Equal(new[] { "general", "editor", "format", "queries", "code-analysis", "ai-assistance", "layout" },
            host.Portability.Sections.Select(s => s.Id));
        Assert.Contains(lines, l => l.Contains("Connections") && l.Contains("kept"));
    }

    [Fact]
    public async Task Restore_all_resets_every_preference_and_touches_nothing_else()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();
        var before = await host.SnapshotProtectedAsync();

        await host.Portability.RestoreAllAsync();

        Assert.Equal(before, await host.SnapshotProtectedAsync());
        Assert.All(host.Db.Writes.Concat(host.Db.Deletes.Select(d => (d.Store, d.Key, ""))),
            w => Assert.DoesNotContain(w.Item1, SettingsStoresHost.ProtectedStores));
        Assert.Empty(host.Db.Clears.Where(c => SettingsStoresHost.ProtectedStores.Contains(c)));

        // Fresh stores read the defaults back.
        Assert.Equal(ThemeMode.System, host.Theme.Current);
        Assert.Equal(13, (await new EditorSettingsStore(host.Db).GetAsync()).FontSize);
        Assert.Equal("builtin.khamis", await new ProfileStore(host.Db).GetActiveIdAsync());
        Assert.Equal(1000, (await new ExecutionSettingsStore(host.Db).GetAsync()).MaxRows);
        var analysis = await new AnalysisSettingsStore(host.Db).GetAsync();
        Assert.True(analysis.Enabled);
        Assert.Empty(analysis.RuleOverrides);
        Assert.Empty(await new AiKeyVault(host.Db, host.Crypto).ListAsync());
        Assert.True((await new WorkspaceLayoutStore(host.Db).LoadAsync()).SchemaVisible);
    }
}
