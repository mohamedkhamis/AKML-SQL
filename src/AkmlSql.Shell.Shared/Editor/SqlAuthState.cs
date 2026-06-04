namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Spec 029. Per-buffer marker for a SQL-authentication editor window, stored in
    /// <c>ITextBuffer.Properties["AkmlSqlAuthState"]</c>. Its presence tells the schema-progress
    /// margin this is a SQL-auth session (so a server-side login rejection is shown as
    /// "credentials rejected" rather than a Windows-auth permission denial). It NEVER holds the
    /// plaintext password — the password lives only (encrypted) in <c>SqlCredentialStore</c>.
    /// </summary>
    internal sealed class SqlAuthState
    {
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;

        /// <summary>True when this SQL-auth window has no engine session yet — no stored credential,
        /// or its credential was rejected. The margin renders the click-to-enter affordance.</summary>
        public bool NeedsCredentials { get; set; }
    }
}
