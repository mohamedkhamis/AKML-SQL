#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (HIS-02, FR-011, data-model §2.3) — every decision about a query's open/closed state
    /// in SQL History, as pure functions. The callers (<see cref="ExecutionCapture"/> and the
    /// package) only send the request these return; null means "send nothing".
    /// </summary>
    internal static class OpenStateReporter
    {
        /// <summary>
        /// The startup request telling the engine which queries this SSMS has open: the session
        /// keys of the open query documents (<c>*.sql</c> files and unsaved <c>SQLQuery…</c> tabs)
        /// that have one. The engine closes this shell's other sessions and reports the queries
        /// left open by an SSMS that is gone.
        /// </summary>
        public static HistoryActionRequest BuildReconcileRequest(int pid, IEnumerable<(string FullName, string? Key)> documents)
        {
            var keys = (documents ?? Enumerable.Empty<(string, string?)>())
                .Where(d => !string.IsNullOrEmpty(d.Key) && IsQueryDocument(d.FullName))
                .Select(d => d.Key!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new HistoryActionRequest
            {
                Action = HistoryActions.ReconcileOpen,
                OwnerPid = pid,
                OpenSessionKeys = keys,
            };
        }

        /// <summary>After a run is recorded: the query is open in this shell.</summary>
        public static HistoryActionRequest OnRecorded(string key, int pid) => Open(key, pid);

        /// <summary>A tab gained focus: its query is open in this shell (if it has run before).</summary>
        public static HistoryActionRequest? OnActivated(string? key, int pid) =>
            string.IsNullOrEmpty(key) ? null : Open(key!, pid);

        /// <summary>
        /// A tab is closing: its query is closed — except while SSMS shuts down, so the queries open
        /// at exit stay open and can be offered for restore on the next start.
        /// </summary>
        public static HistoryActionRequest? OnClosing(string? key, int pid, bool shuttingDown) =>
            string.IsNullOrEmpty(key) || shuttingDown
                ? null
                : new HistoryActionRequest
                {
                    Action = HistoryActions.SetOpenStatus,
                    IsOpen = false,
                    SessionKey = key,
                    OwnerPid = pid,
                };

        private static HistoryActionRequest Open(string key, int pid) => new HistoryActionRequest
        {
            Action = HistoryActions.SetOpenStatus,
            IsOpen = true,
            SessionKey = key,
            OwnerPid = pid,
        };

        /// <summary>A <c>*.sql</c> file or an unsaved <c>SQLQuery…</c> query tab.</summary>
        internal static bool IsQueryDocument(string? fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return false;
            if (fullName!.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) return true;
            string name;
            try { name = Path.GetFileName(fullName); }
            catch (ArgumentException) { name = fullName; }
            return name.StartsWith("SQLQuery", StringComparison.OrdinalIgnoreCase);
        }
    }
}
