using Microsoft.ML.Tokenizers;
using Serilog;

namespace AkmlSql.Engine.Ai.Context;

/// <summary>
/// Estimates token counts for text using the <c>cl100k_base</c> tokenizer from
/// <c>Microsoft.ML.Tokenizers</c>. Used to fit schema context within AI provider token budgets.
/// <para>
/// For OpenAI/Azure models, the count is exact (cl100k_base encoding).
/// For other providers, a 1.1x conservative multiplier is applied.
/// </para>
/// The tokenizer instance is cached via <see cref="Lazy{T}"/> (thread-safe, expensive to create).
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// Lazy-initialized tokenizer instance. Thread-safe and reusable.
    /// Uses cl100k_base encoding (GPT-4, GPT-4o family).
    /// </summary>
    private static readonly Lazy<Tokenizer?> LazyTokenizer = new(CreateTokenizer, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Conservative multiplier applied to token counts for non-OpenAI models.
    /// Different tokenizers may produce slightly different token counts.
    /// </summary>
    private const double NonOpenAiMultiplier = 1.1;

    /// <summary>
    /// Fallback ratio when the tokenizer is unavailable: ~4 characters per token.
    /// </summary>
    private const double FallbackCharsPerToken = 4.0;

    /// <summary>
    /// Estimates the token count for the given text using the cl100k_base tokenizer.
    /// Falls back to a heuristic (text.Length / 4) if the tokenizer is unavailable.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <returns>Estimated token count.</returns>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var tokenizer = LazyTokenizer.Value;
        if (tokenizer != null)
        {
            return tokenizer.CountTokens(text);
        }

        // Fallback heuristic
        return (int)Math.Ceiling(text.Length / FallbackCharsPerToken);
    }

    /// <summary>
    /// Estimates the token count for the given text, adjusted for the target AI provider.
    /// For "openai" and "azure" providers, the exact cl100k_base count is returned.
    /// For all other providers, a 1.1x conservative multiplier is applied.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <param name="provider">
    /// The AI provider name (case-insensitive). Recognized values: "openai", "azure".
    /// </param>
    /// <returns>Estimated token count adjusted for the provider.</returns>
    public static int EstimateTokensForModel(string text, string provider)
    {
        var baseCount = EstimateTokens(text);

        if (string.IsNullOrEmpty(provider))
        {
            return (int)Math.Ceiling(baseCount * NonOpenAiMultiplier);
        }

        var p = provider.ToLowerInvariant();
        if (p is "openai" or "azure")
        {
            return baseCount;
        }

        return (int)Math.Ceiling(baseCount * NonOpenAiMultiplier);
    }

    /// <summary>
    /// Creates the tokenizer instance. Returns null if creation fails (e.g. missing data package).
    /// </summary>
    private static Tokenizer? CreateTokenizer()
    {
        try
        {
            var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            Log.Debug("TokenEstimator: initialized TiktokenTokenizer for gpt-4o");
            return tokenizer;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TokenEstimator: failed to create TiktokenTokenizer, falling back to heuristic");
            return null;
        }
    }
}
