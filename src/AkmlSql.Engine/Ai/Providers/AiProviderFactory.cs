#nullable enable
using System.ClientModel;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Ai.Security;
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
/// via <see cref="CredentialManager"/> before being passed to the underlying SDK.
/// Supports: Anthropic, OpenAI, Azure OpenAI, Google Gemini, Ollama, LM Studio, and custom
/// OpenAI-compatible endpoints.
/// </para>
/// </summary>
public static class AiProviderFactory
{
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
            // Standard OpenAI API
            RequireApiKey(apiKey, "OpenAI");
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
    /// Decrypts an API key using <see cref="CredentialManager"/>.
    /// Returns an empty string for null/empty input (supports local providers that need no key).
    /// </summary>
    private static string DecryptApiKey(string? encryptedKey)
    {
        if (string.IsNullOrEmpty(encryptedKey))
            return string.Empty;

        try
        {
            return CredentialManager.Decrypt(encryptedKey);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AiProviderFactory: failed to decrypt API key, using raw value");
            return encryptedKey;
        }
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
