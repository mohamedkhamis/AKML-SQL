using System;
using System.Linq;
using System.Threading.Tasks;
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
    /// </summary>
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
    }
}
