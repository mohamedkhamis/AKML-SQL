using System;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AkmlSql.Web.Services;

/// <summary>How an AI answer stream failed after the provider accepted the request (review WEB-18).</summary>
public enum AiStreamFailure
{
    /// <summary>The provider sent an error event in the stream (overloaded, quota, content policy…).</summary>
    ProviderError,

    /// <summary>The connection ended before the provider said the answer was complete.</summary>
    Truncated,

    /// <summary>The provider sent data that is not the event format it promised.</summary>
    Malformed,
}

/// <summary>
/// Review WEB-18: an answer stream that did not finish properly. The partial text already shown stays; the message says the
/// answer is incomplete and why, in words that carry no part of the provider's response body (WEB-21).
/// </summary>
public sealed class AiStreamException : Exception
{
    public AiStreamFailure Failure { get; }

    /// <summary>The provider's short error code (<c>overloaded_error</c>, <c>rate_limit_exceeded</c>), when it gave one.</summary>
    public string? Code { get; }

    public AiStreamException(AiStreamFailure failure, string? code = null)
        : base(MessageFor(failure, code))
    {
        Failure = failure;
        Code = code;
    }

    private static string MessageFor(AiStreamFailure failure, string? code) => failure switch
    {
        AiStreamFailure.ProviderError => "The provider stopped the answer with an error" + (code != null ? $" ({code})" : "") + ".",
        AiStreamFailure.Truncated => "The connection to the provider ended before the answer was complete.",
        _ => "The provider sent a reply this app could not read.",
    };
}

/// <summary>
/// Review WEB-21: provider failures in words a user can act on, and nothing else. A provider's (or a proxy's) error body may
/// echo the prompt, the schema or a key; it is never put in an exception message, shown, logged or persisted. Only a short
/// error code from it is kept, and only when it looks like one.
/// </summary>
public static class AiErrorText
{
    private static readonly Regex CodeShape = new("^[A-Za-z0-9_.\\-]{1,64}$", RegexOptions.CultureInvariant);

    /// <summary>Google's reason for a key it rejects. It answers with HTTP 400, not 401 / 403, so the status alone would not say so.</summary>
    public const string GoogleRejectedKey = "API_KEY_INVALID";

    /// <summary>The message for an HTTP failure from <paramref name="providerId"/>.</summary>
    public static string ForStatus(string providerId, HttpStatusCode status, string? code)
    {
        int n = (int)status;
        string suffix = code != null ? $", {code}" : string.Empty;
        string what = n switch
        {
            _ when string.Equals(code, GoogleRejectedKey, StringComparison.Ordinal) => "rejected the API key. Check the key in Settings › AI",
            401 or 403 => "rejected the API key. Check the key in Settings › AI",
            404 => "does not know the endpoint or the model. Check them in Settings › AI",
            408 or 504 => "did not answer in time. Try again",
            429 => "is rate-limiting or out of quota. Try again later, or check the plan",
            >= 500 => "is unavailable at the moment. Try again later",
            _ => "refused the request",
        };
        return $"{providerId} {what} (HTTP {n}{suffix}).";
    }

    /// <summary>
    /// The error code in a provider's JSON error body — OpenAI-style <c>{"error":{"code"|"type"|"status"}}</c>, Anthropic's
    /// <c>{"type":"error","error":{"type"}}</c> — when it is a short identifier; null otherwise. The rest is discarded.
    /// </summary>
    public static string? ExtractCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];   // Gemini wraps it
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                // Google says why in details[].reason (API_KEY_INVALID for a bad key); that is the code that tells the user what to do.
                if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("reason", out var reason)
                            && reason.ValueKind == JsonValueKind.String && Shaped(reason) is { } why)
                            return why;
                    }
                }
                foreach (var name in new[] { "code", "type", "status" })
                {
                    // A numeric code only repeats the HTTP status ("HTTP 400, 400"); a named one says something.
                    if (error.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Number && Shaped(v) is { } code) return code;
                }
                if (error.TryGetProperty("code", out var number) && Shaped(number) is { } numeric) return numeric;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Shaped(JsonElement v)
    {
        var text = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return text != null && CodeShape.IsMatch(text) ? text : null;
    }
}

/// <summary>
/// Review WEB-21: the last line of defence before a message enters the diagnostics ring (which is persisted to IndexedDB and
/// exported). Anything shaped like a credential is masked and the message is cut to a bounded length, so an error text that
/// quoted a key, a header or a long block of a response cannot be kept or exported in full.
/// </summary>
public static class DiagnosticRedaction
{
    /// <summary>The longest message the ring keeps.</summary>
    public const int MaxLength = 1000;

    private const string Mask = "[redacted]";

    private static readonly Regex[] Secrets =
    {
        // Authorization: Bearer xxx / Basic xxx
        new(@"(?i)\b(bearer|basic)\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.CultureInvariant),
        // key=value pairs whose name says secret (connection strings, query strings, headers, JSON)
        new(@"(?i)(""?\b(?:api[_-]?key|x-api-key|password|pwd|secret|token|access[_-]?token|authorization)\b""?\s*[:=]\s*)(""[^""]*""|'[^']*'|[^\s;,&""']+)", RegexOptions.CultureInvariant),
        // provider key prefixes (sk-..., sk-ant-..., AIza...)
        new(@"\b(?:sk-(?:ant-)?[A-Za-z0-9_\-]{16,}|AIza[0-9A-Za-z_\-]{30,})", RegexOptions.CultureInvariant),
        // any other long opaque token: 32+ characters of base64/hex alphabet holding both letters and digits
        new(@"\b(?=[A-Za-z0-9+/_\-]*[0-9])(?=[A-Za-z0-9+/_\-]*[A-Za-z])[A-Za-z0-9+/_\-]{32,}={0,2}", RegexOptions.CultureInvariant),
    };

    public static string Clean(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message ?? string.Empty;
        var text = message;
        text = Secrets[0].Replace(text, m => m.Groups[1].Value + " " + Mask);
        text = Secrets[1].Replace(text, m => m.Groups[1].Value + Mask);
        text = Secrets[2].Replace(text, Mask);
        text = Secrets[3].Replace(text, Mask);
        return text.Length <= MaxLength ? text : text.Substring(0, MaxLength) + "… (cut)";
    }
}
