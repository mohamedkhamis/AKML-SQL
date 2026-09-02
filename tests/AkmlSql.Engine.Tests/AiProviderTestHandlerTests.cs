using System;
using System.ClientModel;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Ai;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers.Ai;

/// <summary>
/// Spec 036 (US2, FR-014, T030) — <see cref="AiProviderTestHandler"/> maps provider failures to
/// the five-cause taxonomy of <c>contracts/ai-provider-test.md</c>: missing/invalid key,
/// unknown model, unreachable endpoint (naming the URL), quota/rate-limit (never "AI is
/// disabled"), and timeout (naming the value and where to change it). The taxonomy is asserted
/// against synthesised exceptions — live provider calls are not unit-tested. Full detail goes to
/// the log; a raw provider payload must never reach the user.
/// </summary>
public class AiProviderTestHandlerTests
{
    // OpenAI-compatible SDKs (incl. Kimi) throw ClientResultException; Status has a protected
    // setter, so tests subclass it to synthesise specific HTTP statuses without a network.
    private sealed class SynthesizedProviderException : ClientResultException
    {
        public SynthesizedProviderException(int status, string message) : base(message)
            => Status = status;
    }

    // ─── Mapping layer (synthesised exceptions, one case per taxonomy row) ───

    [Fact]
    public void Missing_key_maps_verbatim_from_RequireApiKey()
    {
        var ex = new InvalidOperationException(
            "Kimi requires an API key. Set 'ai.apiKey' in config.json or the AKML SQL settings dialog.");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "kimi", "kimi-latest", null, 30);

