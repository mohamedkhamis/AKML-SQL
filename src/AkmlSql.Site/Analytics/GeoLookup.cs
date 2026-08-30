using System.Net;
using MaxMind.GeoIP2;
using MaxMind.Db;
using MaxMind.GeoIP2.Exceptions;

namespace AkmlSql.Site.Analytics;

/// <summary>Location facts resolved from a client IP. Every field is optional.</summary>
/// <param name="CountryCode">ISO 3166-1 alpha-2 ("EG", "GB"), or null.</param>
/// <param name="CountryName">English country name, or null.</param>
/// <param name="Region">Most specific subdivision (state/governorate), or null.</param>
/// <param name="City">City name, or null.</param>
/// <param name="TimeZone">IANA zone ("Africa/Cairo"), or null. Comes from the location, so no
/// client-side clock reading is needed.</param>
public sealed record GeoLocation(
    string? CountryCode,
    string? CountryName,
    string? Region,
    string? City,
    string? TimeZone)
{
    /// <summary>The all-null result used when no database is loaded or the IP is not found.</summary>
    public static readonly GeoLocation Unknown = new(null, null, null, null, null);
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
    private readonly bool _hasCityData;

    /// <summary>Default file name and location when <c>Analytics:GeoDatabasePath</c> is empty.</summary>
    public const string DefaultFileName = "GeoLite2-City.mmdb";

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
            // GeoLite2-City carries subdivisions and a time zone; GeoLite2-Country does not.
            _hasCityData = _reader.Metadata.DatabaseType.Contains("City", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>True when the loaded database carries city/subdivision/timezone detail.</summary>
    public bool HasCityData => _hasCityData;

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
            if (_hasCityData && _reader.TryCity(parsed, out var city) && city is not null)
            {
                return new GeoLocation(
                    city.Country.IsoCode,
                    city.Country.Name,
                    city.MostSpecificSubdivision.Name,
                    city.City.Name,
                    city.Location.TimeZone);
            }

            if (_reader.TryCountry(parsed, out var country) && country is not null)
            {
                return new GeoLocation(country.Country.IsoCode, country.Country.Name, null, null, null);
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
