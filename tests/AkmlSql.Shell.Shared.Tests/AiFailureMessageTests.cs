using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// A timed-out AI IPC request must not surface as the bare "A task was canceled" — the
    /// user cannot act on that. It should say it timed out, for how long it waited, and where
    /// to look. Provider errors (quota, key, model) keep their original message.
    ///
    /// <para>Spec 037 (US5, FR-057, T080): a failed LIVE request caused by the agent's
    /// configuration names the agent and offers the route to its settings; momentary states
    /// (quota, timeout, consent, engine down) keep their bare message. The panel-level tests
    /// construct an <see cref="AiChatPanel"/>, so the class joins the "AkmlSql ThemeRegistry"
    /// collection that serialises panel-constructing classes.</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiFailureMessageTests
    {
        [Fact]
        public void Timeout_cancellation_is_described_with_the_wait_and_a_pointer()
        {
            var settings = new AppSettings();
            settings.Ai.Timeout = 90;

            var msg = AiIpcTimeouts.DescribeFailure(new TaskCanceledException("A task was canceled."), settings);

            Assert.Contains("timed out", msg);
            Assert.Contains("120", msg);           // 90 s provider + 30 s margin
            Assert.DoesNotContain("A task was canceled", msg);
        }

        [Fact]
        public void Provider_errors_keep_their_original_message()
        {
            var ex = new InvalidOperationException("You exceeded your current quota, please check your plan and billing details.");

            Assert.Equal(ex.Message, AiIpcTimeouts.DescribeFailure(ex, new AppSettings()));
        }

        // ─── Spec 036 (US2, FR-009/FR-014, T029): in-dialog provider test failures ────
        // The engine maps provider failures to the five-cause taxonomy; the shell renders the
        // mapped message verbatim and must never let a raw provider payload (JSON body, stack
        // trace) through. These tests use the engine-mapped shapes from contracts/ai-provider-test.md.

        private static AiProviderTestRequest KimiRequest()
            => AiProviderTestRunner.BuildRequest("Kimi (Moonshot)", "kimi-latest", "sk-test-key", "");

        [Fact]
        public void BuildRequest_canonicalises_the_provider_and_never_wraps_the_key()
        {
            var req = AiProviderTestRunner.BuildRequest("Kimi (Moonshot)", "kimi-latest", "sk-plain", "  ");

            Assert.Equal("kimi", req.Provider);                 // display name → canonical id
            Assert.Equal("sk-plain", req.ApiKey);               // sent as the field holds it
            Assert.Null(req.Endpoint);                          // blank endpoint → defaulted engine-side
        }

        [Fact]
        public async Task Engine_not_connected_is_a_distinct_outcome()
        {
            var fake = new FakeRpcClientAccessor { IsConnected = false };

            var (success, message) = await AiProviderTestRunner.RunAsync(fake, KimiRequest(), new AppSettings());

            Assert.False(success);
            Assert.Contains("engine", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not connected", message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(fake.Requests);                        // nothing sent when the pipe is down
        }

        [Fact]
        public async Task No_provider_selected_is_refused_before_ipc()
        {
            var fake = new FakeRpcClientAccessor();
            var req = AiProviderTestRunner.BuildRequest("(None)", "kimi-latest", "sk", "");

            var (success, message) = await AiProviderTestRunner.RunAsync(fake, req, new AppSettings());

            Assert.False(success);
            Assert.Contains("provider", message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(fake.Requests);
        }

        [Fact]
        public async Task Success_renders_with_latency()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.AiProviderTest, new AiProviderTestResponse
            {
                Success = true,
                ModelName = "kimi-latest",
                ProviderVersion = "kimi",
                LatencyMs = 812,
            });

            var (success, message) = await AiProviderTestRunner.RunAsync(fake, KimiRequest(), new AppSettings());

            Assert.True(success);
            Assert.Contains("812", message);
            Assert.DoesNotContain("sk-test-key", message);      // the key is never echoed
        }

        [Fact]
        public async Task The_five_causes_render_distinctly_and_without_raw_payload()
        {
            // One engine-mapped message per FR-014 taxonomy row, as AiProviderTestHandler emits.
            var causes = new[]
            {
                "The API key was rejected by 'kimi' (HTTP 401). Check the key — and the endpoint ('https://api.moonshot.ai/v1'): a key registered on one region's service is not valid on the other.",
                "The model 'kimi-k9' was not found at 'kimi' (HTTP 404). Use a valid model, e.g. \"kimi-latest\", or update the Model field.",
                "Could not reach the AI provider endpoint 'https://api.moonshot.ai/v1'. Check the URL and the network connection.",
                "The 'kimi' account is rate-limited or out of quota (HTTP 429). Check the plan/billing with the provider, or wait and retry.",
                "The provider did not respond within the AI timeout (30s). Increase 'Timeout (seconds)' under Options → AI Assistance.",
            };

            var rendered = new System.Collections.Generic.List<string>();
            foreach (var cause in causes)
            {
                var fake = new FakeRpcClientAccessor();
                fake.Respond(MessageTypes.AiProviderTest, new AiProviderTestResponse
                {
                    Success = false,
                    ErrorMessage = cause,
                    LatencyMs = 500,
                });

                var (success, message) = await AiProviderTestRunner.RunAsync(fake, KimiRequest(), new AppSettings());

                Assert.False(success);
                rendered.Add(message);
            }

            Assert.Equal(causes.Length, rendered.Distinct().Count());   // each cause reads differently

            foreach (var message in rendered)
            {
                Assert.DoesNotContain("{", message);            // no raw JSON body
                Assert.DoesNotContain("}", message);
                Assert.DoesNotContain("   at ", message);       // no stack trace
                Assert.DoesNotContain("disabled", message, StringComparison.OrdinalIgnoreCase); // 429 ≠ "AI is disabled"
                Assert.DoesNotContain("sk-test-key", message);  // the key is never echoed
            }
        }

        [Fact]
        public async Task Quota_failure_never_reads_as_ai_disabled()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.AiProviderTest, new AiProviderTestResponse
            {
                Success = false,
                ErrorMessage = "The 'kimi' account is rate-limited or out of quota (HTTP 429). Check the plan/billing with the provider, or wait and retry.",
            });

            var (_, message) = await AiProviderTestRunner.RunAsync(fake, KimiRequest(), new AppSettings());

            Assert.Contains("quota", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("disabled", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Ipc_timeout_is_described_with_the_wait_and_a_pointer()
        {
            var fake = new FakeRpcClientAccessor();
            fake.Throw(MessageTypes.AiProviderTest, new TaskCanceledException("A task was canceled."));

            var settings = new AppSettings();
            settings.Ai.Timeout = 30;

            var (success, message) = await AiProviderTestRunner.RunAsync(fake, KimiRequest(), settings);

            Assert.False(success);
            Assert.Contains("timed out", message);
            Assert.Contains("60", message);                     // 30 s provider + 30 s margin
            Assert.DoesNotContain("A task was canceled", message);
        }

        // ─── Spec 037 (US5, FR-057, T080): configuration-caused LIVE failures ────
        // A failed live request caused by the agent's configuration names the agent and offers
        // the route to that agent's settings. Momentary states the agent's settings cannot fix
        // (quota, timeout, consent, engine down) keep their bare message — naming an agent there
        // would send the user to a dialog that cannot fix it.

        [Theory]
        [InlineData("The API key was rejected by 'kimi' (HTTP 401). Check the key.")]
        [InlineData("Kimi requires an API key. Set 'ai.apiKey' in config.json or the AKML SQL settings dialog.")]
        [InlineData("Azure OpenAI requires an endpoint URL. Set 'ai.endpoint' to your Azure OpenAI resource URL.")]
        [InlineData("Could not reach the AI provider endpoint 'https://api.moonshot.ai/v1'. Check the URL and the network connection.")]
        [InlineData("The model 'kimi-k9' was not found at 'kimi' (HTTP 404). Use a valid model.")]
        [InlineData("Model 'claude-sonnet-5' is a Anthropic model, but the AI provider is set to 'Gemini'.")]
        [InlineData("Model \"claude-sonnet-5\" is a anthropic model, not a gemini model.")]
        [InlineData("Unknown AI provider: 'azureopenai'. Supported providers: anthropic, openai, azure, gemini, kimi, ollama, lmstudio, custom.")]
        public void Configuration_causes_are_classified_as_such(string error)
            => Assert.True(AiIpcTimeouts.IsConfigurationCaused(error));

        [Theory]
        [InlineData("The 'kimi' account is rate-limited or out of quota (HTTP 429). Check the plan/billing with the provider, or wait and retry.")]
        [InlineData("The AI request timed out after 120s — the provider may be slow or rate-limited. See AKML SQL → View Logs for the provider's last error.")]
        [InlineData("CONSENT_REQUIRED:Data will be sent to kimi. Please confirm in settings.")]
        [InlineData("AI engine is not connected. Please check that the AKML SQL engine is running.")]
        [InlineData("AI assistance is disabled")]
        [InlineData("Request was cancelled")]
        [InlineData("")]
        public void Momentary_states_are_not_configuration_caused(string error)
            => Assert.False(AiIpcTimeouts.IsConfigurationCaused(error));

        [Fact]
        public void A_configuration_caused_live_failure_names_the_agent_and_the_settings_route()
        {
            var msg = AiIpcTimeouts.DescribeLiveFailure(
                "The API key was rejected by 'kimi' (HTTP 401). Check the key.", "Kimi K2");

            Assert.Contains("Kimi K2", msg);                    // names the agent (FR-057)
            Assert.Contains("Options → AI Assistance", msg);    // offers the route to its settings
            Assert.Contains("The API key was rejected", msg);   // the cause survives verbatim
        }

        [Fact]
        public void A_quota_failure_never_names_the_agent_or_the_route()
        {
            const string quota = "The 'kimi' account is rate-limited or out of quota (HTTP 429). " +
                                 "Check the plan/billing with the provider, or wait and retry.";

            var msg = AiIpcTimeouts.DescribeLiveFailure(quota, "Kimi K2");

            Assert.Equal(quota, msg);
            Assert.DoesNotContain("Kimi K2", msg);
            Assert.DoesNotContain("AI Assistance", msg);
        }

        [Fact]
        public void A_timeout_failure_keeps_its_bare_message()
        {
            var timeout = AiIpcTimeouts.DescribeFailure(new TaskCanceledException("A task was canceled."), new AppSettings());

            Assert.Equal(timeout, AiIpcTimeouts.DescribeLiveFailure(timeout, "Kimi K2"));
        }

        [Fact]
        public void No_agent_name_means_no_naming_even_for_a_configuration_cause()
        {
            const string error = "The API key was rejected by 'kimi' (HTTP 401).";

            Assert.Equal(error, AiIpcTimeouts.DescribeLiveFailure(error, null));
            Assert.Equal(error, AiIpcTimeouts.DescribeLiveFailure(error, "  "));
        }

        [StaFact]
        public void The_chat_panel_names_the_agent_and_offers_the_deep_link_on_a_configuration_failure()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Kimi K2");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            string? seenPage = null;
            string? seenAgent = "unset";
            WithPanelSeams(settings, (page, agentId) =>
            {
                seenPage = page;
                seenAgent = agentId;
                return false;   // the user cancels Options — nothing else happens
            }, () =>
            {
                var panel = new AiChatPanel();
                InvokeAddLiveFailureMessage(panel,
                    "The API key was rejected by 'kimi' (HTTP 401). Check the key.", "Kimi K2", agent.Id);

                // The error bubble names the agent and states the route…
                var errorBubble = MessageTexts(panel).Last();
                Assert.Contains("Kimi K2", errorBubble.Text);
                Assert.Contains("Options → AI Assistance", errorBubble.Text);

                // …and the route is a real button deep-linking to THAT agent's settings page.
                var route = Assert.Single(FindAll<Button>(panel),
                    b => AutomationProperties.GetName(b) == "Open Kimi K2 settings");
                Click(route);
                Assert.Equal("AI Assistance", seenPage);
                Assert.Equal(agent.Id, seenAgent);
            });
        }

        [StaFact]
        public void The_chat_panel_offers_no_agent_route_on_a_quota_failure()
        {
            var settings = new AppSettings();
            var agent = UsableAgent("Kimi K2");
            settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agent.Id;

            WithPanelSeams(settings, null, () =>
            {
                var panel = new AiChatPanel();
                InvokeAddLiveFailureMessage(panel,
                    "The 'kimi' account is rate-limited or out of quota (HTTP 429). Check the plan/billing with the provider, or wait and retry.",
                    "Kimi K2", agent.Id);

                // The bare provider message stands; no settings button is offered.
                var errorBubble = MessageTexts(panel).Last();
                Assert.DoesNotContain("Kimi K2", errorBubble.Text);
                Assert.DoesNotContain(FindAll<Button>(panel),
                    b => (AutomationProperties.GetName(b) ?? string.Empty).Contains("settings"));
            });
        }

        // ─── Panel-driving helpers (the AiChatAgentPickerTests idiom) ─────────

        private static AiAgent UsableAgent(string name)
            => new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = "kimi",
                Model = "kimi-latest",
                ApiKey = "sk-test",
                Enabled = true,
            };

        private static void WithPanelSeams(AppSettings settings,
            Func<string?, string?, bool>? showOptionsRoute, Action body)
        {
            var priorSettings = AiChatPanel.TestSettingsProvider;
            var priorRoute = AiChatPanel.ShowOptionsRoute;
            AiChatPanel.TestSettingsProvider = () => settings;
            if (showOptionsRoute != null)
                AiChatPanel.ShowOptionsRoute = showOptionsRoute;
            try
            {
                body();
            }
            finally
            {
                AiChatPanel.TestSettingsProvider = priorSettings;
                AiChatPanel.ShowOptionsRoute = priorRoute;
            }
        }

        private static void InvokeAddLiveFailureMessage(AiChatPanel panel, string error,
            string? agentName, string? agentId)
        {
            var method = typeof(AiChatPanel).GetMethod("AddLiveFailureMessage",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(panel, new object?[] { error, agentName, agentId });
        }

        private static List<TextBox> MessageTexts(DependencyObject root)
            => FindAll<TextBox>(root)
                .Where(tb => AutomationProperties.GetName(tb) == "Message text")
                .ToList();

        private static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            Walk(root, found);
            return found;
        }

        private static void Walk<T>(DependencyObject node, List<T> found) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is T match)
                    found.Add(match);
                if (child is DependencyObject d)
                    Walk(d, found);
            }
        }
    }
}
