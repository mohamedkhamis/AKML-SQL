using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T128; extended by spec 028 (M6) tasks T005/T014/T020.
/// Creates the per-provider request, enforces the origin allow-list, and talks to the
/// provider's REST endpoint directly from the browser (no AKML host in the path).
///
/// <para>
/// Each provider is described by a wire "profile" — a request-body shape, an auth scheme, and
/// an SSE delta parser. The OpenAI-compatible shape covers OpenAI / Azure / Gemini / Ollama /
/// LM Studio; Anthropic uses its native Messages contract. Only providers whose APIs permit
/// cross-origin browser calls are reachable (verified by a live fetch test): Anthropic,
/// Gemini, Ollama, LM Studio. OpenAI and Azure are CORS-blocked and are surfaced as
/// not-available-browser-direct (FR-013 / Reconciliation 3).
/// </para>
/// </summary>
public interface IAiClientFactory
{
    /// <summary>Buffered single-shot chat completion. Returns the model's full text.</summary>
    Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct);

    /// <summary>Streamed chat completion. Yields text deltas as they arrive (typewriter).</summary>
    IAsyncEnumerable<string> StreamAsync(string providerId, AiChatRequest request, CancellationToken ct);

    /// <summary>True iff <paramref name="origin"/> is in the per-provider allow-list.</summary>
    bool IsOriginAllowed(string providerId, string origin);

    /// <summary>True iff the provider's API permits a browser-direct (cross-origin) call.</summary>
    bool IsBrowserDirectCapable(string providerId);
}

public sealed class AiChatRequest
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }

    /// <summary>
    /// Optional multi-turn history -- ordered oldest-first. When present the provider sees:
    /// <c>System -&gt; History[0..n] -&gt; UserPrompt</c>. Empty for single-shot calls.
    /// </summary>
    public AiChatMessage[] History { get; set; } = Array.Empty<AiChatMessage>();
}

public sealed class AiChatMessage
{
    public string Role { get; set; } = "user";   // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Thrown when an AI call would target an origin not on the per-provider allow-list.
/// </summary>
public sealed class UnauthorizedOriginException : Exception
{
    public string ProviderId { get; }
    public string AttemptedOrigin { get; }

    public UnauthorizedOriginException(string providerId, string attemptedOrigin)
        : base($"AI provider '{providerId}' is not allowed to fetch '{attemptedOrigin}'. " +
               "The browser refuses the request before it hits the network.")
    {
        ProviderId = providerId;
        AttemptedOrigin = attemptedOrigin;
    }
}

/// <summary>
/// Thrown when a provider cannot be reached directly from the browser because its API does
/// not return CORS headers (OpenAI, Azure OpenAI). Surfaced as an explanatory notice — never
/// routed through any AKML host or engine relay (PRD §10).
/// </summary>
public sealed class ProviderNotBrowserDirectException : Exception
{
    public string ProviderId { get; }

    public ProviderNotBrowserDirectException(string providerId)
        : base($"'{providerId}' can't be used directly from the browser — its API doesn't permit " +
               "cross-origin browser calls (CORS). Use the desktop edition, or point it at an " +
               "OpenAI-compatible endpoint (e.g. a local proxy).")
    {
        ProviderId = providerId;
    }
}

internal sealed class AiClientFactory : IAiClientFactory
{
    /// <summary>
    /// Hard-coded origin allow-list. Patterns suffixed with <c>*</c> match by prefix (Azure,
    /// where the subdomain is user-supplied).
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowList = new(StringComparer.Ordinal)
    {
        ["openai"]   = new[] { "https://api.openai.com" },
        ["anthropic"] = new[] { "https://api.anthropic.com" },
        ["gemini"]   = new[] { "https://generativelanguage.googleapis.com" },
        ["azure"]    = new[] { "https://*.openai.azure.com" },
        ["ollama"]   = new[] { "http://localhost:11434", "http://127.0.0.1:11434" },
        ["lmstudio"] = new[] { "http://localhost:1234", "http://127.0.0.1:1234" },
    };

    /// <summary>
    /// Providers whose APIs permit a browser-direct (cross-origin) call — verified by a live
    /// cross-origin fetch test (research Decision 3). OpenAI and Azure are deliberately absent:
    /// they send no <c>Access-Control-Allow-Origin</c>, so a browser fetch can never reach them.
    /// </summary>
    private static readonly HashSet<string> BrowserDirect = new(StringComparer.Ordinal)
    {
        "anthropic", "gemini", "ollama", "lmstudio",
    };

    private readonly IAiKeyVault _vault;
    private readonly HttpClient _http;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    public AiClientFactory(IAiKeyVault vault, HttpClient http, IDiagnosticsRingBuffer diagnostics)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public bool IsBrowserDirectCapable(string providerId) => BrowserDirect.Contains(providerId);

