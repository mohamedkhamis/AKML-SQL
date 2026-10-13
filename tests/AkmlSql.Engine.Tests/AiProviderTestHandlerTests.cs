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
using GeminiApiException = Mscc.GenerativeAI.Types.GeminiApiException;

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
        Assert.Contains("'Timeout'", msg);   // the Options label (spec 040 moved the unit out of it)
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

    // ─── Gemini (Mscc.GenerativeAI): GeminiApiException keeps the failed response ───
    // Bodies are what Google actually returned (2026-10-13 engine log, and a junk-key probe).
    // Before the status was read off the exception, every one of these mapped to "unexpected
    // error (GeminiApiException)".

    private static GeminiApiException Gemini(HttpStatusCode status, string body) =>
        new("The request was not successful. Last API response:\n" + body,
            new HttpResponseMessage(status), null);

    [Fact]
    public void Gemini_404_unknown_model_maps_to_the_model_row()
    {
        var ex = Gemini(HttpStatusCode.NotFound,
            "{\"error\":{\"code\":404,\"message\":\"models/gemini-3.8-flash-lite is not found for API version v1beta, " +
            "or is not supported for generateContent.\",\"status\":\"NOT_FOUND\"}}");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "gemini", "gemini-3.8-flash-lite", null, 30);

        Assert.Contains("gemini-3.8-flash-lite", msg);
        Assert.Contains("404", msg);
        Assert.Contains("gemini-flash-latest", msg);          // a valid example is offered
        Assert.DoesNotContain("GeminiApiException", msg);
        Assert.DoesNotContain("{", msg);
    }

    [Fact]
    public void Gemini_503_high_demand_reads_as_a_provider_outage_not_an_unexpected_error()
    {
        var ex = Gemini(HttpStatusCode.ServiceUnavailable,
            "{\"error\":{\"code\":503,\"message\":\"This model is currently experiencing high demand. Spikes in " +
            "demand are usually temporary. Please try again later.\",\"status\":\"UNAVAILABLE\"}}");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "gemini", "gemini-3.7-flash", null, 30);

        Assert.Contains("'gemini'", msg);
        Assert.Contains("503", msg);
        Assert.Contains("temporarily unavailable", msg);
        Assert.Contains("Retry", msg);
        Assert.DoesNotContain("unexpected", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GeminiApiException", msg);
        Assert.DoesNotContain("{", msg);
    }

    [Fact]
    public void Gemini_bad_key_400_API_KEY_INVALID_maps_to_the_rejected_key_row()
    {
        // Google never answers a bad key with 401/403 — it is 400 INVALID_ARGUMENT.
        var ex = Gemini(HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\"," +
            "\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\"," +
            "\"reason\":\"API_KEY_INVALID\",\"domain\":\"googleapis.com\"}]}}");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "gemini", "gemini-flash-latest", null, 30);

        Assert.Contains("API key", msg);
        Assert.Contains("rejected", msg);
        Assert.Contains("'gemini'", msg);
        Assert.Contains("400", msg);
        Assert.DoesNotContain("('')", msg);                   // no endpoint to name for Gemini
        Assert.DoesNotContain("{", msg);
    }

    [Fact]
    public void Gemini_429_maps_to_the_quota_row()
    {
        var ex = Gemini(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"code\":429,\"message\":\"You exceeded your current quota.\",\"status\":\"RESOURCE_EXHAUSTED\"}}");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "gemini", "gemini-flash-latest", null, 30);

        Assert.Contains("quota", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("429", msg);
    }

    [Fact]
    public void Gemini_other_400_keeps_the_status_and_never_leaks_the_body()
    {
        var ex = Gemini(HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":400,\"message\":\"Invalid value at 'generation_config' AIzaSy-secret-echo\"," +
            "\"status\":\"INVALID_ARGUMENT\"}}");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "gemini", "gemini-flash-latest", null, 30);

        Assert.Contains("HTTP 400", msg);
        Assert.Contains("log", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API key was rejected", msg);
        Assert.DoesNotContain("AIzaSy-secret-echo", msg);
        Assert.DoesNotContain("{", msg);
    }

    [Fact]
    public void Gemini_exception_without_a_response_falls_back_to_the_generic_row()
    {
        var ex = new GeminiApiException("The request was not successful.");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "gemini", "gemini-flash-latest", null, 30);

        Assert.Contains("unexpected error", msg);
        Assert.Contains("log", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gemini_sdk_deadline_wrapped_without_a_response_maps_to_the_timeout_row()
    {
        // Mscc's HttpRequestTimeoutHandler throws TimeoutException at the per-request deadline;
        // BaseModel.SendAsync's catch-all wraps it with Response == null.
        var ex = new GeminiApiException("The request was not successful.", new TimeoutException());

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "gemini", "gemini-3.7-flash", null, 45);

        Assert.Contains("timed out", msg);
        Assert.Contains("45", msg);
        Assert.Contains("'Timeout'", msg);
        Assert.DoesNotContain("GeminiApiException", msg);
    }

    [Fact]
    public void Gemini_wrapped_cancellation_with_a_live_token_maps_to_the_timeout_row()
    {
        var ex = new GeminiApiException("The request was not successful.", new TaskCanceledException());

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "gemini", "gemini-flash-latest", null, 30);

        Assert.Contains("timed out", msg);
        Assert.DoesNotContain("unexpected", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Anthropic_401_AuthenticationException_maps_to_the_rejected_key_row()
    {
        // Anthropic.SDK answers 401 with a status-less AuthenticationException whose message
        // carries the raw response body.
        var ex = new System.Security.Authentication.AuthenticationException(
            "Anthropic rejected your authorization, most likely due to an invalid API Key. Full API response follows: " +
            "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "anthropic", "claude-sonnet-4-6", null, 30);

        Assert.Contains("API key", msg);
        Assert.Contains("rejected", msg);
        Assert.Contains("'anthropic'", msg);
        Assert.Contains("HTTP 401", msg);
        Assert.DoesNotContain("{", msg);
        Assert.DoesNotContain("Full API response", msg);
    }

    [Fact]
    public void A_TLS_failure_is_an_unreachable_endpoint_not_a_rejected_key()
    {
        // TLS handshake failures arrive with AuthenticationException INSIDE an HttpRequestException.
        var ex = new HttpRequestException("The SSL connection could not be established.",
            new System.Security.Authentication.AuthenticationException("The remote certificate is invalid."));

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "custom", "my-model", "https://llm.example/v1", 30);

        Assert.Contains("Could not reach", msg);
        Assert.Contains("https://llm.example/v1", msg);
        Assert.DoesNotContain("API key", msg);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData((HttpStatusCode)529)]                         // Anthropic "overloaded"
    public void Server_side_statuses_map_to_the_unavailable_row(HttpStatusCode status)
    {
        var ex = new SynthesizedProviderException((int)status, "server error");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "anthropic", "claude-sonnet-4-6", null, 30);

        Assert.Contains("temporarily unavailable", msg);
        Assert.Contains(((int)status).ToString(), msg);
    }

    [Fact]
    public void An_answered_HttpRequestException_never_reads_as_could_not_reach()
    {
        // A status code means the server answered — the endpoint was reachable.
        var answered503 = new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable);
        var answered400 = new HttpRequestException("Bad Request", null, HttpStatusCode.BadRequest);

        var msg503 = AiProviderTestHandler.MapFailureMessage(
            answered503, "custom", "my-model", "https://llm.example/v1", 30);
        var msg400 = AiProviderTestHandler.MapFailureMessage(
            answered400, "custom", "my-model", "https://llm.example/v1", 30);

        Assert.DoesNotContain("Could not reach", msg503);
        Assert.Contains("temporarily unavailable", msg503);
        Assert.DoesNotContain("Could not reach", msg400);
        Assert.Contains("HTTP 400", msg400);
    }

    [Fact]
    public void Rejected_key_without_a_known_endpoint_does_not_print_empty_quotes()
    {
        var ex = new SynthesizedProviderException(401, "unauthorized");

        var msg = AiProviderTestHandler.MapFailureMessage(ex, "openai", "gpt-4o", null, 30);

        Assert.Contains("API key", msg);
        Assert.Contains("401", msg);
        Assert.DoesNotContain("('')", msg);
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
