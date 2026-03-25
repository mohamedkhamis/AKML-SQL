using System.Diagnostics;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.Extensions.AI;
using Serilog;

namespace AkmlSql.Engine.Ai.Streaming;

/// <summary>
/// Batches streaming AI tokens into coalesced chunks to reduce IPC frame overhead.
/// <para>
/// Emits an <see cref="AiStreamChunkMessage"/> every 50 ms or every 5 tokens (whichever
/// comes first), accumulating text deltas between emissions. A final chunk with
/// <see cref="AiStreamChunkMessage.IsComplete"/> = <c>true</c> is always emitted on
/// completion, cancellation, or error.
/// </para>
/// </summary>
public static class StreamCoalescer
{
    /// <summary>Minimum interval between chunk emissions.</summary>
    private static readonly TimeSpan EmitInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Maximum number of tokens to accumulate before forcing an emission.</summary>
    private const int MaxTokensBeforeEmit = 5;

    /// <summary>
    /// Consumes a streaming AI response and emits coalesced chunks via <paramref name="sendChunk"/>.
    /// </summary>
    /// <param name="stream">
    /// The async enumerable of streaming updates from <c>IChatClient.GetStreamingResponseAsync()</c>.
    /// </param>
    /// <param name="requestId">The request ID of the originating AI request.</param>
    /// <param name="sendChunk">
    /// Callback to send each coalesced <see cref="AiStreamChunkMessage"/> to the shell.
    /// </param>
    /// <param name="ct">Cancellation token to abort the stream.</param>
    /// <returns>A task that completes when the stream is fully consumed or cancelled.</returns>
    public static async Task CoalesceAsync(
        IAsyncEnumerable<ChatResponseUpdate> stream,
        int requestId,
        Func<AiStreamChunkMessage, Task> sendChunk,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sendChunk);

        var buffer = new System.Text.StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        int totalTokens = 0;
        int tokensSinceLastEmit = 0;

        try
        {
            await foreach (var update in stream.WithCancellation(ct))
            {
                // Extract text from the streaming update
                var text = ExtractText(update);

                if (!string.IsNullOrEmpty(text))
                {
                    buffer.Append(text);
                    tokensSinceLastEmit++;
                    totalTokens++;
                }

                // Emit if we've accumulated enough tokens or enough time has passed
                if (buffer.Length > 0 &&
                    (tokensSinceLastEmit >= MaxTokensBeforeEmit ||
                     stopwatch.Elapsed >= EmitInterval))
                {
                    await sendChunk(new AiStreamChunkMessage
                    {
                        RequestId = requestId,
                        Text = buffer.ToString(),
                        IsComplete = false,
                        FinishReason = null,
                        TokensUsed = totalTokens
                    });

                    buffer.Clear();
                    tokensSinceLastEmit = 0;
                    stopwatch.Restart();
                }
            }

            // Emit final chunk with any remaining buffered text
            await sendChunk(new AiStreamChunkMessage
            {
                RequestId = requestId,
                Text = buffer.ToString(),
                IsComplete = true,
                FinishReason = "stop",
                TokensUsed = totalTokens
            });

            Log.Debug("StreamCoalescer: stream completed for request {RequestId}, total tokens: {Tokens}",
                requestId, totalTokens);
        }
        catch (OperationCanceledException)
        {
            // Emit cancellation final chunk
            await EmitFinalChunkSafe(sendChunk, requestId, buffer.ToString(), totalTokens, "cancelled");

            Log.Debug("StreamCoalescer: stream cancelled for request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            // Emit error final chunk
            await EmitFinalChunkSafe(sendChunk, requestId, buffer.ToString(), totalTokens, "error");

            Log.Warning(ex, "StreamCoalescer: stream error for request {RequestId}", requestId);
        }
    }

    /// <summary>
    /// Extracts text content from a <see cref="ChatResponseUpdate"/>.
    /// The update may contain text in its <c>Text</c> property or within its <c>Contents</c> collection.
    /// </summary>
    private static string? ExtractText(ChatResponseUpdate update)
    {
        // Try the Text convenience property first
        if (!string.IsNullOrEmpty(update.Text))
            return update.Text;

        // Fall back to iterating Contents for TextContent items
        if (update.Contents is { Count: > 0 })
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    return textContent.Text;
            }
        }

        return null;
    }

    /// <summary>
    /// Safely emits a final chunk, swallowing any exception from the send callback
    /// to avoid masking the original error.
    /// </summary>
    private static async Task EmitFinalChunkSafe(
        Func<AiStreamChunkMessage, Task> sendChunk,
        int requestId,
        string remainingText,
        int totalTokens,
        string finishReason)
    {
        try
        {
            await sendChunk(new AiStreamChunkMessage
            {
                RequestId = requestId,
                Text = remainingText,
                IsComplete = true,
                FinishReason = finishReason,
                TokensUsed = totalTokens
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "StreamCoalescer: failed to emit final '{FinishReason}' chunk for request {RequestId}",
                finishReason, requestId);
        }
    }
}
