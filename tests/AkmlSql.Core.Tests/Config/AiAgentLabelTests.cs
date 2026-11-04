using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config;

/// <summary>
/// Spec 046 (FR-022, FR-030, contracts/chat-composer-and-agents.md Part 3): one label for an agent
/// in both editions — the SSMS picker, the attribution caption, the agent-switch notice, the web
/// selector and the web answer header all read "Name · Provider · model", so two agents on the same
/// provider are told apart by their model and a name like "gemini-3.5-flash-lite" no longer has to
/// stand for all three.
/// </summary>
public class AiAgentLabelTests
{
    [Fact]
    public void A_full_agent_reads_name_provider_and_model()
        => Assert.Equal("Gemini Flash · Gemini · gemini-2.5-flash",
            AiAgentLabel.Format("Gemini Flash", "gemini", "gemini-2.5-flash"));

    [Fact]
    public void The_provider_goes_through_the_shared_display_names()
    {
        Assert.Equal("Work · Anthropic · claude-sonnet-4-5", AiAgentLabel.Format("Work", "anthropic", "claude-sonnet-4-5"));
        Assert.Equal("Azure · Azure OpenAI · gpt-4o", AiAgentLabel.Format("Azure", "azure", "gpt-4o"));
        Assert.Equal("Local · LM Studio · qwen", AiAgentLabel.Format("Local", "lmstudio", "qwen"));
    }

    [Fact]
    public void Legacy_provider_spellings_are_normalised_first()
        => Assert.Equal("Old · Azure OpenAI · gpt-4o", AiAgentLabel.Format("Old", "AzureOpenAI", "gpt-4o"));

    [Fact]
    public void An_unknown_provider_id_passes_through()
        => Assert.Equal("Mine · acme · m1", AiAgentLabel.Format("Mine", "acme", "m1"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_model_is_left_out_with_its_separator(string? model)
        => Assert.Equal("Local · Ollama", AiAgentLabel.Format("Local", "ollama", model));

    [Fact]
    public void A_blank_provider_is_left_out_with_its_separator()
        => Assert.Equal("Mine · m1", AiAgentLabel.Format("Mine", "", "m1"));

    [Fact]
    public void Only_a_name_reads_as_the_name()
        => Assert.Equal("Kimi", AiAgentLabel.Format("Kimi", null, null));

    [Fact]
    public void A_blank_name_still_names_provider_and_model()
        => Assert.Equal("Gemini · gemini-2.5-flash", AiAgentLabel.Format("  ", "gemini", "gemini-2.5-flash"));

    [Fact]
    public void Nothing_known_reads_as_empty()
        => Assert.Equal(string.Empty, AiAgentLabel.Format(null, null, null));

    [Theory]
    [InlineData("Ollama", "ollama", "llama3", "Ollama · llama3")]          // the offline fallback's name
    [InlineData("gemini", "gemini", "gemini-2.5-flash", "gemini · gemini-2.5-flash")]
    [InlineData("Gemini 2", "gemini", "gemini-2.5-flash", "Gemini 2 · Gemini · gemini-2.5-flash")]
    public void A_name_that_repeats_the_provider_says_it_once(string name, string provider, string model, string expected)
        => Assert.Equal(expected, AiAgentLabel.Format(name, provider, model));

    [Fact]
    public void Parts_are_trimmed()
        => Assert.Equal("Gemini Flash · Gemini · gemini-2.5-flash",
            AiAgentLabel.Format("  Gemini Flash ", " gemini ", " gemini-2.5-flash  "));
}
