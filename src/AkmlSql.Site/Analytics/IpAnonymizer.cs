using System.Net;
using System.Net.Sockets;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// Reduces a client IP to a network prefix before storage.
/// <para>
/// The store still writes the per-day salted hash (which supports unique-visitor counting), and
/// now also keeps a TRUNCATED address so traffic can be analysed by network and resolved to a
/// country. IPv4 keeps the first three octets (/24), IPv6 the first 48 bits (/48) — the same
/// reduction Matomo and Google Analytics' IP anonymisation apply. That is coarse enough that the
/// stored value does not identify a household, and precise enough for country/region geo and for
/// spotting a single noisy network.
/// </para>
/// <para>
/// The full address is still used in-process (for the hash and for the geo lookup) and is never
/// persisted.
/// </para>
/// </summary>
public static class IpAnonymizer
{
    /// <summary>Bits kept for IPv4 (a /24 network).</summary>
    public const int IPv4PrefixBits = 24;

    /// <summary>Bits kept for IPv6 (a /48 network, the usual site allocation).</summary>
    public const int IPv6PrefixBits = 48;

    /// <summary>
    /// Network prefix of <paramref name="ipAddress"/> in canonical form ("203.0.113.0",
    /// "2001:db8:abcd::"), or null when the value is missing or unparseable.
    /// </summary>
    public static string? ToPrefix(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var parsed))
        {
            return null;
        }

        // An IPv4-mapped IPv6 address (::ffff:203.0.113.7, which is what Kestrel reports for an
        // IPv4 client on a dual-stack socket) must be truncated as IPv4, not as IPv6.
        if (parsed.IsIPv4MappedToIPv6)
        {
            parsed = parsed.MapToIPv4();
        }

        var bytes = parsed.GetAddressBytes();
        var keepBits = parsed.AddressFamily == AddressFamily.InterNetwork ? IPv4PrefixBits : IPv6PrefixBits;

        MaskInPlace(bytes, keepBits);
        return new IPAddress(bytes).ToString();
    }

    /// <summary>Zeroes every bit after the first <paramref name="keepBits"/>.</summary>
    private static void MaskInPlace(byte[] bytes, int keepBits)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            var bitOffset = i * 8;
            if (bitOffset >= keepBits)
            {
                bytes[i] = 0;
                continue;
            }

            var remaining = keepBits - bitOffset;
            if (remaining < 8)
            {
                // Partial byte: keep the top `remaining` bits.
                bytes[i] &= (byte)(0xFF << (8 - remaining));
            }
        }
    }
}
