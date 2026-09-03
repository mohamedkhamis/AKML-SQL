using AkmlSql.Core.Config;

namespace AkmlSql.Engine.Ai.Security;

/// <summary>
/// Thin forwarder over <see cref="ApiKeyProtector"/> (spec 036 US2, FR-008): the DPAPI
/// wrap/unwrap moved to AkmlSql.Core so the net472 Options page shares exactly one mechanism
/// with the engine. The entropy (<c>"AkmlSql-ApiKey-v1"</c>) lives there — changing it makes
/// every already-stored key unreadable. Existing engine call sites keep these method names.
/// </summary>
public static class CredentialManager
{
    /// <summary>Encrypts a plaintext API key to a <c>dpapi:&lt;base64&gt;</c> string.</summary>
    public static string Encrypt(string? plainText) => ApiKeyProtector.Protect(plainText);

    /// <summary>Decrypts a <c>dpapi:</c>-prefixed value; legacy plaintext passes through unchanged.</summary>
    public static string Decrypt(string? encrypted) => ApiKeyProtector.Unprotect(encrypted);

    /// <summary>Checks whether the given value is DPAPI-encrypted (starts with <c>dpapi:</c>).</summary>
    public static bool IsEncrypted(string? value) => ApiKeyProtector.IsProtected(value);
}
