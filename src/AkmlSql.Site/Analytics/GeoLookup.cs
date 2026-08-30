using System.Net;
using MaxMind.GeoIP2;
using MaxMind.Db;
using MaxMind.GeoIP2.Exceptions;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// Location resolved from a client IP — country only, by choice.
/// <para>
/// City, region and timezone are deliberately not collected. Country answers the questions a
/// product site actually has (where is the audience, is translation worth it, which timezone to
/// release in) while city-level data narrows a visitor far more than that needs, and the least
/// risky way to hold data you do not need is not to hold it. The Country edition of the database
/// does not even contain the finer fields, so this is enforced at the source rather than by
/// remembering not to read them.
/// </para>
/// </summary>
/// <param name="CountryCode">ISO 3166-1 alpha-2 ("EG", "GB"), or null.</param>
/// <param name="CountryName">English country name, or null.</param>
public sealed record GeoLocation(string? CountryCode, string? CountryName)
{
    /// <summary>The all-null result used when no database is loaded or the IP is not found.</summary>
    public static readonly GeoLocation Unknown = new(null, null);
}

/// <summary>
/// Offline IP-to-location lookup over a MaxMind GeoLite2 <c>.mmdb</c> file.
/// <para>
/// Offline on purpose: a per-request call to a third-party geo API would put visitor IP
/// addresses in someone else's logs and a network hop on the request path. The database is read
/// from disk once at startup and queried in memory.
/// </para>
/// <para>
/// The file is NOT in source control — GeoLite2 needs a MaxMind licence key (see
/// scripts/update-geoip.ps1). When it is missing, unreadable or stale, every lookup returns
/// <see cref="GeoLocation.Unknown"/> and the site runs exactly as before: geo is an enrichment,
/// never a dependency.
/// </para>
/// <para>
/// The lookup receives the FULL client IP, because a truncated prefix resolves poorly. Only the
/// truncated form and the derived location are ever stored.
/// </para>
/// </summary>
public sealed class GeoLookup : IDisposable
{
    private readonly DatabaseReader? _reader;

    /// <summary>Default file name and location when <c>Analytics:GeoDatabasePath</c> is empty.</summary>
    public const string DefaultFileName = "GeoLite2-Country.mmdb";

    /// <summary>Opens the database at <paramref name="databasePath"/>, or stays inert if absent.</summary>
    public GeoLookup(string? databasePath, ILogger<GeoLookup>? logger = null)
    {
        var resolved = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AKML SQL Site",
                DefaultFileName)
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(databasePath));
        DatabasePath = resolved;

        if (!File.Exists(resolved))
        {
            logger?.LogInformation(
                "Geo database not configured or not found ({Path}) — visits will be recorded without location.",
                resolved);
            return;
        }

        try
        {
            _reader = new DatabaseReader(DatabasePath);
            logger?.LogInformation(
                "Geo database loaded: {Type}, built {Built:yyyy-MM-dd}.",
                _reader.Metadata.DatabaseType, _reader.Metadata.BuildDate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDatabaseException)
        {
            // A corrupt or half-downloaded file must not stop the site from serving.
            logger?.LogWarning(ex, "Geo database at {Path} could not be opened — continuing without location.", DatabasePath);
            _reader = null;
        }
    }

    /// <summary>Resolved absolute path the database is expected at (it may not exist).</summary>
    public string DatabasePath { get; }

    /// <summary>True when a database is loaded and lookups can return a location.</summary>
    public bool IsAvailable => _reader is not null;

    /// <summary>
    /// Locates <paramref name="ipAddress"/>. Returns <see cref="GeoLocation.Unknown"/> for a
    /// missing database, an unparseable address, a private/loopback address, or an IP the
    /// database does not know.
    /// </summary>
    public GeoLocation Locate(string? ipAddress)
    {
        if (_reader is null || !IPAddress.TryParse(ipAddress, out var parsed))
        {
            return GeoLocation.Unknown;
        }

        // Loopback and RFC1918/ULA addresses are never in the database; skip the lookup so the
        // logs are not full of expected misses when testing locally.
        if (IPAddress.IsLoopback(parsed) || IsPrivate(parsed))
        {
            return GeoLocation.Unknown;
        }

        try
        {
            // TryCountry only. A City-edition database would also answer this call, so pointing
            // GeoDatabasePath at one still yields country and nothing finer.
            if (_reader.TryCountry(parsed, out var country) && country is not null)
            {
                return new GeoLocation(country.Country.IsoCode, country.Country.Name);
            }
        }
        catch (Exception ex) when (ex is GeoIP2Exception or InvalidDatabaseException)
        {
            // A lookup failure is not worth losing the visit record over.
            return GeoLocation.Unknown;
        }

        return GeoLocation.Unknown;
    }

    /// <summary>RFC1918 / RFC4193 / link-local ranges, which no geo database covers.</summary>
    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length switch
        {
            4 => bytes[0] == 10
                 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                 || (bytes[0] == 192 && bytes[1] == 168)
                 || (bytes[0] == 169 && bytes[1] == 254),
            // fc00::/7 unique-local, fe80::/10 link-local.
            16 => (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80),
            _ => false,
        };
    }

    public void Dispose() => _reader?.Dispose();
}
