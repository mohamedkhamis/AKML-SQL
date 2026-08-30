using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AkmlSql.Site.Admin;

/// <summary>Configuration binding for the <c>Admin</c> section of appsettings.json.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// PBKDF2 password hash in the <c>AKML1$iterations$salt$hash</c> format produced by
    /// <see cref="AdminAuth.HashPassword"/>. EMPTY in the repo — the real value is supplied on the
    /// server as the <c>Admin__PasswordHash</c> environment variable (see OPS-001/OPS-002: a file
    /// under the deploy path is erased by the robocopy mirror, an app-pool variable is not).
    /// When empty/invalid the portal renders a "not configured" notice and no password can sign in.
    /// </summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>True only when the configured value parses as a well-formed PBKDF2 hash.</summary>
    public bool IsConfigured => AdminAuth.TryParseHash(PasswordHash, out _, out _, out _);
}

/// <summary>
/// Password verification for the single shared admin password.
/// <para>
/// The hash is PBKDF2-HMAC-SHA256 with a per-password random salt (SEC-001 — the previous
/// single-round unsalted SHA-256 was GPU-trivial to brute force offline if the configured value
/// ever leaked). Implemented directly on <see cref="Rfc2898DeriveBytes"/> rather than pulling in
/// ASP.NET Core Identity for one call: no new package, and the stored format is self-describing.
/// </para>
/// <para>
/// Format: <c>AKML1$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>. The version
/// prefix and inline iteration count mean the work factor can be raised later without invalidating
/// existing hashes.
/// </para>
/// </summary>
public static class AdminAuth
{
    /// <summary>Authentication scheme name for the admin cookie.</summary>
    public const string Scheme = "AdminCookie";

    /// <summary>Format marker for version 1 hashes (PBKDF2-HMAC-SHA256).</summary>
    public const string HashPrefix = "AKML1";

    /// <summary>Work factor for newly generated hashes (OWASP guidance for PBKDF2-HMAC-SHA256).</summary>
    public const int DefaultIterations = 210_000;

    /// <summary>Lowest iteration count accepted when verifying — rejects a downgraded stored hash.</summary>
    public const int MinimumIterations = 100_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>
    /// Produces a storable hash for <paramref name="password"/> with a fresh random salt. The
    /// output goes into the <c>Admin__PasswordHash</c> environment variable on the server; generate
    /// it with <c>AkmlSql.Site --hash-password</c> so the same code path always produces and
    /// verifies it.
    /// </summary>
    public static string HashPassword(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, MinimumIterations);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, iterations);

        return string.Join('$',
            HashPrefix,
            iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>
    /// Verifies a login attempt against the configured hash using a fixed-time comparison on the
    /// raw digests. An empty/malformed configured value always fails.
    /// </summary>
    public static bool Verify(string? password, string? configuredHash)
    {
        if (string.IsNullOrEmpty(password) || !TryParseHash(configuredHash, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Derive(password, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Parses a stored <c>AKML1$iterations$salt$hash</c> value. False for null, the wrong prefix, a
    /// missing/non-numeric or below-minimum iteration count, non-base64 parts, or wrong-length
    /// salt/hash — every malformed shape collapses to "not configured" rather than throwing.
    /// </summary>
    internal static bool TryParseHash(string? value, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], HashPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations < MinimumIterations)
        {
            return false;
        }

        if (!TryDecodeBase64(parts[2], SaltBytes, out salt) || !TryDecodeBase64(parts[3], HashBytes, out hash))
        {
            return false;
        }

        return true;
    }

    private static bool TryDecodeBase64(string value, int expectedLength, out byte[] bytes)
    {
        bytes = new byte[expectedLength];
        return Convert.TryFromBase64String(value, bytes, out var written) && written == expectedLength;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashBytes);
}
