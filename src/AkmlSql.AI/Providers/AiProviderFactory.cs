using System.ClientModel;
using AkmlSql.Core.Config;
using Anthropic.SDK;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using Serilog;

namespace AkmlSql.Engine.Ai.Providers;

/// <summary>
/// Factory that creates an <see cref="IChatClient"/> from <see cref="AiSettings"/> configuration.
/// <para>
/// Provider selection is driven by <see cref="AiSettings.Provider"/>. API keys are decrypted
/// via the pluggable <see cref="KeyDecryptor"/> hook before being passed to the underlying SDK.
/// Supports: Anthropic, OpenAI, Azure OpenAI, Google Gemini, Ollama, LM Studio, and custom
/// OpenAI-compatible endpoints.
/// </para>
/// </summary>
public static class AiProviderFactory
{
    /// <summary>
    /// Spec 021 T121: pluggable hook for decrypting/unwrapping the API key BEFORE it is
    /// passed to a provider SDK. The default is identity (assumes the caller has already
    /// supplied a plaintext key).
    /// <para>
    /// The engine sets this at startup to call
    /// <c>AkmlSql.Engine.Ai.Security.CredentialManager.Decrypt</c> (Windows DPAPI). The web
    /// edition's own <c>AiKeyVault</c> (M6, T125) unwraps via browser Web Crypto BEFORE
    /// calling the factory, so it leaves the hook at the default. This keeps this assembly
    /// free of any platform-specific crypto and consumable from WASM.
    /// </para>
    /// </summary>
    public static Func<string?, string> KeyDecryptor { get; set; } =
        encrypted => encrypted ?? string.Empty;

    /// <summary>
    /// Creates an <see cref="IChatClient"/> for the primary provider configured in <paramref name="settings"/>.
    /// </summary>
    /// <param name="settings">AI configuration from <c>config.json</c>.</param>
    /// <returns>A ready-to-use <see cref="IChatClient"/> instance. The caller is responsible for disposal.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when required configuration is missing (e.g., no API key for a cloud provider)
    /// or the provider name is unrecognised.
    /// </exception>
    public static IChatClient Create(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var provider = settings.Provider?.Trim() ?? string.Empty;
        var apiKey = DecryptApiKey(settings.ApiKey);

        Log.Debug("AiProviderFactory: creating client for provider={Provider}, model={Model}",
            provider, settings.Model);

        return provider.ToLowerInvariant() switch
        {
            "anthropic" => CreateAnthropicClient(apiKey, settings.Model),
            "openai" => CreateOpenAiClient(apiKey, settings.Model, null),
            "azure" => CreateAzureClient(apiKey, settings.Model, settings.Endpoint),
            "gemini" => CreateGeminiClient(apiKey, settings.Model),
            "ollama" => CreateOllamaClient(settings.Model, settings.Endpoint),
            "lmstudio" => CreateOpenAiClient(apiKey, settings.Model, settings.Endpoint),
            "custom" => CreateOpenAiClient(apiKey, settings.Model, settings.Endpoint),
            _ => throw new InvalidOperationException(
                $"Unknown AI provider: '{settings.Provider}'. " +
                "Supported providers: anthropic, openai, azure, gemini, ollama, lmstudio, custom.")
        };
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> from the offline/fallback provider settings
    /// (<see cref="AiSettings.OfflineProvider"/>, <see cref="AiSettings.OfflineModel"/>,
    /// <see cref="AiSettings.OfflineEndpoint"/>).
    /// </summary>
    /// <param name="settings">AI configuration from <c>config.json</c>.</param>
    /// <returns>A ready-to-use <see cref="IChatClient"/> for the offline provider.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="AiSettings.OfflineProvider"/> is not configured.
    /// </exception>
    public static IChatClient CreateFromFallback(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.OfflineProvider))
        {
            throw new InvalidOperationException(
                "No offline AI provider is configured. Set 'ai.offlineProvider' in config.json.");
        }

        var fallbackSettings = new AiSettings
        {
            Provider = settings.OfflineProvider,
            Model = settings.OfflineModel,
            Endpoint = settings.OfflineEndpoint,
            ApiKey = string.Empty, // Local providers typically don't require API keys
            MaxTokens = settings.MaxTokens,
            Temperature = settings.Temperature,
            Timeout = settings.Timeout
        };

        Log.Debug("AiProviderFactory: creating fallback client for provider={Provider}, model={Model}",
            fallbackSettings.Provider, fallbackSettings.Model);

