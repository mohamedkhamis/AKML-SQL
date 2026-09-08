#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US5) T078/T079/T081 — per-agent health: the four FR-053 statuses with their
    /// badge labels and the last-checked time, <c>needsKey</c> computed LOCALLY with zero RPC
    /// calls (FR-055), health reset to <c>unknown</c> on any provider/model/key/endpoint edit
    /// (R10), the FR-056 failure taxonomy rendering each cause distinctly, and the V24 guarantee
    /// that no API key reaches a health <c>Message</c>, the status line, or a log call (FR-058).
    ///
    /// <para>The page is built through <see cref="SettingsWindow.TestBuildWindowForRenderTest()"/>
    /// (the <c>AiAgentListPageTests</c> idiom). The Test-connection flow runs against
    /// <see cref="FakeRpcClientAccessor"/> injected through
    /// <see cref="AiAssistanceControls.TestRpcAccessor"/> and is awaited directly — with the fake,
    /// every await in the flow completes synchronously on the test thread.</para>
    /// </summary>
    public class AiAgentHealthTests
    {
        // ── T078: the four statuses, their labels, the last-checked time ─────

        [StaFact]
        public void The_four_statuses_render_their_fr053_labels_in_the_list_rows()
        {
            var untested = MakeAgent("Untested");                                   // Health null
            var ready = MakeAgent("Ready");
            ready.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 812 };
            var keyless = MakeAgent("Keyless", provider: "kimi", model: "kimi-latest", apiKey: "");
            var failed = MakeAgent("Failed");
            failed.Health = new AgentHealth { Status = AgentHealthStatus.Failed, Message = "boom" };

            var (_, controls) = BuildPage(SettingsWith(untested, ready, keyless, failed));

            Assert.Contains(RowTexts(controls.ListView.List, 0), t => t == "– Not tested");
            Assert.Contains(RowTexts(controls.ListView.List, 1), t => t == "✔ Ready");
            // needsKey is DERIVED, not read back: a keyless cloud agent reports it (FR-055).
            Assert.Contains(RowTexts(controls.ListView.List, 2), t => t == "⚠ Needs API key");
            Assert.Contains(RowTexts(controls.ListView.List, 3), t => t == "✖ Failed");
        }

        [StaFact]
        public void The_badge_carries_the_last_checked_time_when_one_was_recorded()
        {
            // Noon LOCAL today is "today" in every timezone, so the expectation is deterministic.
            var checkedLocal = DateTime.Today.AddHours(12);
            var agent = MakeAgent("Claude (work)");
            agent.Health = new AgentHealth
            {
                Status = AgentHealthStatus.Ready,
                LatencyMs = 812,
                CheckedUtc = checkedLocal.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            };
            var (_, controls) = BuildPage(SettingsWith(agent));

            var expectedTime = checkedLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            Assert.Equal("✔ Ready · " + expectedTime, AiAgentListView.HealthBadgeTextFor(agent));
            Assert.Contains(RowTexts(controls.ListView.List, 0),
                t => t.StartsWith("✔ Ready", StringComparison.Ordinal) && t.Contains(expectedTime));

            // A check from another day shows its date, not just the time.
            var olderLocal = DateTime.Today.AddDays(-2).AddHours(12);
            var older = MakeAgent("Older");
            older.Health = new AgentHealth
            {
                Status = AgentHealthStatus.Failed,
                CheckedUtc = olderLocal.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            };
            var expectedDate = olderLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            Assert.Equal("✖ Failed · " + expectedDate, AiAgentListView.HealthBadgeTextFor(older));

            // Never checked → the bare label, no time.
            Assert.Equal("– Not tested", AiAgentListView.HealthBadgeTextFor(MakeAgent("Fresh")));
            // An unparseable stamp degrades to the bare label rather than throwing.
            var broken = MakeAgent("Broken");
            broken.Health = new AgentHealth { Status = AgentHealthStatus.Ready, CheckedUtc = "not-a-date" };
            Assert.Equal("✔ Ready", AiAgentListView.HealthBadgeTextFor(broken));
        }

        [StaFact]
        public void Badge_colours_are_semantic_and_meet_contrast_on_the_list_surface()
        {
            // The documented theme-token exception (contracts/options-agents-ui.md § Theme and
            // accessibility): green ready, red failed, amber not-tested / needs-key — chosen per
            // variant so the badge clears WCAG 4.5:1 on the list's Input surface
            // (#FFFFFF in Light, #1E293B in Dark).
            var lightInput = Colors.White;
            var darkInput = Color.FromRgb(0x1E, 0x29, 0x3B);

            foreach (var status in new[]
                     { AgentHealthStatus.Ready, AgentHealthStatus.Failed,
                       AgentHealthStatus.NeedsKey, AgentHealthStatus.Unknown })
            {
                var light = AiAgentListView.BadgeBrushFor(status, dark: false);
                var dark = AiAgentListView.BadgeBrushFor(status, dark: true);
                Assert.True(ContrastRatio(lightInput, light.Color) >= 4.5,
                    $"{status} light badge {light.Color} is below 4.5:1 on the light list surface");
                Assert.True(ContrastRatio(darkInput, dark.Color) >= 4.5,
                    $"{status} dark badge {dark.Color} is below 4.5:1 on the dark list surface");
            }

            // The three semantics read as three distinct colours.
            var ready = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Ready, dark: false).Color;
            var failed = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Failed, dark: false).Color;
            var unknown = AiAgentListView.BadgeBrushFor(AgentHealthStatus.Unknown, dark: false).Color;
            Assert.NotEqual(ready, failed);
            Assert.NotEqual(ready, unknown);
            Assert.NotEqual(failed, unknown);
        }

        [StaFact]
        public void The_badge_flips_off_its_semantic_colour_on_hover_and_selection()
        {
            var ready = MakeAgent("Ready");
            ready.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 10 };
            var (_, controls) = BuildPage(SettingsWith(ready));

            var badge = FindBadgeTextBlock(controls.ListView.List, 0, "✔ Ready");

            // The spec-036 bug class: a LOCAL semantic foreground that cannot flip would stay
            // green-on-accent when the row is selected. The badge therefore carries
            // ancestor-state triggers pairing it back to the row's hover/selected foregrounds.
            Assert.NotNull(badge.Style);
            var triggers = badge.Style.Triggers.OfType<DataTrigger>().ToList();
            var hover = Assert.Single(triggers, t => IsAncestorTrigger(t, "IsMouseOver"));
            var selected = Assert.Single(triggers, t => IsAncestorTrigger(t, "IsSelected"));
            Assert.Equal(PageTheme.Light.FgPrimary, ForegroundSetter(hover));
            Assert.Equal(PageTheme.Light.SelectedText, ForegroundSetter(selected));
        }

        // ── T078: needsKey is local (FR-055) ─────────────────────────────────

        [StaFact]
        public void NeedsKey_is_computed_locally_with_zero_requests()
        {
            var good = MakeAgent("Claude (work)");
            var keyless = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest", apiKey: "");
            var fake = new FakeRpcClientAccessor();

            WithRpc(fake, () =>
            {
                var (_, controls) = BuildPage(SettingsWith(good, keyless));

                // FR-055: the keyless cloud agent reports needsKey the moment the list renders…
                Assert.Equal(AgentHealthStatus.NeedsKey, controls.WorkingAgents[1].Health?.Status);
                Assert.Contains(RowTexts(controls.ListView.List, 1), t => t == "⚠ Needs API key");
                Assert.Equal(AgentHealthStatus.Unknown, controls.WorkingAgents[0].Health?.Status ?? AgentHealthStatus.Unknown);

                // …and nothing was sent to compute it — not one request.
                Assert.Empty(fake.Requests);
            });
        }

        [StaFact]
        public void An_undecryptable_key_reports_needsKey_once_the_agent_is_bound()
        {
            var bad = MakeAgent("Bad");
            bad.ApiKey = "dpapi:" + Convert.ToBase64String(new byte[64]); // valid shape, bad HMAC
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Good"), bad));

            controls.ListView.List.SelectedIndex = 1;   // binding derives KeyDecryptFailed (V23)

            Assert.True(controls.WorkingAgents[1].KeyDecryptFailed);
            Assert.Equal(AgentHealthStatus.NeedsKey, controls.WorkingAgents[1].Health?.Status);
            Assert.Contains(RowTexts(controls.ListView.List, 1), t => t == "⚠ Needs API key");
        }

        [StaFact]
        public void An_unselected_agents_undecryptable_key_reports_needsKey_from_the_first_render()
        {
            var good = MakeAgent("Good");
            var bad = MakeAgent("Bad");
            bad.ApiKey = "dpapi:" + Convert.ToBase64String(new byte[64]); // valid shape, bad HMAC
            var (_, controls) = BuildPage(SettingsWith(good, bad));

            // Bad is NEVER selected (the initial selection is the active agent) — yet its badge
            // is already right: the flag is derived for every agent at Load (FR-053), not first
            // derived on bind. Before the fix the badge stayed stale until the user clicked Bad.
            Assert.Equal(good.Id, controls.SelectedAgentId);
            Assert.True(controls.WorkingAgents[1].KeyDecryptFailed);
            Assert.Equal(AgentHealthStatus.NeedsKey, controls.WorkingAgents[1].Health?.Status);
            Assert.Contains(RowTexts(controls.ListView.List, 1), t => t == "⚠ Needs API key");
        }

        [StaFact]
        public void Supplying_a_key_clears_needsKey_back_to_unknown()
        {
            var keyless = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest", apiKey: "");
            var (_, controls) = BuildPage(SettingsWith(keyless, MakeAgent("Claude (work)")));
            Assert.Equal(AgentHealthStatus.NeedsKey, controls.WorkingAgents[0].Health?.Status);

            KeyBox(controls).Text = "sk-fresh";
            controls.ListView.List.SelectedIndex = 1;   // leaving the agent commits the key

            Assert.Equal(AgentHealthStatus.Unknown, controls.WorkingAgents[0].Health?.Status);
            Assert.Contains(RowTexts(controls.ListView.List, 0), t => t == "– Not tested");
        }

        // ── T078: edits invalidate health (R10) ──────────────────────────────

        [StaTheory]
        [InlineData("provider", AgentHealthStatus.Ready)]
        [InlineData("model", AgentHealthStatus.Ready)]
        [InlineData("key", AgentHealthStatus.Ready)]
        [InlineData("endpoint", AgentHealthStatus.Ready)]
        [InlineData("model", AgentHealthStatus.Failed)]
        [InlineData("key", AgentHealthStatus.NeedsKey)]
        public void Any_connection_edit_resets_health_to_unknown_from_any_state(string field, string initialStatus)
        {
            var agent = MakeAgent("Claude (work)");
            agent.Health = new AgentHealth { Status = initialStatus, LatencyMs = 100, Message = "prior" };
            var (_, controls) = BuildPage(SettingsWith(agent));

            switch (field)
            {
                case "provider": ProviderCombo(controls).SelectedIndex = 4; break;  // Anthropic → Gemini
                case "model": ModelBox(controls).Text = "claude-opus-4-8"; break;
                case "key": KeyBox(controls).Text = "sk-replacement"; break;
                case "endpoint": EndpointBox(controls).Text = "https://edited.example.com"; break;
            }

            // R10: from ANY state, the recorded health no longer applies.
            Assert.Equal(AgentHealthStatus.Unknown, controls.WorkingAgents[0].Health?.Status);
        }

        [StaFact]
        public void Loading_and_switching_agents_does_not_reset_health()
        {
            var ready = MakeAgent("Ready");
            ready.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 812 };
            var (_, controls) = BuildPage(SettingsWith(ready, MakeAgent("Other")));

            // A bind is not an edit: the ready badge survives Load…
            Assert.Equal(AgentHealthStatus.Ready, controls.WorkingAgents[0].Health?.Status);

            controls.ListView.List.SelectedIndex = 1;
            controls.ListView.List.SelectedIndex = 0;   // …and a re-bind on selection change.

            Assert.Equal(AgentHealthStatus.Ready, controls.WorkingAgents[0].Health?.Status);
        }

        // ── T079: the FR-056 failure taxonomy ────────────────────────────────

        [StaFact]
        public async Task The_fr056_causes_each_render_a_distinct_message()
        {
            var messages = new List<string>
            {
                await TestFailureStatusLineAsync(MakeAgent("Kimi", provider: "kimi", model: "kimi-latest"),
                    "The API key was rejected by 'kimi' (HTTP 401). Check the key — and the endpoint: a key issued for a different service or region will be rejected."),
                await TestFailureStatusLineAsync(MakeAgent("Kimi", provider: "kimi", model: "kimi-latest"),
                    "Could not reach the AI provider endpoint 'https://api.moonshot.ai/v1'. Check the URL and the network connection."),
                await TestFailureStatusLineAsync(MakeAgent("Kimi", provider: "kimi", model: "kimi-k9"),
                    "The model 'kimi-k9' was not found at 'kimi' (HTTP 404). Use a valid model, e.g. \"kimi-latest\", or update the Model field."),
                await FamilyMismatchStatusLineAsync(),
                await MissingEndpointStatusLineAsync(),
                await EngineNotConnectedStatusLineAsync(),
            };

            Assert.Equal(6, messages.Distinct().Count());
        }

        [StaFact]
        public async Task An_auth_failure_marks_the_agent_failed_with_the_mapped_message()
        {
            const string authMessage = "The API key was rejected by 'kimi' (HTTP 401). Check the key.";
            var agent = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.AiProviderTest,
                new AiProviderTestResponse { Success = false, ErrorMessage = authMessage });

            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));

                await controls.RunProviderTestAsync();

                Assert.Equal(authMessage, TestResult(controls).Text);
                var health = controls.WorkingAgents[0].Health;
                Assert.NotNull(health);
                Assert.Equal(AgentHealthStatus.Failed, health!.Status);
                Assert.Equal(authMessage, health.Message);
                Assert.True(DateTime.TryParse(health.CheckedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var stamp) && stamp <= DateTime.UtcNow,
                    "a failed check still records when it ran");
            });
        }

        [StaFact]
        public async Task A_family_mismatch_is_refused_locally_before_anything_is_sent()
        {
            var agent = MakeAgent("Gemini", provider: "gemini", model: "gemini-flash-latest");
            var fake = new FakeRpcClientAccessor();

            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));
                ModelBox(controls).Text = "claude-sonnet-5";   // V7 — quickstart scenario 56

                await controls.RunProviderTestAsync();

                var message = TestResult(controls).Text;
                Assert.Contains("claude-sonnet-5", message);
                Assert.Contains("anthropic", message);
                Assert.Contains("gemini", message);
                Assert.Empty(fake.Requests);                        // nothing left the machine
                Assert.Null(controls.WorkingAgents[0].Health);      // no check ran — no health written
            });
        }

        [StaFact]
        public async Task A_missing_endpoint_is_refused_locally_before_anything_is_sent()
        {
            var agent = MakeAgent("Azure", provider: "azure", model: "gpt-4o", endpoint: "");
            var fake = new FakeRpcClientAccessor();

            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));

                await controls.RunProviderTestAsync();

                Assert.Contains("Endpoint is required", TestResult(controls).Text);
                Assert.Empty(fake.Requests);                        // quickstart scenario 55
                Assert.Null(controls.WorkingAgents[0].Health);
            });
        }

        [StaFact]
        public async Task An_unreachable_engine_is_a_distinct_outcome_and_sends_nothing()
        {
            var agent = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var fake = new FakeRpcClientAccessor { IsConnected = false };

            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));

                await controls.RunProviderTestAsync();

                var message = TestResult(controls).Text;
                Assert.Contains("engine", message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("not connected", message, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(fake.Requests);
                Assert.Equal(AgentHealthStatus.Failed, controls.WorkingAgents[0].Health?.Status);
            });
        }

        // ── T084/T085: the test flow and the health it records ───────────────

        [StaFact]
        public async Task Test_uses_the_editors_current_values_not_the_saved_ones()
        {
            var agent = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest", apiKey: "sk-saved");
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.AiProviderTest,
                new AiProviderTestResponse { Success = true, ModelName = "kimi-k2-thinking", LatencyMs = 50 });

            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));
                ModelBox(controls).Text = "kimi-k2-thinking";   // typed, never saved (FR-054)
                KeyBox(controls).Text = "sk-current";

                await controls.RunProviderTestAsync();

                var sent = Assert.Single(fake.Requests);
                var request = Assert.IsType<AiProviderTestRequest>(sent.Payload);
                Assert.Equal("kimi-k2-thinking", request.Model);
                Assert.Equal("sk-current", request.ApiKey);
                Assert.Equal("kimi", request.Provider);
            });
        }

        [StaFact]
        public async Task A_successful_test_marks_the_agent_ready_with_latency_and_check_time()
        {
            var agent = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var fake = new FakeRpcClientAccessor();
            fake.Respond<AiProviderTestRequest>(MessageTypes.AiProviderTest, _ =>
            {
                // Give the round-trip stopwatch something to measure.
                System.Threading.Thread.Sleep(50);
                return new AiProviderTestResponse { Success = true, ModelName = "kimi-latest", LatencyMs = 812 };
            });

            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));

                await controls.RunProviderTestAsync();

                var health = controls.WorkingAgents[0].Health;
                Assert.NotNull(health);
                Assert.Equal(AgentHealthStatus.Ready, health!.Status);
                Assert.True(health.LatencyMs > 0, "the round trip of the successful check is recorded");
                Assert.True(DateTime.TryParse(health.CheckedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var stamp) && stamp <= DateTime.UtcNow);
                Assert.Contains("Connection succeeded", health.Message);
                Assert.Contains(RowTexts(controls.ListView.List, 0),
                    t => t.StartsWith("✔ Ready", StringComparison.Ordinal));
            });
        }

        [StaFact]
        public async Task A_failed_test_marks_the_agent_failed_and_caps_the_message_at_500_chars()
        {
            var agent = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var longMessage = "The provider test failed: " + new string('x', 700);
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.AiProviderTest,
                new AiProviderTestResponse { Success = false, ErrorMessage = longMessage });

            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));

                await controls.RunProviderTestAsync();

                var health = controls.WorkingAgents[0].Health;
                Assert.NotNull(health);
                Assert.Equal(AgentHealthStatus.Failed, health!.Status);
                Assert.Equal(0, health!.LatencyMs);   // LatencyMs records the last SUCCESSFUL check (E2)
                Assert.Equal(500, health.Message.Length);
                Assert.Contains(RowTexts(controls.ListView.List, 0),
                    t => t.StartsWith("✖ Failed", StringComparison.Ordinal));
            });
        }

        [StaFact]
        public async Task A_failed_test_zeroes_the_latency_of_the_previous_success()
        {
            // E2: latencyMs is 0 when the last check did not succeed — an earlier success's
            // number must not stand next to a fresh failure.
            var agent = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            agent.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 812 };
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.AiProviderTest,
                new AiProviderTestResponse { Success = false, ErrorMessage = "boom" });

            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));

                await controls.RunProviderTestAsync();

                var health = controls.WorkingAgents[0].Health;
                Assert.NotNull(health);
                Assert.Equal(AgentHealthStatus.Failed, health!.Status);
                Assert.Equal(0, health.LatencyMs);
            });
        }

        // ── T081: no key anywhere (FR-058 / V24) ─────────────────────────────

        [StaFact]
        public async Task No_api_key_reaches_any_health_message_status_line_or_log_call()
        {
            var secret = "sk-secret-" + Guid.NewGuid().ToString("N");
            var agent = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var sink = new CollectingSink();
            var priorLogger = Log.Logger;
            Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
            try
            {
                var fake = new FakeRpcClientAccessor();
                fake.Respond(MessageTypes.AiProviderTest, new AiProviderTestResponse
                {
                    Success = false,
                    ErrorMessage = "The API key was rejected by 'kimi' (HTTP 401). Check the key.",
                });

                await WithRpcAsync(fake, async () =>
                {
                    var (_, controls) = BuildPage(SettingsWith(agent));
                    KeyBox(controls).Text = secret;   // the key under test — typed, never saved

                    // Failure via an engine-mapped message…
                    await controls.RunProviderTestAsync();
                    Assert.DoesNotContain(secret, controls.WorkingAgents[0].Health?.Message);
                    Assert.DoesNotContain(secret, TestResult(controls).Text);

                    // …failure via an IPC exception (the runner's own log path)…
                    fake.Throw(MessageTypes.AiProviderTest, new System.Threading.Tasks.TaskCanceledException("A task was canceled."));
                    await controls.RunProviderTestAsync();
                    Assert.DoesNotContain(secret, controls.WorkingAgents[0].Health?.Message);
                    Assert.DoesNotContain(secret, TestResult(controls).Text);

                    // …and a success (which names provider/model/latency only).
                    fake.Respond(MessageTypes.AiProviderTest,
                        new AiProviderTestResponse { Success = true, ModelName = "kimi-latest", LatencyMs = 3 });
                    await controls.RunProviderTestAsync();
                    Assert.DoesNotContain(secret, controls.WorkingAgents[0].Health?.Message);
                    Assert.DoesNotContain(secret, TestResult(controls).Text);
                });
            }
            finally
            {
                Log.Logger = priorLogger;
            }

            // Assert on a locked snapshot AFTER the logger is restored: the sink is shared with
            // every test running in parallel, so live enumeration would race their emissions.
            List<LogEvent> captured;
            lock (sink.Events) captured = sink.Events.ToList();
            Assert.NotEmpty(captured);   // the runner's catch-path warning must have been captured
            foreach (var evt in captured)
            {
                Assert.DoesNotContain(secret, evt.RenderMessage());
                Assert.DoesNotContain(secret, evt.Exception?.ToString() ?? string.Empty);
            }
        }

        // ── Scenario drivers ─────────────────────────────────────────────────

        private static async Task<string> TestFailureStatusLineAsync(AiAgent agent, string engineMessage)
        {
            var fake = new FakeRpcClientAccessor();
            fake.Respond(MessageTypes.AiProviderTest,
                new AiProviderTestResponse { Success = false, ErrorMessage = engineMessage });
            string statusLine = string.Empty;
            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(agent));
                await controls.RunProviderTestAsync();
                statusLine = TestResult(controls).Text;
                Assert.Single(fake.Requests);   // an engine-mapped failure still means a request went out
            });
            return statusLine;
        }

        private static async Task<string> FamilyMismatchStatusLineAsync()
        {
            var fake = new FakeRpcClientAccessor();
            string statusLine = string.Empty;
            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(
                    MakeAgent("Gemini", provider: "gemini", model: "gemini-flash-latest")));
                ModelBox(controls).Text = "claude-sonnet-5";
                await controls.RunProviderTestAsync();
                statusLine = TestResult(controls).Text;
                Assert.Empty(fake.Requests);
            });
            return statusLine;
        }

        private static async Task<string> MissingEndpointStatusLineAsync()
        {
            var fake = new FakeRpcClientAccessor();
            string statusLine = string.Empty;
            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(
                    MakeAgent("Azure", provider: "azure", model: "gpt-4o", endpoint: "")));
                await controls.RunProviderTestAsync();
                statusLine = TestResult(controls).Text;
                Assert.Empty(fake.Requests);
            });
            return statusLine;
        }

        private static async Task<string> EngineNotConnectedStatusLineAsync()
        {
            var fake = new FakeRpcClientAccessor { IsConnected = false };
            string statusLine = string.Empty;
            await WithRpcAsync(fake, async () =>
            {
                var (_, controls) = BuildPage(SettingsWith(
                    MakeAgent("Kimi", provider: "kimi", model: "kimi-latest")));
                await controls.RunProviderTestAsync();
                statusLine = TestResult(controls).Text;
                Assert.Empty(fake.Requests);
            });
            return statusLine;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new List<LogEvent>();
            public void Emit(LogEvent logEvent)
            {
                lock (Events) Events.Add(logEvent);
            }
        }

        private static void WithRpc(FakeRpcClientAccessor fake, Action body)
        {
            var prior = AiAssistanceControls.TestRpcAccessor;
            AiAssistanceControls.TestRpcAccessor = fake;
            try
            {
                body();
            }
            finally
            {
                AiAssistanceControls.TestRpcAccessor = prior;
            }
        }

        private static async Task WithRpcAsync(FakeRpcClientAccessor fake, Func<Task> body)
        {
            var prior = AiAssistanceControls.TestRpcAccessor;
            AiAssistanceControls.TestRpcAccessor = fake;
            try
            {
                await body();
            }
            finally
            {
                AiAssistanceControls.TestRpcAccessor = prior;
            }
        }

        private static AiAgent MakeAgent(string name, string provider = "anthropic",
            string model = "claude-sonnet-4-6", string apiKey = "sk-test", string endpoint = "")
        {
            return new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = provider,
                Model = model,
                ApiKey = apiKey.Length == 0 ? string.Empty : ApiKeyProtector.Protect(apiKey),
                Endpoint = endpoint,
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            };
        }

        private static AppSettings SettingsWith(params AiAgent[] agents)
        {
            var settings = new AppSettings();
            foreach (var agent in agents) settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agents.Length > 0 ? agents[0].Id : string.Empty;
            return settings;
        }

        private static (SettingsWindow Dialog, AiAssistanceControls Controls) BuildPage(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (Dictionary<string, IPageControls>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var controls), "AI Assistance controls not found.");
            return (dialog, (AiAssistanceControls)controls!);
        }

        /// <summary>The visible text runs of one rendered agent row (marker, name, detail, badge).</summary>
        private static List<string> RowTexts(ListBox list, int index)
        {
            var item = (ListBoxItem)list.Items[index];
            var texts = new List<string>();
            foreach (var block in LogicalTree.Descendants<TextBlock>((DependencyObject)item.Content))
                texts.Add(block.Text);
            return texts;
        }

        private static TextBlock FindBadgeTextBlock(ListBox list, int index, string badgeStart)
        {
            var item = (ListBoxItem)list.Items[index];
            var badge = LogicalTree.Descendants<TextBlock>((DependencyObject)item.Content)
                .FirstOrDefault(t => t.Text.StartsWith(badgeStart, StringComparison.Ordinal));
            Assert.NotNull(badge);
            return badge!;
        }

        private static bool IsAncestorTrigger(DataTrigger trigger, string propertyName)
        {
            if (trigger.Binding is not Binding binding) return false;
            if (binding.RelativeSource?.Mode != RelativeSourceMode.FindAncestor) return false;
            if (binding.RelativeSource.AncestorType != typeof(ListBoxItem)) return false;
            return binding.Path?.Path == propertyName;
        }

        private static SolidColorBrush ForegroundSetter(DataTrigger trigger)
        {
            var setter = trigger.Setters.OfType<Setter>()
                .FirstOrDefault(s => s.Property == TextBlock.ForegroundProperty);
            Assert.NotNull(setter);
            return Assert.IsType<SolidColorBrush>(setter!.Value);
        }

        // WCAG 2.x relative-luminance contrast, the same math OptionsHoverContrastTests sweeps with.
        private static double ContrastRatio(Color a, Color b)
        {
            var la = RelativeLuminance(a);
            var lb = RelativeLuminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            static double Channel(byte v)
            {
                var s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static ComboBox ProviderCombo(AiAssistanceControls controls) => GetField<ComboBox>(controls, "_provider");
        private static TextBox ModelBox(AiAssistanceControls controls) => GetField<TextBox>(controls, "_model");
        private static TextBox KeyBox(AiAssistanceControls controls) => GetField<TextBox>(controls, "_apiKey");
        private static TextBox EndpointBox(AiAssistanceControls controls) => GetField<TextBox>(controls, "_endpoint");
        private static TextBlock TestResult(AiAssistanceControls controls) => GetField<TextBlock>(controls, "_testResult");

        private static T GetField<T>(object instance, string name) where T : class
        {
            var f = instance.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(f != null, $"field {name} not found on {instance.GetType().Name}");
            var value = f!.GetValue(instance) as T;
            Assert.NotNull(value);
            return value!;
        }
    }
}
