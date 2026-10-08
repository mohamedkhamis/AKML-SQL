#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (T133, HIS-13/HIS-14) — the pure rules behind capturing query text History keeps
    /// without a run: drafts (a query tab's text that was never run), the size limit, and which tabs
    /// the autosave snapshots. <see cref="ExecutionCapture"/> applies them.
    /// </summary>
    internal static class DraftCapturePolicy
    {
        internal const int MinLimitKb = 16;
        internal const int MaxLimitKb = 1024;

        /// <summary>
        /// A draft is recorded for a query document (<c>*.sql</c> or <c>SQLQuery*</c>) with text that
        /// has no History session yet; one with a session gets a version snapshot instead.
        /// </summary>
        internal static bool ShouldCaptureDraft(string? documentName, string? text, bool hasSessionKey) =>
            !hasSessionKey && !string.IsNullOrWhiteSpace(text) && OpenStateReporter.IsQueryDocument(documentName);

        /// <summary>
        /// Text within <paramref name="limitKb"/> KB (clamped to 16–1024) is returned as is; longer
        /// text is cut to that many characters, followed by a line saying it was cut.
        /// </summary>
        internal static string TruncateToLimit(string text, int limitKb)
        {
            var kb = Math.Max(MinLimitKb, Math.Min(MaxLimitKb, limitKb));
            var limit = kb * 1024;
            if (text == null || text.Length <= limit) return text!;
            return text.Substring(0, limit) + $"\r\n-- [truncated by AKML SQL: query larger than {kb} KB]";
        }

        /// <summary>The documents the autosave snapshots: query documents with unsaved changes.</summary>
        internal static IEnumerable<string> SelectAutosaveTargets(IEnumerable<(string FullName, bool IsDirty)> documents) =>
            documents.Where(d => d.IsDirty && OpenStateReporter.IsQueryDocument(d.FullName)).Select(d => d.FullName);
    }
}
