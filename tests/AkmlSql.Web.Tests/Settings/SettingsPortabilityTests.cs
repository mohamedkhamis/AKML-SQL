using System.Text.Json;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 041 (T135, research R48, R49, FR-059, SC-013) — Export writes the data-model §1.5 envelope with no
/// secret in it; Import applies only the sections present, counts what it did not understand, never brings a key,
/// and re-applies the fully-local guard.
/// </summary>
public sealed class SettingsPortabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_export_is_the_envelope_of_data_model_1_5()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();

        using var doc = JsonDocument.Parse(await host.Portability.ExportAsync(Now));
        var root = doc.RootElement;

        Assert.Equal("akmlsql-web-settings", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("2026-10-09T14:30:00Z", root.GetProperty("exportedAt").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("appVersion").GetString()));
        var sections = root.GetProperty("sections");
        Assert.Equal(new[] { "general", "editor", "format", "queries", "codeAnalysis", "aiAssistance", "layout" },
            sections.EnumerateObject().Select(p => p.Name));

        Assert.Equal("dark", sections.GetProperty("general").GetProperty("theme").GetString());
        Assert.Equal(18, sections.GetProperty("editor").GetProperty("fontSize").GetInt32());
        Assert.Equal("builtin.ansi", sections.GetProperty("format").GetProperty("activeStyleId").GetString());
        Assert.Equal(500, sections.GetProperty("queries").GetProperty("maxRows").GetInt32());
        Assert.True(sections.GetProperty("queries").GetProperty("showColumnTypes").GetBoolean());
        var analysis = sections.GetProperty("codeAnalysis");
        Assert.False(analysis.GetProperty("enabled").GetBoolean());
        Assert.Equal("off", analysis.GetProperty("ruleOverrides").GetProperty("PE001").GetString());
        Assert.False(analysis.GetProperty("problemsFilter").GetProperty("showInfo").GetBoolean());
        var ai = sections.GetProperty("aiAssistance");
        Assert.Equal(3, ai.GetProperty("providers").GetArrayLength());
        Assert.Equal("anthropic", ai.GetProperty("activeProviderId").GetString());
        Assert.Equal("schemaNamesOnly", ai.GetProperty("privacy").GetProperty("globalDefaultMode").GetString());
        Assert.True(ai.GetProperty("ghostText").GetProperty("enabled").GetBoolean());
        Assert.False(sections.GetProperty("layout").GetProperty("schemaVisible").GetBoolean());
        Assert.Equal(420, sections.GetProperty("layout").GetProperty("aiWidth").GetInt32());
    }

    [Fact]
    public async Task The_export_holds_no_secret_and_no_key_material()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();

        var json = await host.Portability.ExportAsync(Now);

        foreach (var secret in new[] { SettingsStoresHost.OpenAiKey, SettingsStoresHost.AnthropicKey, SettingsStoresHost.PairingToken })
            Assert.DoesNotContain(secret, json);
        // No key-material or connection field names (as JSON property names) …
        foreach (var field in new[] { "ciphertext", "iv", "aad", "hasKey", "bearerTokenWrappedRef", "password", "login", "token" })
            Assert.DoesNotContain($"\"{field}\"", json, StringComparison.OrdinalIgnoreCase);
        // … and none of the connections, schema cache, snippets, chat or document seeded beside the preferences.
        foreach (var data in new[] { "prod-sql", "report_user", "Office engine", "10.0.0.5", "SELECT * FROM", "SELECT 1", "hello" })
            Assert.DoesNotContain(data, json, StringComparison.Ordinal);
        // The wrapped ciphertext itself is not there either.
        foreach (var (_, record) in await host.Db.ListAsync(StoreNames.AiKeys))
        {
            if (!record.StartsWith('{')) continue;
            using var r = JsonDocument.Parse(record);
            var cipher = r.RootElement.GetProperty("Ciphertext").GetString();
            if (!string.IsNullOrEmpty(cipher)) Assert.DoesNotContain(cipher, json);
        }
    }

    [Fact]
    public void The_file_is_named_after_the_day()
    {
        Assert.Equal("akmlsql-web-settings-20261009.json", new SettingsStoresHost().Portability.ExportFileName(Now));
    }

    [Fact]
    public async Task Import_applies_only_the_sections_present()
    {
        var host = new SettingsStoresHost();
        await host.SeedEverythingAsync();

        var result = await host.Portability.ImportAsync("""
            { "format": "akmlsql-web-settings", "version": 1,
              "sections": { "editor": { "fontSize": 22, "wordWrap": false, "tabSize": 8 } } }
            """);

        Assert.True(result.Ok);
        Assert.Equal(1, result.SectionsApplied);
        Assert.Equal(0, result.Skipped);
        var editor = await new EditorSettingsStore(host.Db).GetAsync();
        Assert.Equal(22, editor.FontSize);
        Assert.Equal(8, editor.TabSize);
        Assert.Equal(500, (await new ExecutionSettingsStore(host.Db).GetAsync()).MaxRows);   // absent → unchanged
        Assert.Equal("Imported 1 section.", result.Summary);
    }

    [Fact]
    public async Task Unknown_properties_nested_ones_too_and_unknown_sections_are_counted()
    {
        var host = new SettingsStoresHost();

        var result = await host.Portability.ImportAsync("""
            { "format": "akmlsql-web-settings", "version": 3,
              "sections": {
                "editor": { "fontSize": 14, "minimap": true },
                "codeAnalysis": { "enabled": true, "problemsFilter": { "showInfo": true, "showHints": false } },
                "aiAssistance": { "providers": [ { "providerId": "openai", "model": "gpt-5", "temperature": 0.2 } ] },
                "grid": { "maxColumnPx": 480, "zebra": true }
              } }
            """);

        Assert.True(result.Ok);                 // a newer version is accepted
        Assert.Equal(3, result.SectionsApplied);
        Assert.Equal(5, result.Skipped);         // minimap, showHints, temperature, and grid's two settings
        Assert.Equal("Imported 3 sections. 5 settings skipped.", result.Summary);
    }

    [Fact]
    public async Task Imported_providers_need_a_key_and_an_existing_key_is_kept()
    {
        var host = new SettingsStoresHost();
        await host.Vault.SetKeyAsync("openai", new AiProviderConfig { DisplayName = "OpenAI", Model = "gpt-4o" }, "sk-existing");

        await host.Portability.ImportAsync("""
            { "format": "akmlsql-web-settings", "version": 1,
              "sections": { "aiAssistance": {
                "providers": [ { "providerId": "openai", "displayName": "OpenAI", "model": "gpt-5" },
                               { "providerId": "anthropic", "displayName": "Claude", "model": "claude-opus-5-5", "hasKey": true } ],
                "activeProviderId": "anthropic" } } }
            """);

        Assert.True(await host.Vault.HasKeyAsync("openai"));
        Assert.Equal("gpt-5", (await host.Vault.GetConfigAsync("openai"))!.Model);
        Assert.False(await host.Vault.HasKeyAsync("anthropic"));
        Assert.Equal("anthropic", await host.Preference.GetActiveAsync());
    }

    [Fact]
    public async Task An_active_provider_that_does_not_exist_is_not_applied()
    {
        var host = new SettingsStoresHost();
        await host.Preference.SetActiveAsync("openai");

        var result = await host.Portability.ImportAsync("""
            { "format": "akmlsql-web-settings", "version": 1,
              "sections": { "aiAssistance": { "providers": [], "activeProviderId": "gemini" } } }
            """);

        Assert.Equal("openai", await host.Preference.GetActiveAsync());
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task Fully_local_never_leaves_a_cloud_provider_active_after_an_import()
    {
        var host = new SettingsStoresHost();

        await host.Portability.ImportAsync("""
            { "format": "akmlsql-web-settings", "version": 1,
              "sections": { "aiAssistance": {
                "providers": [ { "providerId": "openai", "model": "gpt-5" } ], "activeProviderId": "openai",
                "privacy": { "globalDefaultMode": "fullyLocal" } } } }
            """);

        Assert.Equal(string.Empty, await host.Preference.GetActiveAsync());
    }

    [Theory]
    [InlineData("{ \"format\": \"something-else\", \"version\": 1, \"sections\": {} }")]
    [InlineData("{ \"version\": 1 }")]
    [InlineData("[1, 2, 3]")]
    [InlineData("not json")]
    public async Task A_file_that_is_not_a_settings_export_is_refused_and_changes_nothing(string json)
    {
        var host = new SettingsStoresHost();

        var result = await host.Portability.ImportAsync(json);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Equal(0, host.Db.ChangeCount);
    }

    [Fact]
    public async Task An_export_imports_back_to_the_same_settings()
    {
        var source = new SettingsStoresHost();
        await source.SeedEverythingAsync();
        var json = await source.Portability.ExportAsync(Now);

        var target = new SettingsStoresHost();
        var result = await target.Portability.ImportAsync(json);

        Assert.True(result.Ok);
        Assert.Equal(7, result.SectionsApplied);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(json.Replace(Now.ToString("O"), ""), (await target.Portability.ExportAsync(Now)).Replace(Now.ToString("O"), ""));
    }
}
