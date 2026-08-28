using System.Security.Cryptography;
using System.Text;

namespace AkmlSql.Site.Admin;

/// <summary>Configuration binding for the <c>Admin</c> section of appsettings.json.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// SHA-256 hex (lowercase, 64 chars) of the shared admin password. EMPTY in the repo —
    /// the real hash is set in appsettings.Production.json on the server. When empty/invalid the
    /// portal renders a "not configured" notice and no password can ever sign in.
    /// </summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>True only when the configured hash is a well-formed 64-char SHA-256 hex string.</summary>
    public bool IsConfigured => AdminAuth.TryDecodeHash(PasswordHash, out _);
}

/// <summary>Password-hash verification for the single shared admin password.</summary>
public static class AdminAuth
{
    /// <summary>Authentication scheme name for the admin cookie.</summary>
    public const string Scheme = "AdminCookie";

    /// <summary>SHA-256 of a password, lowercase hex — the value the owner puts in server config.</summary>
    public static string ComputeSha256Hex(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    /// <summary>
    /// Verifies a login attempt against the configured hash using a fixed-time comparison on the
    /// raw digests (never on the hex strings). An empty/malformed configured hash always fails.
    /// </summary>
    public static bool Verify(string? password, string? configuredHashHex)
    {
        if (string.IsNullOrEmpty(password) || !TryDecodeHash(configuredHashHex, out var expected))
        {
            return false;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Decodes a 64-char SHA-256 hex string; false for null/wrong-length/non-hex input.</summary>
    internal static bool TryDecodeHash(string? hex, out byte[] bytes)
    {
        bytes = [];
        if (hex is not { Length: 64 })
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
