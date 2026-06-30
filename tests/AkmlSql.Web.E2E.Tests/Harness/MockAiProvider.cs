using System.Net;
using System.Text;
using System.Text.Json;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Spec 028 (M6) task T038 — a local mock AI provider for the browser-AI E2E. Shaped as an
/// Ollama / OpenAI-compatible endpoint on <c>http://127.0.0.1:11434/v1/chat/completions</c> with
/// permissive CORS so the Blazor WASM app (origin <c>http://localhost:5000</c>) reaches it
/// browser-direct, with no real key and no real network. localhost→localhost with CORS is the only
/// browser-reachable mock path (cloud origins can't be intercepted from the browser).
///
/// <para>
/// Mirrors the wire contract the app emits (verified live 2026-06-03; see
/// <c>specs/028-m6-ai-browser-closure/SC-009-EVIDENCE/</c>):
/// request body <c>{ model, messages[], stream, [max_tokens], [temperature] }</c>; buffered
/// response <c>{ choices:[{ message:{ content } }] }</c>; streamed response OpenAI SSE
/// (<c>data: {choices:[{delta:{content}}]}</c> lines, <c>data: [DONE]</c> sentinel).
/// </para>
///
/// <para>
/// Every received chat-completions request body is recorded in <see cref="Captures"/> so a test
/// can assert per-privacy-mode schema disclosure on the wire (the T041 evidence, automated).
/// </para>
/// </summary>
public sealed class MockAiProvider : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<JsonElement> _captures = new();
    private readonly object _gate = new();
    private Task? _loop;

    /// <summary>OpenAI-style streamed tokens; concatenation is the full answer.</summary>
    public static readonly string[] StreamTokens =
        ["This ", "query ", "returns ", "rows ", "from ", "the ", "referenced ", "tables. ", "[MOCK-STREAM]"];

    /// <summary>Buffered (stream:false) content — used by Ghost Text (SendAsync).</summary>
    public const string BufferedContent = "Customers c ON o.CustomerId = c.CustomerId [MOCK]";

    public int Port { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public string ChatCompletionsUrl => $"{BaseUrl}/v1/chat/completions";

    private MockAiProvider(int port)
    {
        Port = port;
        // localhost prefixes don't require an admin urlacl reservation.
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    /// <summary>Start an Ollama-shaped mock on port 11434 (the app's default Ollama endpoint).</summary>
    public static MockAiProvider StartOllama(int port = 11434)
    {
        var mock = new MockAiProvider(port);
        mock._listener.Start();
        mock._loop = Task.Run(() => mock.AcceptLoopAsync(mock._cts.Token));
        return mock;
    }

    /// <summary>Snapshot of the request bodies received so far (chat-completions only).</summary>
    public IReadOnlyList<JsonElement> Captures
    {
        get { lock (_gate) return _captures.ToArray(); }
    }

    public void ResetCaptures()
    {
        lock (_gate) _captures.Clear();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) { return; } // listener stopped
            _ = Task.Run(() => HandleAsync(ctx), ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        // Permissive CORS so the browser's cross-origin fetch + preflight succeed.
        res.AddHeader("Access-Control-Allow-Origin", "*");
        res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.AddHeader("Access-Control-Allow-Headers", "*");

        try
        {
            if (req.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }

            if (req.HttpMethod == "POST" && req.Url!.AbsolutePath.StartsWith("/v1/chat/completions", StringComparison.Ordinal))
            {
                string body;
                using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);

                bool stream = false;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    lock (_gate) _captures.Add(doc.RootElement.Clone());
                    stream = doc.RootElement.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.True;
                }
                catch (JsonException) { /* record nothing, treat as buffered */ }

                if (stream) await WriteSseAsync(res).ConfigureAwait(false);
                else WriteBuffered(res);
                return;
            }

            res.StatusCode = 404;
            res.Close();
        }
        catch (Exception)
        {
            try { res.Abort(); } catch { /* ignore */ }
        }
    }

    private static void WriteBuffered(HttpListenerResponse res)
    {
        res.StatusCode = 200;
        res.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = BufferedContent } } },
        });
        var bytes = Encoding.UTF8.GetBytes(payload);
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.Close();
    }

    private static async Task WriteSseAsync(HttpListenerResponse res)
    {
        res.StatusCode = 200;
        res.ContentType = "text/event-stream";
        res.SendChunked = true;
        foreach (var token in StreamTokens)
        {
            var chunk = JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = token } } } });
            var bytes = Encoding.UTF8.GetBytes($"data: {chunk}\n\n");
            await res.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            await res.OutputStream.FlushAsync().ConfigureAwait(false);
        }
        var done = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
        await res.OutputStream.WriteAsync(done).ConfigureAwait(false);
        res.Close();
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _listener.Stop(); _listener.Close(); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
