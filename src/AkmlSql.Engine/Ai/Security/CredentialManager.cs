#nullable enable
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace AkmlSql.Engine.Ai.Security;

/// <summary>
/// Provides DPAPI-based encryption and decryption for API keys and other secrets.
/// <para>
/// Encrypted values are stored with a <c>dpapi:</c> prefix followed by a Base64-encoded
/// DPAPI blob. Values without the prefix are returned as-is for backward compatibility
/// during migration from plaintext config.
/// </para>
/// </summary>
public static class CredentialManager
{
    private const string EncryptedPrefix = "dpapi:";

    /// <summary>
    /// Fixed application entropy derived from SHA-256 of <c>"AkmlSql-ApiKey-v1"</c>.
    /// Ensures that only AKML SQL can decrypt the blob (in addition to the user-scoped DPAPI key).
    /// </summary>
    private static readonly byte[] AppEntropy = SHA256.HashData(Encoding.UTF8.GetBytes("AkmlSql-ApiKey-v1"));

    /// <summary>
    /// Encrypts a plaintext string using DPAPI with <see cref="DataProtectionScope.CurrentUser"/>
    /// and the application-specific entropy.
    /// </summary>
    /// <param name="plainText">The plaintext value to encrypt (e.g., an API key).</param>
    /// <returns>
    /// A string in the format <c>dpapi:&lt;base64&gt;</c>. Returns an empty string if
    /// <paramref name="plainText"/> is <c>null</c> or empty.
    /// </returns>
    public static string Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipher = ProtectedData.Protect(plainBytes, AppEntropy, DataProtectionScope.CurrentUser);
            return EncryptedPrefix + Convert.ToBase64String(cipher);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CredentialManager: DPAPI encryption failed");
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
    public static string Decrypt(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return string.Empty;

        if (!IsEncrypted(encrypted))
            return encrypted;

        try
        {
            var base64 = encrypted.Substring(EncryptedPrefix.Length);
            var cipher = Convert.FromBase64String(base64);
            var plainBytes = ProtectedData.Unprotect(cipher, AppEntropy, DataProtectionScope.CurrentUser);

            try
            {
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                // Zero the plaintext byte array to minimize exposure in memory
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CredentialManager: DPAPI decryption failed");
            throw;
        }
    }

    /// <summary>
    /// Checks whether the given value is DPAPI-encrypted (starts with <c>dpapi:</c>).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns><c>true</c> if the value has the DPAPI prefix; otherwise <c>false</c>.</returns>
    public static bool IsEncrypted(string? value)
    {
        return value != null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
    }
}
