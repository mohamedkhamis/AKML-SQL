using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The shell's IPC wait for an AI request must exceed the provider timeout the engine
    /// honours (<see cref="AiSettings.Timeout"/>). AiChatPanel hard-coded 30 s while the
    /// engine gave the provider 90 s, so any answer slower than 30 s surfaced as
    /// "Error: A task was canceled" even though the provider was still generating.
    /// </summary>
    public class AiIpcTimeoutsTests
    {
        [Theory]
        [InlineData(90, 120_000)]   // the shipped default config: 90 s provider + 30 s margin
        [InlineData(45, 75_000)]
        [InlineData(300, 330_000)]
        public void Ipc_wait_is_provider_timeout_plus_margin(int providerTimeoutSec, int expectedMs)
        {
            var settings = new AppSettings();
            settings.Ai.Timeout = providerTimeoutSec;

            Assert.Equal(expectedMs, AiIpcTimeouts.ForAiRequestMs(settings));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Nonsense_provider_timeout_falls_back_to_the_default(int broken)
        {
            var settings = new AppSettings();
            settings.Ai.Timeout = broken;

            Assert.Equal(120_000, AiIpcTimeouts.ForAiRequestMs(settings));
        }

        [Fact]
        public void Null_settings_fall_back_to_the_default()
        {
            Assert.Equal(120_000, AiIpcTimeouts.ForAiRequestMs(null));
        }

        // ── Spec 037 (FR-048): the budget follows the ANSWERING agent ────────

        [Fact]
        public void The_chat_budget_follows_the_assigned_agents_timeout_not_the_active_ones()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Active", timeoutSec: 30);
            var chatty = UsableAgent("Chatty", timeoutSec: 300);
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(chatty);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.Timeout = 30;   // the flat mirror follows the ACTIVE agent (V18)
            settings.Ai.FeatureAgents.Chat = chatty.Id;

            // The engine serves chat from the assigned agent with ITS 300 s timeout — the
            // shell budget must outlast that, not the active agent's 30 s.
            Assert.Equal(330_000, AiIpcTimeouts.ForAiRequestMs(settings, AiFeature.Chat));
            // A feature with no assignment follows the active agent (S3).
            Assert.Equal(60_000, AiIpcTimeouts.ForAiRequestMs(settings, AiFeature.Explain));
        }

        [Fact]
        public void The_per_feature_budget_falls_back_to_the_flat_value_when_nothing_resolves()
        {
            var settings = new AppSettings();
            settings.Ai.Timeout = 45;

            // No agents at all — nothing resolves, the flat value rules.
            Assert.Equal(75_000, AiIpcTimeouts.ForAiRequestMs(settings, AiFeature.Chat));
        }

        [Fact]
        public void The_per_feature_timeout_text_quotes_the_budget_actually_waited()
        {
            var settings = new AppSettings();
            var active = UsableAgent("Active", timeoutSec: 30);
            var chatty = UsableAgent("Chatty", timeoutSec: 300);
            settings.Ai.Agents.Add(active);
            settings.Ai.Agents.Add(chatty);
            settings.Ai.ActiveAgentId = active.Id;
            settings.Ai.Timeout = 30;
            settings.Ai.FeatureAgents.Chat = chatty.Id;

            var text = AiIpcTimeouts.DescribeFailure(
                new System.OperationCanceledException(), settings, AiFeature.Chat);

            Assert.Contains("330s", text);
        }

        private static AiAgent UsableAgent(string name, int timeoutSec)
        {
            return new AiAgent
            {
                Id = System.Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = "sk-test",
                Timeout = timeoutSec,
                Enabled = true,
            };
        }
    }
}
