using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Mscc.GenerativeAI;
using Serilog;
using GeminiContent = Mscc.GenerativeAI.Types.Content;
using GeminiFinishReason = Mscc.GenerativeAI.Types.FinishReason;
using GeminiGenerateContentRequest = Mscc.GenerativeAI.Types.GenerateContentRequest;
using GeminiGenerateContentResponse = Mscc.GenerativeAI.Types.GenerateContentResponse;
using GeminiGenerationConfig = Mscc.GenerativeAI.Types.GenerationConfig;

namespace AkmlSql.Engine.Ai.Providers;

/// <summary>
/// A thin <see cref="IChatClient"/> adapter wrapping <see cref="GenerativeModel"/>.
/// <para>
/// Maps the Microsoft.Extensions.AI chat abstraction to the Mscc.GenerativeAI SDK for Google Gemini.
/// System instructions are set on the <see cref="GeminiGenerateContentRequest"/>, and user/assistant
/// messages are mapped to Gemini's <see cref="GeminiContent"/> format with "user" and "model" roles.
/// </para>
/// </summary>
public sealed class GeminiChatClientAdapter : IChatClient
{
    private readonly GenerativeModel _model;
    private readonly string _modelName;

    /// <summary>
    /// Initialises a new Gemini adapter.
    /// </summary>
    /// <param name="apiKey">Google AI Studio API key.</param>
    /// <param name="modelName">Model identifier (e.g. "gemini-2.0-flash", "gemini-1.5-pro").</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="apiKey"/> or <paramref name="modelName"/> is empty.</exception>
    public GeminiChatClientAdapter(string apiKey, string modelName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Gemini API key is required.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Gemini model name is required.", nameof(modelName));

        _modelName = modelName;
        var googleAi = new GoogleAI(apiKey);
        _model = googleAi.GenerativeModel(model: modelName);
    }

    /// <inheritdoc />
    public ChatClientMetadata Metadata => new(nameof(GeminiChatClientAdapter), null, _modelName);

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatMessages);

        try
        {
            var messageList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();

            // Extract system instruction and build Gemini content list
            var (systemInstruction, contents) = MapMessages(messageList);

            var request = new GeminiGenerateContentRequest
            {
                Contents = contents
            };

            // Set system instruction if present
            if (!string.IsNullOrEmpty(systemInstruction))
            {
                request.SystemInstruction = new GeminiContent(systemInstruction);
            }

            // Apply generation config from ChatOptions
            request.GenerationConfig = BuildGenerationConfig(options);

            var response = await _model.GenerateContent(request, cancellationToken: cancellationToken);

            return MapResponse(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "GeminiChatClientAdapter: GenerateContent failed for model {Model}", _modelName);
            throw;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatMessages);

        var messageList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();

        // Extract system instruction and build Gemini content list
        var (systemInstruction, contents) = MapMessages(messageList);

        var request = new GeminiGenerateContentRequest
        {
            Contents = contents
        };

        if (!string.IsNullOrEmpty(systemInstruction))
        {
            request.SystemInstruction = new GeminiContent(systemInstruction);
        }

        request.GenerationConfig = BuildGenerationConfig(options);

        // Use Gemini's streaming API
        var stream = _model.GenerateContentStream(request);

        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            var text = chunk?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text)
                {
                    ModelId = _modelName
                };
            }
        }

        // Emit a final empty update to signal completion
        yield return new ChatResponseUpdate(ChatRole.Assistant, string.Empty)
        {
            FinishReason = ChatFinishReason.Stop,
            ModelId = _modelName
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(GenerativeModel) && serviceKey == null)
            return _model;

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // GenerativeModel does not implement IDisposable; nothing to clean up.
    }

    /// <summary>
    /// Builds a <see cref="GeminiGenerationConfig"/> from <see cref="ChatOptions"/>.
    /// </summary>
    private static GeminiGenerationConfig? BuildGenerationConfig(ChatOptions? options)
    {
        if (options == null) return null;

        var config = new GeminiGenerationConfig();
        bool hasConfig = false;

        if (options.Temperature.HasValue)
        {
            config.Temperature = (double)options.Temperature.Value;
            hasConfig = true;
        }

        if (options.MaxOutputTokens.HasValue)
        {
            config.MaxOutputTokens = options.MaxOutputTokens.Value;
            hasConfig = true;
        }

        if (options.TopP.HasValue)
        {
            config.TopP = (double)options.TopP.Value;
            hasConfig = true;
        }

        if (options.StopSequences is { Count: > 0 })
        {
            config.StopSequences = options.StopSequences.ToList();
            hasConfig = true;
        }

        return hasConfig ? config : null;
    }

    /// <summary>
    /// Maps a Gemini <see cref="GeminiGenerateContentResponse"/> to a <see cref="ChatResponse"/>.
    /// </summary>
    private ChatResponse MapResponse(GeminiGenerateContentResponse? response)
    {
        var responseText = response?.Text ?? string.Empty;
        var responseMessage = new ChatMessage(ChatRole.Assistant, responseText);

        var result = new ChatResponse(responseMessage)
        {
            ModelId = _modelName
        };

        // Map finish reason if available
        if (response?.Candidates is { Count: > 0 })
        {
            var candidate = response.Candidates[0];
            result.FinishReason = MapFinishReason(candidate.FinishReason);
        }

        // Map usage metadata if available
        if (response?.UsageMetadata != null)
        {
            result.Usage = new UsageDetails
            {
                InputTokenCount = response.UsageMetadata.PromptTokenCount,
                OutputTokenCount = response.UsageMetadata.CandidatesTokenCount,
                TotalTokenCount = response.UsageMetadata.TotalTokenCount
            };
        }

        return result;
    }

    /// <summary>
    /// Maps <see cref="ChatMessage"/> list to Gemini <see cref="GeminiContent"/> list,
    /// extracting any system instruction separately.
    /// </summary>
    private static (string? SystemInstruction, List<GeminiContent> Contents) MapMessages(
        IList<ChatMessage> messages)
    {
        string? systemInstruction = null;
        var contents = new List<GeminiContent>(messages.Count);

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                // Accumulate system messages into a single system instruction
                var text = ExtractTextContent(message);
                systemInstruction = systemInstruction == null
                    ? text
                    : systemInstruction + "\n" + text;
                continue;
            }

            var role = message.Role == ChatRole.Assistant ? "model" : "user";
            var contentText = ExtractTextContent(message);

            if (!string.IsNullOrEmpty(contentText))
            {
                contents.Add(new GeminiContent(contentText, role));
            }
        }

        return (systemInstruction, contents);
    }

    /// <summary>
    /// Extracts the concatenated text content from a <see cref="ChatMessage"/>.
    /// </summary>
    private static string ExtractTextContent(ChatMessage message)
    {
        if (message.Text is not null)
            return message.Text;

        if (message.Contents is { Count: > 0 })
        {
            var texts = new List<string>();
            foreach (var content in message.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    texts.Add(textContent.Text);
            }
            return string.Join("\n", texts);
        }

        return string.Empty;
    }

    /// <summary>
    /// Maps a Gemini finish reason to the <see cref="ChatFinishReason"/> abstraction.
    /// </summary>
    private static ChatFinishReason? MapFinishReason(GeminiFinishReason? reason)
    {
        if (reason == null) return null;

        return reason switch
        {
            GeminiFinishReason.Stop => ChatFinishReason.Stop,
            GeminiFinishReason.MaxTokens => ChatFinishReason.Length,
            GeminiFinishReason.Safety => ChatFinishReason.ContentFilter,
            _ => ChatFinishReason.Stop
        };
    }
}
