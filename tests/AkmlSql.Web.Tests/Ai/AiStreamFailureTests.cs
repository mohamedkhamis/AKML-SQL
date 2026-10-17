using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Review findings WEB-18 and WEB-21. An answer stream is complete only when the provider says so: an error event, data that
/// is not JSON, or a connection that ends first is a typed failure, not a finished answer (the text that arrived stays with the
/// caller). And a provider's error body — which may echo the prompt or a key — never reaches an exception message, the
/// screen or the persisted diagnostics; synthetic markers stand in for real secrets.
/// </summary>
public sealed class AiStreamFailureTests
{
    private const string Marker = "SYNTHETIC-PROMPT-MARKER-7f3a";
    private const string FakeKey = "sk-ant-api03-SYNTHETICKEYxxxxxxxxxxxxxxxxxxxxxxxx";

    private static StreamReader Reader(string s) => new(new MemoryStream(Encoding.UTF8.GetBytes(s)));

    private static async Task<(List<string> Tokens, AiStreamException? Error)> Drain(IAsyncEnumerable<string> stream)
    {
        var tokens = new List<string>();
        try
        {
            await foreach (var t in stream) tokens.Add(t);
            return (tokens, null);
        }
        catch (AiStreamException ex)
        {
            return (tokens, ex);
        }
    }