        return Create(fallbackSettings);
    }

    /// <summary>
    /// Creates an Anthropic Claude client via the Anthropic.SDK.
    /// <para>
    /// <see cref="AnthropicClient.Messages"/> implements <see cref="IChatClient"/> directly.
    /// </para>
    /// </summary>
    private static IChatClient CreateAnthropicClient(string apiKey, string model)
    {
        RequireApiKey(apiKey, "Anthropic");
        RequireModel(model, "Anthropic");
        RequireModelFamily(model, "anthropic", "Anthropic");

        var client = new AnthropicClient(apiKey);
        // AnthropicClient.Messages implements IChatClient (Anthropic.SDK 5.10.0+)
        return client.Messages;
    }

    /// <summary>
    /// Creates an OpenAI-compatible client. Also used for LM Studio and custom endpoints.
    /// <para>
    /// When <paramref name="endpoint"/> is provided, the client connects to that URL instead of
    /// the default OpenAI API, enabling LM Studio, vLLM, and other OpenAI-compatible servers.
    /// </para>
    /// </summary>
    private static IChatClient CreateOpenAiClient(string apiKey, string model, string? endpoint)
    {
        RequireModel(model, "OpenAI");

        OpenAI.Chat.ChatClient chatClient;

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            // Custom endpoint (LM Studio, vLLM, text-generation-inference, etc.)
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint)
            };

            // Allow empty API key for local servers that don't require authentication
            var credential = new ApiKeyCredential(
                string.IsNullOrEmpty(apiKey) ? "no-key-required" : apiKey);

            chatClient = new OpenAI.Chat.ChatClient(model, credential, options);
        }
        else
        {
            // Standard OpenAI API. Only this branch is family-guarded: custom endpoints
            // (LM Studio, vLLM, proxies) legitimately serve foreign model ids.
            RequireApiKey(apiKey, "OpenAI");
            RequireModelFamily(model, "openai", "OpenAI");
            chatClient = new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey));
        }

        return chatClient.AsIChatClient();
    }

    /// <summary>
    /// Creates an Azure OpenAI client.
    /// <para>
    /// Azure OpenAI requires both an endpoint and an API key. The <paramref name="model"/>
    /// parameter corresponds to the Azure deployment name, not a model identifier.
    /// </para>
    /// </summary>
    private static IChatClient CreateAzureClient(string apiKey, string model, string? endpoint)
    {
        RequireApiKey(apiKey, "Azure OpenAI");
        RequireModel(model, "Azure OpenAI");

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "Azure OpenAI requires an endpoint URL. " +
                "Set 'ai.endpoint' to your Azure OpenAI resource URL (e.g., https://your-resource.openai.azure.com/).");
        }

        // Azure.AI.OpenAI is a transitive dependency via Microsoft.Extensions.AI.OpenAI.
        // Use the OpenAI SDK with Azure endpoint configuration.
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint)
        };

        var credential = new ApiKeyCredential(apiKey);
        var chatClient = new OpenAI.Chat.ChatClient(model, credential, options);
        return chatClient.AsIChatClient();
    }

    /// <summary>
    /// Creates a Google Gemini client via the <see cref="GeminiChatClientAdapter"/>.
    /// </summary>
    private static IChatClient CreateGeminiClient(string apiKey, string model)
    {
        RequireApiKey(apiKey, "Gemini");
        RequireModel(model, "Gemini");
        RequireModelFamily(model, "gemini", "Gemini");

        return new GeminiChatClientAdapter(apiKey, model);
    }

    /// <summary>
    /// Creates an Ollama client.
    /// <para>
    /// <see cref="OllamaApiClient"/> implements <see cref="IChatClient"/> directly.
    /// Defaults to <c>http://localhost:11434</c> when no endpoint is configured.
    /// </para>
    /// </summary>
    private static IChatClient CreateOllamaClient(string model, string? endpoint)
    {
        RequireModel(model, "Ollama");

        var uri = string.IsNullOrWhiteSpace(endpoint)
            ? new Uri("http://localhost:11434")
            : new Uri(endpoint);

        // OllamaApiClient implements IChatClient directly (OllamaSharp 5.x)
        return new OllamaApiClient(uri, model);
    }

    /// <summary>
    /// Decrypts/unwraps an API key via the <see cref="KeyDecryptor"/> hook.
    /// Returns an empty string for null/empty input (supports local providers that need no key).
    /// </summary>
    private static string DecryptApiKey(string? encryptedKey)
    {
        if (string.IsNullOrEmpty(encryptedKey))
            return string.Empty;

        return KeyDecryptor(encryptedKey);
    }

    /// <summary>
    /// Validates that an API key is present for cloud providers that require one.
    /// </summary>
    private static void RequireApiKey(string apiKey, string providerName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"{providerName} requires an API key. Set 'ai.apiKey' in config.json or the AKML SQL settings dialog.");
        }
    }

    /// <summary>
    /// Refuses an obvious cross-provider model mismatch for the first-party clouds — e.g.
    /// provider=Gemini with model=claude-sonnet-5 previously went to Google's API verbatim and
    /// the user saw Google's raw 404 JSON in the chat panel. Unrecognised model names pass
    /// (local models, fine-tunes); custom/local providers never call this.
    /// </summary>
    private static void RequireModelFamily(string model, string expectedFamily, string providerName)
    {
        var family = AiModelFamily.Detect(model);
        if (family != null && family != expectedFamily)
        {
            throw new InvalidOperationException(
                $"Model '{model}' is a {FamilyDisplayName(family)} model, but the AI provider is set to '{providerName}'. " +
                $"Use a {providerName} model (e.g. \"{AiModelFamily.DefaultModelFor(expectedFamily)}\") " +
                "or change the provider in Options → AI Assistance.");
        }
    }

    private static string FamilyDisplayName(string family) => family switch
    {
        "anthropic" => "Anthropic (Claude)",
        "openai" => "OpenAI (GPT)",
        "gemini" => "Google (Gemini)",
        _ => family,
    };

    /// <summary>
    /// Validates that a model name is specified.
    /// </summary>
    private static void RequireModel(string model, string providerName)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                $"{providerName} requires a model name. Set 'ai.model' in config.json " +
                $"(e.g., \"gpt-4o\", \"claude-sonnet-4-20250514\", \"gemini-2.0-flash\").");
        }
    }
}
