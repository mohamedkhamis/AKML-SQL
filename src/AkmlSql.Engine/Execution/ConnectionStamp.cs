using System;
using System.Security.Cryptography;
using System.Text;
using AkmlSql.Core;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Ties an editable result to the connection it was read through. Every execute reply carries the stamp of the
    /// session's connection after the run (<see cref="Core.Ipc.Messages.ExecuteQueryResult.ConnectionStamp"/>), the
    /// grid sends it back with its edits, and <see cref="ApplyChangesHandler"/> refuses edits whose stamp is not the
    /// session's current one. The connection string names the server, the login and the database (a database switch
    /// rewrites it), so a different server, login, database or kind changes the stamp.
    ///
    /// <para>The stamp is an HMAC under a key made when this engine process starts: it says nothing about the
    /// connection string to the browser, and a stamp from another engine, or from before a restart, never matches.</para>
    /// </summary>
    public static class ConnectionStamp
    {
        private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

        /// <summary>The stamp of a session connected to <paramref name="connectionString"/>; null when there is no connection.</summary>
        public static string? Of(DatabaseKind kind, string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return null;
            var data = Encoding.UTF8.GetBytes(((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + connectionString);
            return Convert.ToHexString(HMACSHA256.HashData(ProcessKey, data));
        }

        /// <summary>
        /// True when edits stamped <paramref name="stamp"/> may be written through the session's current connection. An
        /// empty stamp comes from an older web bundle that does not send one, and is accepted as before.
        /// </summary>
        public static bool Allows(string? stamp, DatabaseKind kind, string? connectionString)
        {
            if (string.IsNullOrEmpty(stamp)) return true;
            var current = Of(kind, connectionString);
            return current != null
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stamp), Encoding.ASCII.GetBytes(current));
        }

        /// <summary>What the grid shows when the engine refuses stale edits.</summary>
        public const string StaleMessage =
            "These results came from a different connection (another server, login, database or engine). " +
            "Nothing was written. Run the query again on the current connection to edit its rows.";
    }
}
