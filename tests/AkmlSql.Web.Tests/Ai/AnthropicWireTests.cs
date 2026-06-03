using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 028 (M6) task T025 (US3). The native Anthropic wire: request body (system at top
/// level, required max_tokens), the browser-direct auth headers, and the named-event SSE
/// parser. Verified to clear CORS by the plan-time live fetch test; these lock the contract.
/// </summary>
public sealed class AnthropicWireTests
{
    [Fact]
    public void BuildBody_PutsSystemAtTopLevelAndRequiresMaxTokens()
    {
        var config = new AiProviderConfig { ProviderId = "anthropic", Model = "claude-sonnet-4-6" };
        var request = new AiChatRequest { SystemPrompt = "sys-prompt", UserPrompt = "hello" };

        var json = JsonSerializer.Serialize(AnthropicWire.BuildBody(config, request, stream: false));

        Assert.Contains("\"system\":\"sys-prompt\"", json);
        Assert.Contains("\"max_tokens\":4096", json);   // defaulted (required by Anthropic)
        Assert.Contains("\"content\":\"hello\"", json);
        Assert.DoesNotContain("\"role\":\"system\"", json); // system is NOT a message turn
        // Unset optionals are OMITTED, never serialized as explicit null (Anthropic 400s on null).
        Assert.DoesNotContain("temperature", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void BuildBody_OmitsTemperatureNull_ButKeepsSetValues()
    {
        var config = new AiProviderConfig { ProviderId = "anthropic", Model = "claude" };
        var request = new AiChatRequest { SystemPrompt = "s", UserPrompt = "u", Temperature = 0.2, MaxTokens = 150 };
        var json = JsonSerializer.Serialize(AnthropicWire.BuildBody(config, request, stream: true));

        Assert.Contains("\"temperature\":0.2", json);
        Assert.Contains("\"max_tokens\":150", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void ApplyAuth_SetsBrowserDirectHeaders()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        AnthropicWire.ApplyAuth(req, "sk-ant-test");

        Assert.Equal("sk-ant-test", req.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", req.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("true", req.Headers.GetValues("anthropic-dangerous-direct-browser-access").Single());
    }

    [Fact]
    public void ExtractText_ConcatenatesTextBlocks()
    {
        const string body = "{\"content\":[{\"type\":\"text\",\"text\":\"Hello \"},{\"type\":\"text\",\"text\":\"world\"}]}";
        Assert.Equal("Hello world", AnthropicWire.ExtractText(body));
    }

    [Fact]
    public async Task ParseSse_YieldsTextDeltasAndStopsOnMessageStop()
    {
        const string sse =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"id\":\"x\"}}\n" +
            "\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}\n" +
            "\n" +
            "event: ping\n" +
            "data: {\"type\":\"ping\"}\n" +
            "\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\" there\"}}\n" +
            "\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n" +
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"AFTER-STOP\"}}\n";

        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(sse)));
        var tokens = new List<string>();
        await foreach (var t in AnthropicWire.ParseSse(reader, CancellationToken.None)) tokens.Add(t);

        Assert.Equal(new[] { "Hello", " there" }, tokens); // nothing after message_stop
    }
}
