using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Settings › AI assistance, 2026-11 master–detail layout: the agent list on the left, the selected agent (or its form) on
/// the right. Runs over the REAL vault and preference (in-memory IndexedDB, in-memory Web Crypto) so storage is exercised
/// end to end: several agents per provider, the stored key kept on Edit, Duplicate re-wrapping the key, Delete handing the
/// active choice over. Earlier findings stay pinned: the empty Model box means the default the form shows (the 2026-10-19
/// Gemini test), and the first agent saved becomes the active one.
/// </summary>
public sealed class AiAssistanceSectionTests : BunitContext
{
    private readonly InMemoryIndexedDbAdapter _db = new();
    private readonly AiKeyVault _vault;
    private readonly AiPreference _preference;
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly FakeTester _tester = new();

    public AiAssistanceSectionTests()
    {
        _vault = new AiKeyVault(_db, new InMemoryWebCryptoWrapper());
        _preference = new AiPreference(_db);
        Services.AddSingleton<IAiKeyVault>(_vault);
        Services.AddSingleton<IAiPreference>(_preference);
        Services.AddSingleton<IAiFeatureSettings>(new Settings());
        Services.AddSingleton<IAiClientFactory>(new Client());
        Services.AddSingleton<IAiConnectionTester>(_tester);
        Services.AddSingleton<IDialogService>(_dialogs);
        Services.AddSingleton<INotificationService>(_notifications);
    }

    // ── Form helpers ────────────────────────────────────────────────────────────────

    private static void Fill(IRenderedComponent<AiAssistanceSection> cut, string providerId, string? model = null, string? key = null,
        string? name = null, string? endpoint = null)
    {
        cut.Find("form select").Change(providerId);
        var inputs = cut.FindAll("form input.akml-input");   // Name, Model, Endpoint, [API key]
        if (name != null) inputs[0].Change(name);
        if (model != null) cut.FindAll("form input.akml-input")[1].Change(model);
        if (endpoint != null) cut.FindAll("form input.akml-input")[2].Change(endpoint);
        if (key != null) cut.Find("form input[type=password]").Change(key);
    }

    private static void Submit(IRenderedComponent<AiAssistanceSection> cut) => cut.Find("form").Submit();

    private IRenderedComponent<AiAssistanceSection> RenderWith(params (string Provider, string Model, string Key)[] agents)
    {
        var cut = Render<AiAssistanceSection>();
        foreach (var (provider, model, key) in agents)
        {
            if (cut.FindAll("[data-testid='ai-agent-form']").Count == 0) cut.Find("[data-testid='ai-agent-add']").Click();
            Fill(cut, provider, model, key);
            Submit(cut);
            cut.WaitForState(() => cut.FindAll("[data-testid='ai-agent-form']").Count == 0);
        }
        return cut;
    }

    private async Task<IReadOnlyList<AiProviderConfig>> AgentsAsync() => await _vault.ListAsync();

