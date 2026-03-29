using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Non-blocking completion helper. AugmentCompletionSession runs on the UI thread,
    /// so we CANNOT make synchronous RPC calls. Instead:
    /// 1. Return cached completions immediately (if available)
    /// 2. Trigger a background RPC fetch for next time
    /// 3. First Ctrl+Space after connecting returns keywords only; second returns full results
    /// </summary>
    internal static class CompletionRpcHelper
    {
        // Per-session cached completions from the last successful RPC
        private static readonly ConcurrentDictionary<string, CompletionItem[]> Cache = new();

        /// <summary>
        /// Returns cached completions immediately (NEVER blocks UI thread).
        /// Triggers a background fetch for fresh results at the current cursor position.
        /// Flow: type SQL → document synced via OnBufferChanged → Ctrl+Space →
        /// returns cached items + triggers fresh fetch → next Ctrl+Space shows fresh results.
        /// </summary>
        public static List<Completion> GetCachedCompletions(ITextBuffer buffer, ICompletionSession session)
        {
            if (!buffer.Properties.TryGetProperty("AkmlSqlSessionId", out string sessionId))
                return new List<Completion>();

            var point = session.GetTriggerPoint(buffer.CurrentSnapshot);
            if (point == null)
                return new List<Completion>();

            var cursorOffset = point.Value.Position;
            var docText = buffer.CurrentSnapshot.GetText();

            // Trigger background fetch with current document text and cursor
            TriggerBackgroundFetch(sessionId, cursorOffset, docText);

            // Return cached results immediately
            if (Cache.TryGetValue(sessionId, out var items) && items != null && items.Length > 0)
                return ConvertItems(items);

            return new List<Completion>();
        }

        private static void TriggerBackgroundFetch(string sessionId, int cursorOffset, string docText)
        {
            Task.Run(async () =>
            {
                try
                {
                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;

                    // Send document text first
                    await client.SendNotificationAsync(MessageTypes.DocumentChanged,
                        new DocumentChange { SessionId = sessionId, ChangeType = 0, FullText = docText });

                    // Small delay to let Engine process the document
                    await Task.Delay(50);

                    var response = await client.SendRequestAsync<CompletionResponse, CompletionRequest>(
                        MessageTypes.RequestCompletion,
                        new CompletionRequest { SessionId = sessionId, CursorOffset = cursorOffset, TriggerKind = 1 },
                        timeoutMs: 3000);

                    if (response?.Items != null && response.Items.Length > 0)
                    {
                        Cache[sessionId] = response.Items;
                        Log.Debug("Completion fetch: {Count} items at offset {Offset}",
                            response.Items.Length, cursorOffset);
                    }
                }
                catch (Exception ex) { Log.Debug(ex, "Completion fetch failed"); }
            });
        }

        private static List<Completion> ConvertItems(CompletionItem[] items)
        {
            var completions = new List<Completion>(items.Length);
            foreach (var item in items)
            {
                ImageSource icon = null;
                try { icon = SqlPromptIcons.GetIcon(item.ObjectType); }
                catch { /* icon is optional */ }

                var description = !string.IsNullOrEmpty(item.SecondaryText)
                    ? $"{item.DisplayText} — {item.SecondaryText}"
                    : item.DisplayText;

                completions.Add(new Completion(
                    displayText: item.DisplayText,
                    insertionText: item.InsertText ?? item.DisplayText,
                    description: description,
                    iconSource: icon,
                    iconAutomationText: null));
            }
            return completions;
        }

        internal class SessionIdKey { }
    }
}
