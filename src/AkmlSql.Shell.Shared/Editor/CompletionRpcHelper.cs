using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Editor.Completion;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Non-blocking Engine RPC helper for completions.
    /// All methods are safe to call from any thread. Engine calls are always async.
    /// Used by CompletionController to fetch items from the Engine.
    /// </summary>
    internal static class CompletionRpcHelper
    {
        // Per-session cached completion items
        private static readonly ConcurrentDictionary<string, CompletionItemModel[]> Cache = new();

        /// <summary>
        /// Returns cached completion items for the session (never blocks).
        /// </summary>
        public static CompletionItemModel[] GetCached(string sessionId)
        {
            if (Cache.TryGetValue(sessionId, out var items))
                return items;
            return Array.Empty<CompletionItemModel>();
        }

        /// <summary>
        /// Triggers a background fetch from the Engine. Sends document text first,
        /// then requests completions at the cursor offset. Results are cached.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void FetchAsync(string sessionId, int cursorOffset, string docText)
        {
            Task.Run(async () =>
            {
                try
                {
                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;

                    // Send document text first, then wait for Engine to parse and
                    // update the session so CursorContextAnalyzer sees FROM/JOIN context
                    await client.SendNotificationAsync(MessageTypes.DocumentChanged,
                        new DocumentChange { SessionId = sessionId, ChangeType = 0, FullText = docText });

                    await Task.Delay(150); // Let Engine process the document

                    var response = await client.SendRequestAsync<CompletionResponse, CompletionRequest>(
                        MessageTypes.RequestCompletion,
                        new CompletionRequest { SessionId = sessionId, CursorOffset = cursorOffset, TriggerKind = 1 },
                        timeoutMs: 3000);

                    if (response?.Items != null && response.Items.Length > 0)
                    {
                        var models = new CompletionItemModel[response.Items.Length];
                        for (int i = 0; i < response.Items.Length; i++)
                        {
                            var item = response.Items[i];
                            models[i] = new CompletionItemModel
                            {
                                DisplayText = item.DisplayText ?? string.Empty,
                                InsertText = item.InsertText ?? item.DisplayText ?? string.Empty,
                                SecondaryText = item.SecondaryText ?? string.Empty,
                                ObjectType = item.ObjectType,
                                SortPriority = item.SortPriority
                            };
                        }

                        Cache[sessionId] = models;
                        Log.Debug("Completion fetch: {Count} items at offset {Offset}",
                            models.Length, cursorOffset);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Completion fetch failed");
                }
            });
        }

        /// <summary>Clears cached completions for a session.</summary>
        public static void ClearCache(string sessionId)
        {
            Cache.TryRemove(sessionId, out _);
        }
    }
}
