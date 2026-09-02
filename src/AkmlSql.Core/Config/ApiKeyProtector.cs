using System;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 036 (US2, FR-008) — DPAPI wrap/unwrap for AI provider API keys, promoted from the
    /// engine's <c>CredentialManager</c> so the net472 Options page can protect keys at save time.
    /// The engine's <c>CredentialManager</c> delegates here, so there is exactly one mechanism.
    /// <para>
    /// Encrypted values are stored with a <c>dpapi:</c> prefix followed by a Base64-encoded
    /// DPAPI blob. Values without the prefix are legacy plaintext and pass through unchanged on
    /// read, so no migration step is needed — the first save after this change upgrades the
    /// stored value in place.
    /// </para>
    /// </summary>
    public static class ApiKeyProtector
    {
        private const string EncryptedPrefix = "dpapi:";

        // CRITICAL: the entropy source string must stay "AkmlSql-ApiKey-v1" byte-for-byte —
        // changing it makes every key stored by an earlier build undecryptable. Note it differs
        // from SqlCredentialStore's entropy ("AkmlSql-SqlCred-v1"); they are not interchangeable.
        private const string EntropySource = "AkmlSql-ApiKey-v1";

        /// <summary>
        /// Fixed application entropy derived from SHA-256 of <see cref="EntropySource"/>.
        /// Ensures that only AKML SQL can decrypt the blob (in addition to the user-scoped DPAPI key).
        /// </summary>
        private static readonly byte[] AppEntropy = ComputeEntropy();

        // netstandard2.0-safe: SHA256.HashData is .NET5+, so hash via an instance.
        private static byte[] ComputeEntropy()
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(EntropySource));
        }

        /// <summary>
        /// Encrypts a plaintext string using DPAPI with <see cref="DataProtectionScope.CurrentUser"/>
        /// and the application-specific entropy.
        /// </summary>
        /// <param name="plainText">The plaintext value to encrypt (e.g., an API key).</param>
        /// <returns>
        /// A string in the format <c>dpapi:&lt;base64&gt;</c>. Returns an empty string if
        /// <paramref name="plainText"/> is <c>null</c> or empty.
        /// </returns>
        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText!);
                try
                {
                    var cipher = ProtectedData.Protect(plainBytes, AppEntropy, DataProtectionScope.CurrentUser);
                    return EncryptedPrefix + Convert.ToBase64String(cipher);
                }
                finally
                {
                    Array.Clear(plainBytes, 0, plainBytes.Length); // portable across netstandard2.0 + net10
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ApiKeyProtector: DPAPI encryption failed");
                throw;
            }
        }

        /// <summary>
        /// Decrypts a DPAPI-encrypted string. If the value does not start with the
        /// <c>dpapi:</c> prefix, it is returned as-is (legacy plaintext fallback).
        /// </summary>
        /// <param name="encrypted">
        /// The encrypted string (with <c>dpapi:</c> prefix) or a legacy plaintext value.
        /// </param>
        /// <returns>The decrypted plaintext string.</returns>
        public static string Unprotect(string? encrypted)
        {
            if (string.IsNullOrEmpty(encrypted))
                return string.Empty;

            if (!IsProtected(encrypted))
                return encrypted!;

            try
            {
                var base64 = encrypted!.Substring(EncryptedPrefix.Length);
                var cipher = Convert.FromBase64String(base64);
                var plainBytes = ProtectedData.Unprotect(cipher, AppEntropy, DataProtectionScope.CurrentUser);

                try
                {
                    return Encoding.UTF8.GetString(plainBytes);
                }
                finally
                {
                    // Zero the plaintext byte array to minimize exposure in memory
                    Array.Clear(plainBytes, 0, plainBytes.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ApiKeyProtector: DPAPI decryption failed");
                throw;
            }
        }

        /// <summary>
        /// Checks whether the given value is DPAPI-encrypted (starts with <c>dpapi:</c>).
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns><c>true</c> if the value has the DPAPI prefix; otherwise <c>false</c>.</returns>
        public static bool IsProtected(string? value)
        {
            return value != null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
        }
    }
}
