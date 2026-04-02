using System;

namespace AkmlSql.Core.Models.Safety
{
    /// <summary>
    /// Tracks the state of an open transaction on a specific editor tab.
    /// Used by <c>TransactionMonitor</c> to detect uncommitted transactions
    /// and issue reminders.
    /// </summary>
    public sealed class TransactionState
    {
        /// <summary>
        /// Unique identifier for the tab/document (typically the document moniker / full path).
        /// </summary>
        public string TabId { get; }

        /// <summary>
        /// UTC timestamp of when the first <c>BEGIN TRAN</c> was detected for this tab.
        /// </summary>
        public DateTime StartedAt { get; }

        /// <summary>
        /// UTC timestamp of the last time a reminder was shown to the user, or <c>null</c>
        /// if no reminder has been shown yet.
        /// </summary>
        public DateTime? LastReminderAt { get; set; }

        /// <summary>
        /// Nesting depth of the open transaction. Incremented by <c>BEGIN TRAN</c>,
        /// decremented by <c>COMMIT</c>, and cleared entirely by <c>ROLLBACK</c>.
        /// </summary>
        public int TranCount { get; set; }

        /// <summary>Creates a new <see cref="TransactionState"/> for a tab.</summary>
        /// <param name="tabId">The document identifier (moniker).</param>
        /// <param name="startedAt">UTC timestamp of the first BEGIN TRAN detection.</param>
        /// <param name="tranCount">Initial transaction nesting depth (default 1).</param>
        public TransactionState(string tabId, DateTime startedAt, int tranCount = 1)
        {
            TabId    = tabId ?? throw new ArgumentNullException(nameof(tabId));
            StartedAt = startedAt.Kind == DateTimeKind.Utc ? startedAt : startedAt.ToUniversalTime();
            TranCount = tranCount;
        }
    }
}
