using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Spec 046 (US4, FR-030 – FR-033, contracts/chat-composer-and-agents.md Part 6): the web chat's
/// agent selector. It lists the usable agents as "Name · Provider · model", shows the active one,
/// changes the ONE active agent every web AI action shares (spec Assumptions), and ends with
/// "Manage agents…", which opens Settings without changing the selection.
/// </summary>
public sealed class AiAgentSelectorTests : BunitContext
{
    private readonly FakeAiKeyVault _vault = new();
    private readonly FakeAiPreference _preference = new("a2");

    public AiAgentSelectorTests()
    {
        _vault.Configs.Add(new AiProviderConfig { Id = "a1", ProviderId = "anthropic", DisplayName = "Work", Model = "claude-sonnet-4-5", HasKey = true });
        _vault.Configs.Add(new AiProviderConfig { Id = "a2", ProviderId = "gemini", DisplayName = "Gemini Flash", Model = "gemini-2.5-flash", HasKey = true });
        _vault.Configs.Add(new AiProviderConfig { Id = "a3", ProviderId = "ollama", DisplayName = "Local", Model = "llama3", HasKey = false });
        _vault.Configs.Add(new AiProviderConfig { Id = "a4", ProviderId = "openai", DisplayName = "NoKey", Model = "gpt-4o", HasKey = false });
        _vault.Configs.Add(new AiProviderConfig { Id = "a5", ProviderId = "anthropic", DisplayName = "Off", Model = "m", HasKey = true, Enabled = false });
        Services.AddSingleton<IAiKeyVault>(_vault);
        Services.AddSingleton<IAiPreference>(_preference);
    }

    private static IReadOnlyList<string> OptionTexts(IRenderedComponent<AiAgentSelector> cut)
        => cut.FindAll("option").Select(o => o.TextContent.Trim()).ToList();

    [Fact]
    public void Usable_agents_are_listed_as_name_provider_and_model_then_manage()
    {
        var cut = Render<AiAgentSelector>();

        Assert.Equal(new[]
        {
            "Work · Anthropic · claude-sonnet-4-5",
            "Gemini Flash · Gemini · gemini-2.5-flash",
            "Local · Ollama · llama3",           // a local provider needs no key
            AiAgentSelector.ManageText,
        }, OptionTexts(cut));
    }

    [Fact]
    public void The_active_agent_is_selected_and_the_control_explains_itself()
    {
        var cut = Render<AiAgentSelector>();
        var select = cut.Find("select[data-testid='chat-agent-select']");

        Assert.Equal("a2", select.GetAttribute("value"));
        Assert.Equal("Answering agent", select.GetAttribute("aria-label"));
        Assert.Equal("Choose which agent answers your messages", select.GetAttribute("title"));
    }

    [Fact]
    public async Task Choosing_an_agent_makes_it_the_active_agent_and_reports_it()
    {
        string? reported = null;
        var cut = Render<AiAgentSelector>(p => p.Add(s => s.Changed, id => reported = id));

        await cut.Find("select").ChangeAsync(new ChangeEventArgs { Value = "a1" });

        Assert.Equal("a1", await _preference.GetActiveAsync());
        Assert.Equal("a1", reported);
    }

    [Fact]
    public async Task Re_choosing_the_active_agent_reports_nothing()
    {
        string? reported = null;
        var cut = Render<AiAgentSelector>(p => p.Add(s => s.Changed, id => reported = id));

        await cut.Find("select").ChangeAsync(new ChangeEventArgs { Value = "a2" });

        Assert.Null(reported);
    }

    [Fact]
    public async Task Manage_agents_opens_settings_without_changing_the_selection()
    {
        string? reported = null;
        var cut = Render<AiAgentSelector>(p => p.Add(s => s.Changed, id => reported = id));
        var navigation = Services.GetRequiredService<NavigationManager>();

        await cut.Find("select").ChangeAsync(new ChangeEventArgs { Value = AiAgentSelector.ManageValue });

        Assert.EndsWith("settings/ai-assistance", navigation.Uri);
        Assert.Equal("a2", await _preference.GetActiveAsync());
        Assert.Null(reported);
    }

    [Fact]
    public void Without_a_key_vault_the_selector_renders_nothing()
    {
        using var bare = new BunitContext();
        bare.Services.AddSingleton<IAiPreference>(new FakeAiPreference("a1"));

        var cut = bare.Render<AiAgentSelector>();

        Assert.Empty(cut.FindAll("select"));
    }
}