    public bool IsOriginAllowed(string providerId, string origin)
    {
        if (!AllowList.TryGetValue(providerId, out var allowed)) return false;
        foreach (var pattern in allowed)
        {
            if (pattern.Contains("*", StringComparison.Ordinal))
            {
                var prefix = pattern.Substring(0, pattern.IndexOf('*'));
                var suffix = pattern.Substring(pattern.IndexOf('*') + 1);
                if (origin.StartsWith(prefix, StringComparison.Ordinal) &&
                    origin.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (string.Equals(origin, pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var (config, endpointUrl) = await GateAsync(providerId).ConfigureAwait(false);
        using var req = await BuildRequestAsync(providerId, config, endpointUrl, request, stream: false).ConfigureAwait(false);

        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "ai",
                $"{providerId} returned {(int)response.StatusCode}: {response.ReasonPhrase}");
            throw new HttpRequestException(
                $"{providerId} returned {(int)response.StatusCode}: {response.ReasonPhrase}. {respBody}",
                inner: null,
                statusCode: response.StatusCode);
        }

        return IsAnthropic(providerId) ? AnthropicWire.ExtractText(respBody) : OpenAiWire.ExtractText(respBody);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string providerId, AiChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var (config, endpointUrl) = await GateAsync(providerId).ConfigureAwait(false);
        using var req = await BuildRequestAsync(providerId, config, endpointUrl, request, stream: true).ConfigureAwait(false);

        // ResponseHeadersRead is load-bearing: it lets SendAsync return before the body
        // completes so we can read the stream incrementally. On net10 browser response
        // streaming is on by default (no SetBrowserResponseStreamingEnabled call needed).
        using var response = await _http
            .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _diagnostics.Log(DiagnosticLevel.Warn, "ai",
                $"{providerId} (stream) returned {(int)response.StatusCode}: {response.ReasonPhrase}");
            throw new HttpRequestException(
                $"{providerId} returned {(int)response.StatusCode}: {response.ReasonPhrase}. {errBody}",
                inner: null,
                statusCode: response.StatusCode);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        var tokens = IsAnthropic(providerId)
            ? AnthropicWire.ParseSse(reader, ct)
            : OpenAiWire.ParseSse(reader, ct);

        await foreach (var token in tokens.ConfigureAwait(false))
        {
            yield return token;
        }
    }

    /// <summary>Browser-direct + origin gating shared by both call paths.</summary>
    private async Task<(AiProviderConfig Config, string EndpointUrl)> GateAsync(string providerId)
    {
        if (!IsBrowserDirectCapable(providerId))
        {
            _diagnostics.Log(DiagnosticLevel.Error, "ai",
                $"Refused AI call: '{providerId}' is not reachable browser-direct (CORS).");
            throw new ProviderNotBrowserDirectException(providerId);
        }

        var config = await _vault.GetConfigAsync(providerId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No AI provider config stored for '{providerId}'.");

        var endpointUrl = ResolveEndpoint(config);
        var origin = ExtractOrigin(endpointUrl);
        if (!IsOriginAllowed(providerId, origin))
        {
            _diagnostics.Log(DiagnosticLevel.Error, "ai",
                $"Refused AI call: '{providerId}' attempted '{origin}', not on allow-list.");
            throw new UnauthorizedOriginException(providerId, origin);
        }

        return (config, endpointUrl);
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(
        string providerId, AiProviderConfig config, string endpointUrl, AiChatRequest request, bool stream)
    {
        var body = IsAnthropic(providerId)
            ? AnthropicWire.BuildBody(config, request, stream)
            : OpenAiWire.BuildBody(config, request, stream);

        var req = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        // Local providers (Ollama / LM Studio) typically have no key (HasKey=false); skip auth
        // rather than calling UnwrapForCallAsync (which throws when there is no key).
        if (config.HasKey)
        {
            using var unwrapped = await _vault.UnwrapForCallAsync(providerId).ConfigureAwait(false);
            if (IsAnthropic(providerId))
            {
                AnthropicWire.ApplyAuth(req, unwrapped.Value);
            }
            else
            {
                OpenAiWire.ApplyAuth(req, unwrapped.Value);
            }
        }

        return req;
    }

    private static bool IsAnthropic(string providerId) => string.Equals(providerId, "anthropic", StringComparison.Ordinal);

    private static string ResolveEndpoint(AiProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.Endpoint)) return config.Endpoint;
        return config.ProviderId switch
        {
            "openai" => "https://api.openai.com/v1/chat/completions",
            "anthropic" => "https://api.anthropic.com/v1/messages",
            "gemini" => "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            "ollama" => "http://localhost:11434/v1/chat/completions",
            "lmstudio" => "http://localhost:1234/v1/chat/completions",
            _ => throw new InvalidOperationException(
                $"Provider '{config.ProviderId}' requires an explicit endpoint."),
        };
    }

    internal static string ExtractOrigin(string url)
    {
        var uri = new Uri(url);
        return $"{uri.Scheme}://{uri.Authority}";
    }
}

/// <summary>OpenAI-compatible wire (OpenAI / Azure / Gemini / Ollama / LM Studio).</summary>
internal static class OpenAiWire
{
    public static object BuildBody(AiProviderConfig config, AiChatRequest request, bool stream)
    {
        var messages = new List<object>(2 + request.History.Length)
        {
            new { role = "system", content = request.SystemPrompt },
        };
        foreach (var m in request.History) messages.Add(new { role = m.Role, content = m.Content });
        messages.Add(new { role = "user", content = request.UserPrompt });

        // Build with a dictionary so unset optional numerics are OMITTED, not serialized as
        // explicit JSON null — Anthropic and several OpenAI-compatible local servers (Ollama,
        // llama.cpp, LM Studio) reject `"temperature":null` / `"max_tokens":null` with a 4xx.
        var body = new Dictionary<string, object?>
        {
            ["model"] = config.Model,
            ["messages"] = messages.ToArray(),
            ["stream"] = stream,
        };
        if (request.MaxTokens.HasValue) body["max_tokens"] = request.MaxTokens.Value;
        if (request.Temperature.HasValue) body["temperature"] = request.Temperature.Value;
        return body;
    }

    public static void ApplyAuth(HttpRequestMessage req, string apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public static string ExtractText(string responseBody)
    {
        // { choices: [{ message: { content: "..." } }] }
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch (Exception)
        {
            return responseBody;
        }
    }

    /// <summary>Parse OpenAI SSE: <c>data: {json}</c> lines, <c>delta.content</c> tokens, <c>data: [DONE]</c>.</summary>
    public static async IAsyncEnumerable<string> ParseSse(
        System.IO.StreamReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line.Substring(5).Trim();
            if (data == "[DONE]") yield break;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    token = content.GetString();
                }
            }
            catch (JsonException) { /* skip malformed chunk */ }

            if (!string.IsNullOrEmpty(token)) yield return token!;
        }
    }
}

/// <summary>Anthropic native Messages wire (browser-direct via the dangerous-direct header).</summary>
internal static class AnthropicWire
{
    public static object BuildBody(AiProviderConfig config, AiChatRequest request, bool stream)
    {
        var messages = new List<object>(1 + request.History.Length);
        foreach (var m in request.History) messages.Add(new { role = m.Role, content = m.Content });
        messages.Add(new { role = "user", content = request.UserPrompt });

