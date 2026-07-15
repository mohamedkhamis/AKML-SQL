#nullable enable
using System;
using System.Collections.Generic;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Productivity;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.CommandPalette
{
    /// <summary>
    /// Spec 030 T086 / FR-045 — Command Palette provider that surfaces matching database objects
    /// (tables, views, procedures, functions, …) alongside the static command results.
    ///
    /// It issues an <see cref="ObjectSearchRequest"/> to the engine (T085 handler) using the active
    /// editor's session id, and maps each hit onto the palette's shared <see cref="CommandEntry"/>
    /// result model so the existing <c>ListBox</c> item template renders it with no UI changes. DB
    /// object entries are distinguished by the <see cref="IdPrefix"/> on their <see cref="CommandEntry.Id"/>;
    /// the remainder is the schema-qualified name the ViewModel inserts at the caret when selected.
    ///
    /// Everything degrades silently to "no DB objects" (an empty list): if the engine is not running,
    /// the session is not connected, or the schema cache is not yet populated, the engine returns a
    /// successful-but-empty (or failed) response and the palette simply shows commands only.
    /// </summary>
    internal static class DbObjectProvider
    {
        /// <summary>
        /// Marker prefix on <see cref="CommandEntry.Id"/> that identifies a DB-object result. The text
        /// after the prefix is the schema-qualified object name (e.g. <c>dbobj:dbo.usp_GetUsers</c>).
        /// </summary>
        public const string IdPrefix = "dbobj:";

        private const int MaxResults = 50;
        private const int TimeoutMs = 5000;

        /// <summary>Minimum search-text length before a DB-object query is worth sending. Shared with
        /// <see cref="CommandPaletteViewModel"/>'s pre-debounce guard so the two cannot drift.</summary>
        internal const int MinChars = 2;

        /// <summary>
        /// Queries the engine for database objects matching <paramref name="searchText"/>. Runs the IPC
        /// off the caller's thread (the underlying send is async I/O) and never throws — any failure
        /// yields an empty list so the palette degrades to commands-only.
        /// </summary>
        /// <param name="sessionId">Active editor session id used to resolve the live connection + schema cache.</param>
        /// <param name="searchText">Fuzzy search text typed into the palette.</param>
        public static async System.Threading.Tasks.Task<IReadOnlyList<CommandEntry>> SearchAsync(
            string? sessionId, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText) || searchText.Trim().Length < MinChars)
                return Array.Empty<CommandEntry>();

            if (string.IsNullOrEmpty(sessionId))
                return Array.Empty<CommandEntry>();

            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                    return Array.Empty<CommandEntry>();

                var request = new ObjectSearchRequest
                {
                    SessionId = sessionId!,
                    SearchText = searchText,
                    MaxResults = MaxResults
                };

                var response = await client
                    .SendRequestAsync<ObjectSearchResponse, ObjectSearchRequest>(
                        MessageTypes.ObjectSearch, request, timeoutMs: TimeoutMs)
                    .ConfigureAwait(false);

                if (response == null || !response.Success || response.Results == null || response.Results.Length == 0)
                    return Array.Empty<CommandEntry>();

                var list = new List<CommandEntry>(response.Results.Length);
                foreach (var r in response.Results)
                {
                    var qualified = string.IsNullOrEmpty(r.SchemaName)
                        ? r.ObjectName
                        : $"{r.SchemaName}.{r.ObjectName}";

                    if (string.IsNullOrEmpty(qualified))
                        continue;

                    list.Add(new CommandEntry
                    {
                        // Id carries the exact text inserted at the caret when the entry is chosen.
                        Id = IdPrefix + qualified,
                        Name = qualified,
                        Category = FriendlyType(r.ObjectType),
                        KeyboardShortcut = null
                    });
                }

                return list;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "DbObjectProvider: object search failed for '{Search}'", searchText);
                return Array.Empty<CommandEntry>();
            }
        }

        /// <summary>
        /// Extracts the schema-qualified object name (the text to insert) from a DB-object entry id.
        /// Returns the whole id unchanged if it lacks the <see cref="IdPrefix"/>.
        /// </summary>
        public static string GetInsertText(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return string.Empty;
            return entryId.StartsWith(IdPrefix, StringComparison.Ordinal)
                ? entryId.Substring(IdPrefix.Length)
                : entryId;
        }

        /// <summary>True when the entry id denotes a DB-object result rather than a command.</summary>
        public static bool IsDbObject(string entryId) =>
            !string.IsNullOrEmpty(entryId) && entryId.StartsWith(IdPrefix, StringComparison.Ordinal);

        /// <summary>Maps the engine's raw object-type token onto a short category label for the palette.</summary>
        private static string FriendlyType(string? objectType) => objectType switch
        {
            "Table" => "Table",
            "View" => "View",
            "Procedure" => "Procedure",
            "ScalarFunction" or "TableFunction" or "InlineFunction" => "Function",
            "Synonym" => "Synonym",
            "Sequence" => "Sequence",
            null or "" => "Object",
            _ => objectType
        };
    }
}
