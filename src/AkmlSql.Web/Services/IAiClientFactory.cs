using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T128. Creates the per-provider HTTP client and
/// enforces the origin allow-list per contracts/ai-key-wrapping.md "Provider-call
/// contract". A fetch to any non-allow-listed origin is refused at this layer with
/// <see cref="UnauthorizedOriginException"/>, before the request hits the network.
///
/// <para>
/// The web edition does NOT pull in the heavy provider SDKs (Anthropic.SDK / OpenAI /
/// OllamaSharp) -- the bundle would explode. Instead it issues direct fetches to the
/// provider's REST endpoint via an HttpClient. The supported wire format is the
/// OpenAI-compatible <c>POST /v1/chat/completions</c> shape, which OpenAI itself, LM
/// Studio, vLLM, Ollama (via <c>/v1</c>), and many self-hosted servers expose. The
/// Anthropic and Gemini wire formats are different and live as TODO follow-ups.
/// </para>
/// </summary>
public interface IAiClientFactory
{
    /// <summary>
    /// Send a single chat-completion request to <paramref name="providerId"/> and
    /// return the model's text response. The factory unwraps the API key via
    /// <see cref="IAiKeyVault.UnwrapForCallAsync"/>, the plaintext lives in a using
    /// scope only.
    /// </summary>
    Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct);

    /// <summary>True iff <paramref name="origin"/> is in the per-provider allow-list.</summary>
    bool IsOriginAllowed(string providerId, string origin);
}

public sealed class AiChatRequest
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }

    /// <summary>
    /// Optional multi-turn history -- ordered oldest-first. When present the
    /// provider sees: <c>System -&gt; History[0..n] -&gt; UserPrompt</c>. Used by
    /// the AiChatPanel for conversational mode. Empty for single-shot Explain /
    /// Fix / Optimize / TextToSql calls.
    /// </summary>
    public AiChatMessage[] History { get; set; } = Array.Empty<AiChatMessage>();
}

public sealed class AiChatMessage
{
    public string Role { get; set; } = "user";   // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Thrown when an AI call would target an origin not on the per-provider allow-list
/// per contracts/ai-key-wrapping.md.
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

internal sealed class AiClientFactory : IAiClientFactory
{
    /// <summary>
    /// Hard-coded origin allow-list. Patterns suffixed with <c>*</c> match by prefix
    /// (used for Azure, where the subdomain is user-supplied).
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

    private readonly IAiKeyVault _vault;
    private readonly HttpClient _http;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    public AiClientFactory(IAiKeyVault vault, HttpClient http, IDiagnosticsRingBuffer diagnostics)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public bool IsOriginAllowed(string providerId, string origin)
    {
        if (!AllowList.TryGetValue(providerId, out var allowed)) return false;
        foreach (var pattern in allowed)
        {
            if (pattern.Contains("*", StringComparison.Ordinal))
            {
                // Wildcard subdomain match -- "https://*.openai.azure.com" matches
                // "https://anything.openai.azure.com".
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

        using var unwrapped = await _vault.UnwrapForCallAsync(providerId).ConfigureAwait(false);
        var apiKey = unwrapped.Value;

        // Build the OpenAI-compatible chat-completion body. With History present we
        // interleave: system -> history (oldest first) -> userPrompt.
        var messages = new System.Collections.Generic.List<object>(2 + request.History.Length);
        messages.Add(new { role = "system", content = request.SystemPrompt });
        foreach (var m in request.History)
        {
            messages.Add(new { role = m.Role, content = m.Content });
        }
        messages.Add(new { role = "user", content = request.UserPrompt });

        var body = new
        {
            model = config.Model,
            messages = messages.ToArray(),
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
        };
        var json = JsonSerializer.Serialize(body);

        using var req = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        // Authentication varies by provider. For OpenAI / LM Studio / many local servers
        // it's "Authorization: Bearer <key>". Anthropic uses "x-api-key"; Gemini puts the
        // key in a query parameter. We default to the OpenAI shape -- Anthropic / Gemini
        // wire formats land in a follow-up.
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // FR-033 error mapping (lands fully in T136 -- here we surface the raw status).
            _diagnostics.Log(DiagnosticLevel.Warn, "ai",
                $"{providerId} returned {(int)response.StatusCode}: {response.ReasonPhrase}");
            throw new HttpRequestException(
                $"{providerId} returned {(int)response.StatusCode}: {response.ReasonPhrase}. {respBody}",
                inner: null,
                statusCode: response.StatusCode);
        }

        return ExtractTextResponse(respBody);
    }

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

    private static string ExtractTextResponse(string responseBody)
    {
        // OpenAI-compatible: { choices: [{ message: { content: "..." } }] }
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
            // Provider returned a non-OpenAI-shaped payload -- surface the raw text so
            // the user can copy + paste into the diagnostics bundle.
            return responseBody;
        }
    }
}
