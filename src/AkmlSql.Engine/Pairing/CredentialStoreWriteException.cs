using System.IO;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 043 follow-up (review): a remembered password could not be forgotten because the engine's credential store
    /// (<see cref="AkmlSql.Core.Config.SqlCredentialStore"/>) could not be written — or, since review round 3, could not be read,
    /// so nothing in it could be removed — and the entry may still be there. Thrown by
    /// <see cref="SqlServerCredentialResolver.Forget"/> and <see cref="AkmlSql.Engine.PostgreSql.PgCredentialResolver.Forget"/> so
    /// the <c>CredentialRequest</c> handler answers <c>Ok = false</c> instead of reporting the password as forgotten. The message is
    /// the one the browser shows; it names no server, login or password.
    /// </summary>
    internal sealed class CredentialStoreWriteException : IOException
    {
        /// <summary>What the browser is told.</summary>
        internal const string UserMessage = "The credential store could not be written.";

        public CredentialStoreWriteException() : base(UserMessage) { }
    }
}