    // ── Add ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task With_no_agent_the_add_form_is_open_and_the_first_agent_saved_becomes_active()
    {
        var cut = Render<AiAssistanceSection>();
        Assert.Single(cut.FindAll("[data-testid='ai-agent-form']"));
        Assert.Contains("Add your first AI agent", cut.Markup);

        Fill(cut, "gemini", model: null, key: "test-key");
        Submit(cut);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid^='ai-agent-item-']")));
        var agent = Assert.Single(await AgentsAsync());
        Assert.Equal("gemini-flash-latest", agent.Model);           // the empty box meant the default it showed
        Assert.Equal("Google Gemini", agent.DisplayName);
        Assert.Equal(32, agent.Id.Length);
        cut.WaitForAssertion(() => Assert.Equal(agent.Id, _preference.GetActiveAsync().Result));
        Assert.Equal("gemini", await _preference.GetActiveProviderAsync());
        Assert.DoesNotContain("Model is required", cut.Markup);
    }

    [Fact]
    public void Azure_still_needs_its_deployment_name()
    {
        var cut = Render<AiAssistanceSection>();
        Fill(cut, "azure", endpoint: "https://x.openai.azure.com/", key: "k");
        Submit(cut);

        cut.WaitForAssertion(() => Assert.Contains("Model is required.", cut.Markup));
        Assert.Empty(_vault.ListAsync().Result);
    }

    [Fact]
    public async Task Two_agents_can_share_one_provider()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "k1"), ("anthropic", "claude-opus-4-8", "k2"));

        var agents = await AgentsAsync();
        Assert.Equal(2, agents.Count);
        Assert.All(agents, a => Assert.Equal("anthropic", a.ProviderId));
        Assert.NotEqual(agents[0].Id, agents[1].Id);
        Assert.Equal(new[] { "Anthropic", "Anthropic 2" }, agents.Select(a => a.DisplayName));
        Assert.Equal(2, cut.FindAll("[data-testid^='ai-agent-item-']").Count);
        Assert.Contains("2 of 20", cut.Find("[data-testid='ai-agent-count']").TextContent);

        // Each keeps its own key.
        using (var k1 = await _vault.UnwrapForCallAsync(agents[0].Id)) Assert.Equal("k1", k1.Value);
        using (var k2 = await _vault.UnwrapForCallAsync(agents[1].Id)) Assert.Equal("k2", k2.Value);
    }

    [Fact]
    public async Task Saving_another_agent_leaves_the_active_one_alone()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "k1"));
        var first = (await AgentsAsync())[0].Id;

        cut.Find("[data-testid='ai-agent-add']").Click();
        Fill(cut, "gemini", "gemini-flash-latest", "k2");
        Submit(cut);

        cut.WaitForAssertion(() => Assert.Equal(2, _vault.ListAsync().Result.Count));
        Assert.Equal(first, await _preference.GetActiveAsync());
    }

    [Fact]
    public void A_duplicate_name_is_refused()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "k1"));

        cut.Find("[data-testid='ai-agent-add']").Click();
        Fill(cut, "gemini", "gemini-flash-latest", "k2", name: "anthropic");
        Submit(cut);

        cut.WaitForAssertion(() => Assert.Contains("Another agent is already named", cut.Markup));
        Assert.Single(_vault.ListAsync().Result);
    }

    // ── Details and Edit ────────────────────────────────────────────────────────────

    [Fact]
    public void The_details_show_the_selected_agent_without_its_key()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "sk-secret"));

        var detail = cut.Find("[data-testid='ai-agent-detail']");
        Assert.Contains("Anthropic", detail.TextContent);
        Assert.Contains("claude-sonnet-4-6", detail.TextContent);
        Assert.Contains("Stored, encrypted in this browser", detail.TextContent);
        Assert.DoesNotContain("sk-secret", cut.Markup);
        Assert.Single(cut.FindAll("[data-testid^='ai-agent-active-']"));
    }

    [Fact]
    public async Task Edit_with_an_empty_key_box_keeps_the_stored_key()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "sk-keep"));

        cut.Find("[data-testid='ai-agent-edit']").Click();
        Assert.Equal("Stored. Leave empty to keep it.", cut.Find("form input[type=password]").GetAttribute("placeholder"));
        cut.FindAll("form input.akml-input")[1].Change("claude-opus-4-8");
        Submit(cut);

        cut.WaitForAssertion(() => Assert.Equal("claude-opus-4-8", _vault.ListAsync().Result[0].Model));
        var agent = (await AgentsAsync())[0];
        Assert.True(agent.HasKey);
        using var key = await _vault.UnwrapForCallAsync(agent.Id);
        Assert.Equal("sk-keep", key.Value);
    }

    [Fact]
    public void Switching_provider_on_edit_needs_a_new_key()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "sk-anthropic"));

        cut.Find("[data-testid='ai-agent-edit']").Click();
        cut.Find("form select").Change("gemini");
        Assert.Contains("belongs to Anthropic and is removed", cut.Markup);
        Submit(cut);

        cut.WaitForAssertion(() => Assert.Contains("API key is required.", cut.Markup));
        Assert.Equal("anthropic", _vault.ListAsync().Result[0].ProviderId);   // nothing saved
    }

    [Fact]
    public async Task Cancel_discards_the_form()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "k"));

        cut.Find("[data-testid='ai-agent-edit']").Click();
        cut.FindAll("form input.akml-input")[1].Change("changed");
        cut.Find("[data-testid='ai-agent-cancel']").Click();

        Assert.Empty(cut.FindAll("[data-testid='ai-agent-form']"));
        Assert.Equal("claude-sonnet-4-6", (await AgentsAsync())[0].Model);
    }

    // ── Duplicate, Set as active, Delete ────────────────────────────────────────────

    [Fact]
    public async Task Duplicate_copies_the_agent_and_its_key_under_a_new_id()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "sk-dup"));

        cut.Find("[data-testid='ai-agent-duplicate']").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, _vault.ListAsync().Result.Count));
        var agents = await AgentsAsync();
        var copy = agents.Single(a => a.DisplayName == "Anthropic (copy)");
        Assert.NotEqual(agents[0].Id, copy.Id);
        using var key = await _vault.UnwrapForCallAsync(copy.Id);
        Assert.Equal("sk-dup", key.Value);
        Assert.Contains("Anthropic (copy)", cut.Find("[data-testid='ai-agent-title']").TextContent);   // the copy is selected
    }

    [Fact]
    public async Task Set_as_active_moves_the_active_agent()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "k1"), ("gemini", "gemini-flash-latest", "k2"));
        var second = (await AgentsAsync())[1];

        cut.Find($"[data-testid='ai-agent-item-{second.Id}']").Click();
        cut.Find("[data-testid='ai-agent-set-active']").Click();

        cut.WaitForAssertion(() => Assert.Equal(second.Id, _preference.GetActiveAsync().Result));
        Assert.Equal("gemini", await _preference.GetActiveProviderAsync());
    }

    [Fact]
    public async Task A_turned_off_agent_cannot_be_made_active()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "k1"), ("gemini", "gemini-flash-latest", "k2"));
        var agents = await AgentsAsync();
        cut.Find($"[data-testid='ai-agent-item-{agents[1].Id}']").Click();
        cut.Find("[data-testid='ai-agent-edit']").Click();
        cut.Find("form input[type=checkbox]").Change(false);
        Submit(cut);
        cut.WaitForAssertion(() => Assert.False(_vault.ListAsync().Result[1].Enabled));

        cut.Find("[data-testid='ai-agent-set-active']").Click();

        Assert.Equal(agents[0].Id, await _preference.GetActiveAsync());
        Assert.Contains(_notifications.Sent, n => n.Text.Contains("turned off"));
    }

    [Fact]
    public async Task Deleting_the_active_agent_confirms_and_hands_over()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "k1"), ("gemini", "gemini-flash-latest", "k2"));
        var agents = await AgentsAsync();
        Assert.Equal(agents[0].Id, await _preference.GetActiveAsync());

        cut.Find($"[data-testid='ai-agent-item-{agents[0].Id}']").Click();
        cut.Find("[data-testid='ai-agent-delete']").Click();

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal("Delete agent", confirm.Title);
        Assert.Contains("Google Gemini", string.Join(" ", confirm.Details ?? Array.Empty<string>()));
        cut.WaitForAssertion(() => Assert.Single(_vault.ListAsync().Result));
        Assert.Equal(agents[1].Id, await _preference.GetActiveAsync());
    }

    // ── Test connection ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_test_in_the_form_uses_the_typed_values_and_is_recorded_on_save()
    {
        _tester.Result = new AiConnectionTestResult(true, "Connected — gemini-flash-latest answered in 42 ms.", 42);
        var cut = Render<AiAssistanceSection>();
        Fill(cut, "gemini", "gemini-flash-latest", "typed-key");

        cut.Find("[data-testid='ai-agent-form-test']").Click();
        cut.WaitForAssertion(() => Assert.Contains("answered in 42 ms", cut.Find("[data-testid='ai-agent-form-test-result']").TextContent));
        var (config, key) = Assert.Single(_tester.Calls);
        Assert.Equal("gemini", config.ProviderId);
        Assert.Equal("typed-key", key);

        Submit(cut);
        cut.WaitForAssertion(() => Assert.Single(_vault.ListAsync().Result));
        var agent = (await AgentsAsync())[0];
        Assert.Equal(AiAgentCheck.Ready, agent.LastCheck?.Status);
        Assert.Equal(42, agent.LastCheck!.LatencyMs);
        cut.WaitForAssertion(() => Assert.Contains("Ready", cut.Find($"[data-testid='ai-agent-item-{agent.Id}']").TextContent));
    }

    [Fact]
    public async Task A_test_from_the_details_records_its_outcome_and_keeps_the_key()
    {
        var cut = RenderWith(("anthropic", "claude-sonnet-4-6", "sk-stored"));
        _tester.Result = new AiConnectionTestResult(false, "The API key was rejected (HTTP 401).", 0);

        cut.Find("[data-testid='ai-agent-test']").Click();

        cut.WaitForAssertion(() => Assert.Contains("rejected", cut.Find("[data-testid='ai-agent-test-result']").TextContent));
        var (_, key) = Assert.Single(_tester.Calls);
        Assert.Null(key);   // the stored key is used, never read into the page
        var agent = (await AgentsAsync())[0];
        Assert.Equal(AiAgentCheck.Failed, agent.LastCheck?.Status);
        Assert.True(agent.HasKey);
    }

    // ── Agents saved before agents had ids ──────────────────────────────────────────

    [Fact]
    public async Task An_agent_saved_by_provider_id_still_lists_and_stays_active()
    {
        await _vault.SetKeyAsync("openai", new AiProviderConfig { ProviderId = "openai", DisplayName = "OpenAI", Model = "gpt-4.1" }, "sk-old");
        await _preference.SetActiveAsync("openai");

        var cut = Render<AiAssistanceSection>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='ai-agent-item-openai']")));
        Assert.Single(cut.FindAll("[data-testid='ai-agent-active-openai']"));
        Assert.Equal("openai", await _preference.GetActiveProviderAsync());
        using var key = await _vault.UnwrapForCallAsync("openai");
        Assert.Equal("sk-old", key.Value);
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────────

    private sealed class FakeTester : IAiConnectionTester
    {
        public AiConnectionTestResult Result { get; set; } = new(true, "Connected.", 10);
        public List<(AiProviderConfig Config, string? Key)> Calls { get; } = new();

        public Task<AiConnectionTestResult> TestAsync(AiProviderConfig config, string? apiKeyPlain, CancellationToken ct)
        {
            Calls.Add((config, string.IsNullOrEmpty(apiKeyPlain) ? null : apiKeyPlain));
            return Task.FromResult(Result);
        }
    }

    private sealed class Settings : IAiFeatureSettings
    {
        public Task<AiFeatureSettings> GetAsync() => Task.FromResult(new AiFeatureSettings());
        public Task SetAsync(AiFeatureSettings settings) => Task.CompletedTask;
        public Task<AiPrivacyMode> ResolveModeAsync(string featureId) => Task.FromResult(AiPrivacyMode.FullSchema);
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
    }

    private sealed class Client : IAiClientFactory
    {
        public Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct) => Task.FromResult(string.Empty);
#pragma warning disable CS1998
        public async IAsyncEnumerable<string> StreamAsync(string providerId, AiChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { yield break; }
#pragma warning restore CS1998
        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => true;
    }
}
