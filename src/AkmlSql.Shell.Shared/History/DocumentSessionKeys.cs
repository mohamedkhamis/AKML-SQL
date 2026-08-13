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
    }
}