        Assert.Equal(ex.Message, msg);
    }

    [Fact]
    public void Rejected_key_401_names_key_provider_and_endpoint()
    {
        // Kimi note (contracts/kimi-provider.md): a .cn key against the .ai endpoint is the
        // likely first-run mistake, so the endpoint is named alongside the key.
        var ex = new SynthesizedProviderException(401, "unauthorized");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "kimi", "kimi-latest", null, 30);

        Assert.Contains("API key", msg);
        Assert.Contains("401", msg);
        Assert.Contains("kimi", msg);
        Assert.Contains("https://api.moonshot.ai/v1", msg);   // default endpoint named even when unset
    }

    [Fact]
    public void Rejected_key_403_is_the_same_cause()
    {
        var ex = new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden);

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "openai", "gpt-4o", null, 30);

        Assert.Contains("API key", msg);
        Assert.Contains("403", msg);
        Assert.Contains("openai", msg);
    }

    [Fact]
    public void Unknown_model_404_names_model_provider_and_a_valid_example()
    {
        var ex = new SynthesizedProviderException(404, "model not found");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "kimi", "kimi-k9-ultra", null, 30);

        Assert.Contains("kimi-k9-ultra", msg);
        Assert.Contains("404", msg);
        Assert.Contains("kimi-latest", msg);                  // a valid example is offered
    }

    [Fact]
    public void Unreachable_endpoint_names_the_url()
    {
        var ex = new HttpRequestException("No such host is known. (no-such-host.example:443)");

        var msg = AiProviderTestHandler.MapFailureMessage(
            ex, "custom", "my-model", "https://no-such-host.example/v1", 30);

        Assert.Contains("https://no-such-host.example/v1", msg);
        Assert.Contains("network", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Quota_429_never_reads_as_ai_disabled()
    {
        var ex = new SynthesizedProviderException(429, "rate limited");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "kimi", "kimi-latest", null, 30);

        Assert.Contains("quota", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("429", msg);
        Assert.DoesNotContain("disabled", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Anthropic_rate_limit_type_maps_to_the_quota_row()
    {
        // Anthropic.SDK's RateLimitsExceeded carries no HTTP status — the type name is the signal.
        var ex = new AnthropicRateLimitLikeException("too many requests");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "anthropic", "claude-sonnet-4-6", null, 30);

        Assert.Contains("quota", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disabled", msg, StringComparison.OrdinalIgnoreCase);
    }

    // Mirrors the shape of Anthropic.SDK.RateLimitsExceeded (HttpRequestException subclass whose
    // name carries the signal) without taking a dependency on the SDK's exact ctor.
    private sealed class AnthropicRateLimitLikeException : HttpRequestException
    {
        public AnthropicRateLimitLikeException(string message) : base(message) { }
    }

    [Fact]
    public void Timeout_names_the_value_and_where_to_change_it()
    {
        var ex = new TaskCanceledException("The operation was canceled.");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "kimi", "kimi-latest", null, 45);

        Assert.Contains("timed", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("45", msg);
        Assert.Contains("Timeout (seconds)", msg);
        Assert.Contains("Options", msg);
    }

    [Fact]
    public void Unexpected_errors_point_to_the_log_and_never_leak_the_raw_payload()
    {
        var ex = new FormatException("{\"error\":{\"message\":\"sk-secret-key invalid\",\"type\":\"auth\"}}");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "kimi", "kimi-latest", null, 30);

        Assert.DoesNotContain("{", msg);
        Assert.DoesNotContain("sk-secret-key", msg);
        Assert.Contains("log", msg, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Handler level (no network — validation and cancellation paths) ───

    private static RpcMessage Request(AiProviderTestRequest req, int requestId = 7) => new()
    {
        MessageType = MessageTypes.AiProviderTest,
        RequestId = requestId,
        Payload = MessagePackSerializer.Serialize(req),
    };

    private static AiProviderTestResponse Unpack(RpcMessage? response)
    {
        Assert.NotNull(response);
        Assert.Equal(MessageTypes.AiProviderTestResult, response!.MessageType);
        return MessagePackSerializer.Deserialize<AiProviderTestResponse>(response.Payload!);
    }

    [Fact]
    public async Task Empty_payload_is_a_clean_failure()
    {
        var handler = new AiProviderTestHandler();

        var response = Unpack(await handler.HandleAsync(
            new RpcMessage { MessageType = MessageTypes.AiProviderTest, RequestId = 3, Payload = null },
            CancellationToken.None));

        Assert.False(response.Success);
        Assert.Contains("payload", response.ErrorMessage);
    }

    [Fact]
    public async Task Missing_provider_and_model_are_named_before_any_client_is_built()
    {
        var handler = new AiProviderTestHandler();

        var noProvider = Unpack(await handler.HandleAsync(
            Request(new AiProviderTestRequest { Provider = "", Model = "kimi-latest" }), CancellationToken.None));
        Assert.False(noProvider.Success);
        Assert.Contains("Provider", noProvider.ErrorMessage);

        var noModel = Unpack(await handler.HandleAsync(
            Request(new AiProviderTestRequest { Provider = "kimi", Model = " " }), CancellationToken.None));
        Assert.False(noModel.Success);
        Assert.Contains("Model", noModel.ErrorMessage);
    }

    [Fact]
    public async Task Kimi_without_a_key_fails_naming_kimi_without_network()
    {
        var handler = new AiProviderTestHandler();

        var response = Unpack(await handler.HandleAsync(
            Request(new AiProviderTestRequest { Provider = "kimi", Model = "kimi-latest", ApiKey = "" }),
            CancellationToken.None));

        Assert.False(response.Success);
        Assert.Contains("Kimi", response.ErrorMessage);
        Assert.Contains("API key", response.ErrorMessage);
    }

    [Fact]
    public async Task Shell_side_cancellation_reports_cancelled_not_timeout()
    {
        var handler = new AiProviderTestHandler();
        var cancelled = new CancellationToken(canceled: true);

        var response = Unpack(await handler.HandleAsync(
            Request(new AiProviderTestRequest { Provider = "kimi", Model = "kimi-latest", ApiKey = "sk-test" }),
            cancelled));

        Assert.False(response.Success);
        Assert.Equal("Test was cancelled.", response.ErrorMessage);
    }

    [Fact]
    public async Task Response_echoes_the_request_id()
    {
        var handler = new AiProviderTestHandler();

        var response = await handler.HandleAsync(
            Request(new AiProviderTestRequest { Provider = "", Model = "" }, requestId: 42),
            CancellationToken.None);

        Assert.Equal(42, response!.RequestId);
    }
}