    // ── OpenAI-compatible wire ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAi_a_finish_reason_completes_the_answer_even_without_DONE()
    {
        var (tokens, error) = await Drain(OpenAiWire.ParseSse(Reader(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n"), CancellationToken.None));

        Assert.Null(error);
        Assert.Equal(new[] { "Hi" }, tokens);
    }

    [Fact]
    public async Task OpenAi_an_error_event_mid_stream_is_a_provider_error_with_its_code()
    {
        var (tokens, error) = await Drain(OpenAiWire.ParseSse(Reader(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Partial\"}}]}\n" +
            $"data: {{\"error\":{{\"message\":\"{Marker}\",\"code\":\"rate_limit_exceeded\"}}}}\n"), CancellationToken.None));

        Assert.Equal(new[] { "Partial" }, tokens);
        Assert.Equal(AiStreamFailure.ProviderError, error!.Failure);
        Assert.Equal("rate_limit_exceeded", error.Code);
        Assert.DoesNotContain(Marker, error.Message);
    }

    [Fact]
    public async Task OpenAi_a_connection_that_ends_before_the_end_is_truncated()
    {
        var (tokens, error) = await Drain(OpenAiWire.ParseSse(Reader(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Half an ans\"}}]}\n"), CancellationToken.None));

        Assert.Equal(new[] { "Half an ans" }, tokens);
        Assert.Equal(AiStreamFailure.Truncated, error!.Failure);
    }

    [Fact]
    public async Task OpenAi_cancellation_is_cancellation_not_a_stream_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in OpenAiWire.ParseSse(Reader("data: [DONE]\n"), cts.Token)) { }
        });
    }

    // ── Anthropic wire ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anthropic_an_error_event_is_a_provider_error_with_its_type()
    {
        var (tokens, error) = await Drain(AnthropicWire.ParseSse(Reader(
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Part\"}}\n" +
            "event: error\n" +
            $"data: {{\"type\":\"error\",\"error\":{{\"type\":\"overloaded_error\",\"message\":\"{Marker}\"}}}}\n"), CancellationToken.None));

        Assert.Equal(new[] { "Part" }, tokens);
        Assert.Equal(AiStreamFailure.ProviderError, error!.Failure);
        Assert.Equal("overloaded_error", error.Code);
        Assert.DoesNotContain(Marker, error.Message);
    }

    [Fact]
    public async Task Anthropic_without_message_stop_is_truncated_and_malformed_data_is_malformed()
    {
        var (_, truncated) = await Drain(AnthropicWire.ParseSse(Reader(
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Part\"}}\n"), CancellationToken.None));
        Assert.Equal(AiStreamFailure.Truncated, truncated!.Failure);

        var (_, malformed) = await Drain(AnthropicWire.ParseSse(Reader("data: {not json\n"), CancellationToken.None));
        Assert.Equal(AiStreamFailure.Malformed, malformed!.Failure);
    }

    // ── HTTP failures (WEB-21) ───────────────────────────────────────────────────────────────────

    private sealed class FailingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public FailingHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
    }

    private static async Task<(IAiClientFactory Client, DiagnosticsRingBuffer Diagnostics)> ClientAsync(HttpStatusCode status, string body)
    {
        var vault = new AiKeyVault(new InMemoryIndexedDbAdapter(), new InMemoryWebCryptoWrapper());
        await vault.SetKeyAsync("anthropic", new AiProviderConfig { ProviderId = "anthropic", Model = "m" }, FakeKey);
        var diagnostics = new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter());
        return (new AiClientFactory(vault, new HttpClient(new FailingHandler(status, body)), diagnostics), diagnostics);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the API key")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limiting")]
    [InlineData(HttpStatusCode.NotFound, "endpoint or the model")]
    [InlineData(HttpStatusCode.BadGateway, "unavailable")]
    public async Task A_failing_response_says_what_to_do_without_its_body(HttpStatusCode status, string advice)
    {
        var body = $"{{\"type\":\"error\",\"error\":{{\"type\":\"authentication_error\",\"message\":\"{Marker} key={FakeKey}\"}}}}";
        var (client, diagnostics) = await ClientAsync(status, body);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SendAsync("anthropic", new AiChatRequest { UserPrompt = "q" }, CancellationToken.None));

        Assert.Contains(advice, ex.Message);
        Assert.Contains("authentication_error", ex.Message);   // the provider's code is kept…
        Assert.DoesNotContain(Marker, ex.Message);             // …its words are not
        Assert.DoesNotContain(FakeKey, ex.Message);
        Assert.All(diagnostics.Snapshot(), e => Assert.DoesNotContain(Marker, e.Message));
    }

    [Fact]
    public async Task The_streaming_path_keeps_the_body_out_too()
    {
        var (client, diagnostics) = await ClientAsync(HttpStatusCode.BadRequest, Marker);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in client.StreamAsync("anthropic", new AiChatRequest { UserPrompt = "q" }, CancellationToken.None)) { }
        });

        Assert.DoesNotContain(Marker, ex.Message);
        Assert.All(diagnostics.Snapshot(), e => Assert.DoesNotContain(Marker, e.Message));
    }

    // ── The diagnostics boundary (WEB-21) ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwx", "abcdefghijklmnopqrstuvwx")]
    [InlineData("x-api-key=" + FakeKey, FakeKey)]
    [InlineData("Server=db;User ID=u;Password=hunter2-synthetic;", "hunter2-synthetic")]
    [InlineData("failed with token 9f8e7d6c5b4a39281706f5e4d3c2b1a0ffeeddcc", "9f8e7d6c5b4a39281706f5e4d3c2b1a0ffeeddcc")]
    public void The_diagnostics_ring_masks_credential_shaped_text(string message, string secret)
    {
        var ring = new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter());
        ring.Log(DiagnosticLevel.Error, "ai", message);

        var kept = Assert.Single(ring.Snapshot()).Message;
        Assert.DoesNotContain(secret, kept);
        Assert.Contains("[redacted]", kept);
    }

    [Fact]
    public void The_diagnostics_ring_bounds_a_message()
    {
        var ring = new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter());
        ring.Log(DiagnosticLevel.Error, "execute", new string('x', 10_000));

        Assert.True(Assert.Single(ring.Snapshot()).Message.Length <= DiagnosticRedaction.MaxLength + 10);
    }

    [Fact]
    public void Ordinary_messages_are_kept_as_they_are()
    {
        const string message = "Connected to engine 1.26.1017.1200 with 9 capability(s).";
        Assert.Equal(message, DiagnosticRedaction.Clean(message));
    }
}
