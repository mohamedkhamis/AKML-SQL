using System;
using System.Collections.Generic;
using Serilog;

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
        /// <para>
        /// <b>Collision guard:</b> if <paramref name="newFullName"/> already maps to a DIFFERENT
        /// key — meaning some other still-open document already owns that path (e.g. a Save-As
        /// overwrote a file another open tab is tracking) — the migration is skipped rather than
        /// overwriting that entry. Overwriting would silently MERGE two unrelated tabs' history
        /// into one session, which is strictly worse than the split this method exists to fix: a
        /// split is visible and truthful, a merge is silent data conflation the user cannot detect.
        /// The entry under <paramref name="oldFullName"/> is retired either way — its owner's
        /// <c>FullName</c> has already changed, so nothing will ever look it up under the old name
        /// again; on a collision the renamed document simply mints a fresh key on its next
        /// execution (a safe split, not a merge).
        /// </para>
        /// </summary>
        internal static void Rename(string oldFullName, string newFullName)
        {
            if (string.IsNullOrEmpty(oldFullName) || string.IsNullOrEmpty(newFullName)) return;
            if (string.Equals(oldFullName, newFullName, StringComparison.OrdinalIgnoreCase)) return;

            lock (Gate)
            {
                if (!Keys.TryGetValue(oldFullName, out var key)) return; // nothing tracked — nothing to migrate

                if (Keys.TryGetValue(newFullName, out var existingKey)
                    && !string.Equals(existingKey, key, StringComparison.Ordinal))
                {
                    Log.Debug(
                        "DocumentSessionKeys: rename collision on '{New}' (from '{Old}') — already owned by a " +
                        "different session; skipping migration to avoid merging two tabs' history",
                        newFullName, oldFullName);
                    Keys.Remove(oldFullName); // retire the stale entry; its owner no longer uses this name
                    return;
                }

                Keys.Remove(oldFullName);
                Keys[newFullName] = key; // same session key, now tracked under the new name
            }
        }
    }
}
