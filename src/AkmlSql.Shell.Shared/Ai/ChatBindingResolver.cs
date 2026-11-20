#nullable enable
using System;
using System.IO;
using AkmlSql.Shell.Shared.Editor;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>Spec 046 (US1): what the AI Chat found when it looked for its query window.</summary>
    internal enum ChatBindingState
    {
        /// <summary>A query window with a server and database — the chat can answer about it.</summary>
        Bound,
        /// <summary>A query window with no connection (or one still connecting).</summary>
        NotConnected,
        /// <summary>The active document is not a query window and no bound one survives.</summary>
        NotQueryWindow,
        /// <summary>No document is open and no bound query window survives.</summary>
        NoDocument
    }

    /// <summary>
    /// Spec 046 (US1): one open SQL editor as the resolver sees it — its editor session and the
    /// connection its window caption shows (null server/database when it shows none).
    /// </summary>
    internal sealed class TrackedViewInfo
    {
        public TrackedViewInfo(string sessionId, string? server, string? database)
        {
            SessionId = sessionId;
            Server = server;
            Database = database;
        }

        public string SessionId { get; }
        public string? Server { get; }
        public string? Database { get; }
        public bool IsConnected => !string.IsNullOrEmpty(Server) && !string.IsNullOrEmpty(Database);
    }

    /// <summary>
    /// Spec 046 (US1, data-model.md › ChatBinding): the query window the AI Chat answers about.
    /// The panel holds exactly one and reads its header, its schema poll, its send gate, its
    /// refusal text and its insert target from it (FR-002, FR-008), so no two parts of the panel can
    /// disagree about the connection again.
    /// </summary>
    internal sealed class ChatBinding
    {
        private ChatBinding(ChatBindingState state, string? sessionId, string? documentPath,
            string? server, string? database, bool isSticky)
        {
            State = state;
            SessionId = sessionId;
            DocumentPath = documentPath;
            Server = server;
            Database = database;
            IsSticky = isSticky;
        }

        public ChatBindingState State { get; }

        /// <summary>The editor session (<c>AkmlSqlSessionId</c>); set for Bound and NotConnected only.</summary>
        public string? SessionId { get; }

        /// <summary>The window's document path — matches tracked views and targets Insert into query.</summary>
        public string? DocumentPath { get; }

        public string? Server { get; }
        public string? Database { get; }

        /// <summary>True when this is the previous binding, kept because the active document is not a query window.</summary>
        public bool IsSticky { get; }

        /// <summary>The document's file name — the only part of the path that reaches the UI or the log.</summary>
        public string? DocumentName => FileNameOf(DocumentPath);

        /// <summary><c>{Server}.{Database}</c> when bound, else empty; compare with <see cref="SameKey"/>.</summary>
        public string BindingKey => State == ChatBindingState.Bound ? $"{Server}.{Database}" : string.Empty;

        /// <summary>The chat header's text (FR-027 of spec 036, kept).</summary>
        public string HeaderText => State == ChatBindingState.Bound
            ? $"{Server}.{Database}"
            : AiChatPanel.NotConnectedHeaderText;

        /// <summary>The header's tooltip: which window, and what it is connected to.</summary>
        public string? HeaderToolTip => State switch
        {
            ChatBindingState.Bound => $"{DocumentName} — {Server}.{Database}",
            ChatBindingState.NotConnected => $"{DocumentName} is not connected",
            _ => null
        };

        /// <summary>
        /// contracts/chat-binding.md Part 3: what the chat says instead of answering. Names what was
        /// found and what to do; never "as if the database were empty" (spec 036 FR-028).
        /// </summary>
        public string RefusalText => State switch
        {
            ChatBindingState.NotConnected =>
                $"{DocumentName} is not connected to a database (or is still connecting). " +
                "Connect it — Query ▸ Connection ▸ Connect — then ask again.",
            ChatBindingState.NotQueryWindow =>
                $"The active window ({DocumentName}) is not a query window, and no connected query " +
                "window has been used yet. Click into a connected query window, then ask again.",
            ChatBindingState.NoDocument =>
                "No query window is open. Open a query window, connect it to a database, then ask again.",
            _ => string.Empty
        };

        internal static ChatBinding ForBound(string sessionId, string documentPath, string server, string database,
            bool isSticky = false)
            => new ChatBinding(ChatBindingState.Bound, sessionId, documentPath, server, database, isSticky);

        internal static ChatBinding ForNotConnected(string sessionId, string documentPath)
            => new ChatBinding(ChatBindingState.NotConnected, sessionId, documentPath, null, null, false);

        internal static ChatBinding ForNotQueryWindow(string documentPath)
            => new ChatBinding(ChatBindingState.NotQueryWindow, null, documentPath, null, null, false);

        internal static ChatBinding ForNoDocument()
            => new ChatBinding(ChatBindingState.NoDocument, null, null, null, null, false);

        /// <summary>Same server and database, ordinal-ignore-case (SQL Server names are case-insensitive by default).</summary>
        internal static bool SameKey(ChatBinding? a, ChatBinding? b)
            => string.Equals(a?.BindingKey ?? string.Empty, b?.BindingKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        private static string? FileNameOf(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var name = Path.GetFileName(path);
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch (ArgumentException)
            {
                return path;   // a caption-like name with invalid path characters
            }
        }
    }

    /// <summary>
    /// Spec 046 (US1, contracts/chat-binding.md Part 1, research R1): decides which query window the
    /// AI Chat answers about. The chat follows the ACTIVE DOCUMENT — the query window tab SSMS shows
    /// as active, which stays put while a tool window (the chat itself, Object Explorer) or a pane
    /// inside the window (results grid, Messages) has the keyboard. The old rule asked for the text
    /// view that last had the keyboard and refused when that was the Messages pane — the reported
    /// "No database connection" while the header still named the database.
    ///
    /// <para>The editor-context AI commands (Explain, Fix, Text to SQL…) are invoked FROM the editor
    /// and keep <c>RefactorCommandHelper.TryGetActiveRealSessionId</c>; only the chat uses this.</para>
    /// </summary>
    internal static class ChatBindingResolver
    {
        /// <summary>
        /// The whole rule, pure and host-free (unit-tested off-host). <paramref name="activeDocumentPath"/>
        /// is null when no document is active; <paramref name="findTracked"/> maps a document path to
        /// the open SQL editor for it (null when the document is not one).
        /// </summary>
        internal static ChatBinding Decide(string? activeDocumentPath,
            Func<string, TrackedViewInfo?> findTracked, ChatBinding? previous)
        {
            if (!string.IsNullOrEmpty(activeDocumentPath))
            {
                var active = findTracked(activeDocumentPath!);
                if (active != null)
                {
                    return active.IsConnected
                        ? ChatBinding.ForBound(active.SessionId, activeDocumentPath!, active.Server!, active.Database!)
                        : ChatBinding.ForNotConnected(active.SessionId, activeDocumentPath!);
                }
            }

            // FR-003: the active document is not a query window (or there is none) — keep the last
            // bound window while it is open and still connected, re-reading its current database.
            if (previous is { State: ChatBindingState.Bound } && !string.IsNullOrEmpty(previous.DocumentPath))
            {
                var kept = findTracked(previous.DocumentPath!);
                if (kept != null && kept.IsConnected)
                    return ChatBinding.ForBound(kept.SessionId, previous.DocumentPath!, kept.Server!, kept.Database!, isSticky: true);
            }

            return string.IsNullOrEmpty(activeDocumentPath)
                ? ChatBinding.ForNoDocument()
                : ChatBinding.ForNotQueryWindow(activeDocumentPath!);
        }

        /// <summary>
        /// UI thread. Gathers the active document's path from DTE and, per path, the tracked SQL
        /// editor and its caption's connection; then <see cref="Decide"/>. Never throws — a host
        /// failure yields <see cref="ChatBindingState.NoDocument"/> and a Debug line.
        /// </summary>
        internal static ChatBinding Resolve(ChatBinding? previous)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                IServiceProvider serviceProvider = ServiceProvider.GlobalProvider;
                var dte = serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;

                string? activePath = null;
                try
                {
                    activePath = dte?.ActiveDocument?.FullName;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ChatBindingResolver: DTE.ActiveDocument unavailable");
                }

                return Decide(activePath, path => FindTracked(serviceProvider, path), previous);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ChatBindingResolver: binding resolution failed");
                return ChatBinding.ForNoDocument();
            }
        }

        private static TrackedViewInfo? FindTracked(IServiceProvider serviceProvider, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();   // only reached from Resolve, which runs on the UI thread
            if (!ConnectionWiringHelper.TryFindTrackedView(path, out var sessionId, out var view) ||
                string.IsNullOrEmpty(sessionId))
                return null;

            var connection = SsmsConnectionDetector.TryDetectConnection(serviceProvider, view);
            return new TrackedViewInfo(sessionId, connection?.Server, connection?.Database);
        }
    }
}
