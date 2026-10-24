using System;
using Microsoft.Data.SqlClient;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 043 follow-up (review): when the engine may put a SQL Server password the browser never sent into a bridge request
    /// (a copied tab password, <see cref="SessionPasswordCopy"/>, or a remembered one, <see cref="SqlServerCredentialResolver"/>).
    /// Matching the server and login alone is not enough: a request could ask for no encryption, accept any certificate, name
    /// another host for the certificate, send the login to a failover partner, or use another authentication method, and the
    /// password would go somewhere it was never given to. The web client always sends a plain SQL login (server, database,
    /// <c>Encrypt=true</c>, its certificate choice, application name, timeout, login), so these rules cost it nothing.
    /// </summary>
    internal static class BridgeSqlSecurity
    {
        /// <summary>
        /// A SQL Server login and nothing else: no Windows or Entra authentication, the connection encrypted (Mandatory or
        /// Strict, never Optional), and no failover partner, certificate host name, certificate file or SPN of its own.
        /// </summary>
        public static bool IsPlainSqlLogin(SqlConnectionStringBuilder b)
        {
            if (b == null || b.IntegratedSecurity) return false;
            if (b.Authentication is not (SqlAuthenticationMethod.NotSpecified or SqlAuthenticationMethod.SqlPassword)) return false;
            if (!IsEncrypted(b.Encrypt)) return false;
            return string.IsNullOrEmpty(b.FailoverPartner)
                && string.IsNullOrEmpty(b.FailoverPartnerSPN)
                && string.IsNullOrEmpty(b.HostNameInCertificate)
                && string.IsNullOrEmpty(b.ServerCertificate)
                && string.IsNullOrEmpty(b.ServerSPN);
        }

        /// <summary>
        /// Both name the same server and login and ask for the same encryption, certificate checking, certificate host name,
        /// certificate file, SPNs, failover partner and authentication method.
        /// </summary>
        public static bool SameSecurity(SqlConnectionStringBuilder a, SqlConnectionStringBuilder b) =>
            Same(a.DataSource?.Trim(), b.DataSource?.Trim())
            && Same(a.UserID, b.UserID)
            && Equals(a.Encrypt, b.Encrypt)
            && a.TrustServerCertificate == b.TrustServerCertificate
            && Same(a.HostNameInCertificate, b.HostNameInCertificate)
            && Same(a.ServerCertificate, b.ServerCertificate)
            && Same(a.ServerSPN, b.ServerSPN)
            && Same(a.FailoverPartner, b.FailoverPartner)
            && Same(a.FailoverPartnerSPN, b.FailoverPartnerSPN)
            && a.Authentication == b.Authentication;

        private static bool IsEncrypted(SqlConnectionEncryptOption? encrypt) =>
            Equals(encrypt, SqlConnectionEncryptOption.Mandatory) || Equals(encrypt, SqlConnectionEncryptOption.Strict);

        // Host names, certificate paths and SPNs are case-insensitive on Windows; a missing value and an empty one are the same.
        private static bool Same(string? a, string? b) =>
            string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
