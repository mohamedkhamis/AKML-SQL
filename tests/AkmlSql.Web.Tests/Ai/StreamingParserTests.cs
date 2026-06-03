using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 028 (M6) task T019 (US2). The OpenAI-compatible SSE delta parser (shared by
/// OpenAI / Gemini / Ollama / LM Studio): yields delta.content tokens, ignores role-only and
/// finish chunks, terminates at <c>data: [DONE]</c>, and honours cancellation.
/// </summary>
public sealed class StreamingParserTests
{
    private static StreamReader Reader(string s) => new(new MemoryStream(Encoding.UTF8.GetBytes(s)));

    private static async Task<List<string>> Collect(IAsyncEnumerable<string> src, CancellationToken ct = default)
    {
        var list = new List<string>();
        await foreach (var x in src.WithCancellation(ct)) list.Add(x);
        return list;
    }

    [Fact]
    public async Task OpenAiSse_YieldsContentDeltasAndStopsAtDone()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"\"}}]}\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n" +
            "data: [DONE]\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"AFTER-DONE\"}}]}\n";

        var tokens = await Collect(OpenAiWire.ParseSse(Reader(sse), CancellationToken.None));

        Assert.Equal(new[] { "Hello", " world" }, tokens); // role-only/empty/finish skipped; nothing after [DONE]
    }

    [Fact]
    public async Task OpenAiSse_SkipsMalformedChunks()
    {
        const string sse =
            "data: not-json\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n" +
            "data: [DONE]\n";

        var tokens = await Collect(OpenAiWire.ParseSse(Reader(sse), CancellationToken.None));
        Assert.Equal(new[] { "ok" }, tokens);
    }

    [Fact]
    public void OpenAi_ExtractText_ReadsChoiceMessageContent()
    {
        const string body = "{\"choices\":[{\"message\":{\"content\":\"buffered answer\"}}]}";
        Assert.Equal("buffered answer", OpenAiWire.ExtractText(body));
    }

    [Fact]
    public void OpenAi_ApplyAuth_SetsBearer()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        OpenAiWire.ApplyAuth(req, "sk-test");
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", req.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ParseSse_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        const string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}]}\n";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Collect(OpenAiWire.ParseSse(Reader(sse), cts.Token), cts.Token));
    }
}
