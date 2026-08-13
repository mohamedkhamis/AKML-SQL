using System;
using System.Collections.Generic;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Maps an open editor document to a stable query-session key. The key lives only as long as
    /// the document stays open: <see cref="Forget"/> is called from the document-close hook, so
    /// reopening the same file starts a new session ("one tab, one history entry").
    /// </summary>
    internal static class DocumentSessionKeys
    {
        private static readonly Dictionary<string, string> Keys =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        internal static string ForDocument(string documentFullName)
        {
            if (string.IsNullOrEmpty(documentFullName))
                return Guid.NewGuid().ToString("N");   // unidentifiable doc — its own session

            lock (Gate)
            {
                if (!Keys.TryGetValue(documentFullName, out var key))
                {
                    key = Guid.NewGuid().ToString("N");
                    Keys[documentFullName] = key;
                }
                return key;
            }
        }

        internal static void Forget(string documentFullName)
        {
            if (string.IsNullOrEmpty(documentFullName)) return;
            lock (Gate) { Keys.Remove(documentFullName); }
        }

        /// <summary>
        /// Migrates the session key tracked under <paramref name="oldFullName"/> to
        /// <paramref name="newFullName"/> — the SAME session continues under the new name.
        /// Called when a document is renamed on disk (Save As, or the first Save of an unsaved
        /// scratch document), so executions before and after the save land in one history entry
        /// instead of splitting into two. A no-op if <paramref name="oldFullName"/> has no tracked
        /// key (nothing to migrate) or the two names are equal.
        /// </summary>
        internal static void Rename(string oldFullName, string newFullName)
        {
            if (string.IsNullOrEmpty(oldFullName) || string.IsNullOrEmpty(newFullName)) return;
            if (string.Equals(oldFullName, newFullName, StringComparison.OrdinalIgnoreCase)) return;

            lock (Gate)
            {
                if (Keys.TryGetValue(oldFullName, out var key))
                {
                    Keys.Remove(oldFullName);
                    Keys[newFullName] = key; // same session key, now tracked under the new name
                }
            }
        }
    }
}