        // Dictionary so a null temperature is omitted (Anthropic rejects explicit null). system is
        // top-level; max_tokens is required.
        var body = new Dictionary<string, object?>
        {
            ["model"] = config.Model,
            ["system"] = request.SystemPrompt,
            ["messages"] = messages.ToArray(),
            ["max_tokens"] = request.MaxTokens ?? 4096,
            ["stream"] = stream,
        };
        if (request.Temperature.HasValue) body["temperature"] = request.Temperature.Value;
        return body;
    }

    public static void ApplyAuth(HttpRequestMessage req, string apiKey)
    {
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        // Opt in to browser-direct CORS (the official mechanism).
        req.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");
    }

    public static string ExtractText(string responseBody)
    {
        // { content: [ { type: "text", text: "..." }, ... ] }
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                        && block.TryGetProperty("text", out var txt))
                    {
                        sb.Append(txt.GetString());
                    }
                }
                return sb.ToString();
            }
        }
        catch (Exception) { }
        return responseBody;
    }

    /// <summary>Parse Anthropic SSE: <c>content_block_delta</c>/<c>text_delta</c> tokens; stop on <c>message_stop</c>.</summary>
    public static async IAsyncEnumerable<string> ParseSse(
        System.IO.StreamReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue; // ignore the "event:" lines
            var data = line.Substring(5).Trim();
            if (data.Length == 0) continue;

            string? token = null;
            var stop = false;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "message_stop")
                {
                    stop = true;
                }
                else if (type == "content_block_delta"
                    && doc.RootElement.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta"
                    && delta.TryGetProperty("text", out var txt))
                {
                    token = txt.GetString();
                }
            }
            catch (JsonException) { /* skip ping/malformed */ }

            if (stop) yield break;
            if (!string.IsNullOrEmpty(token)) yield return token!;
        }
    }
}
