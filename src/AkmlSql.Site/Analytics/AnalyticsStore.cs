using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AkmlSql.Site.Analytics;

/// <summary>Configuration binding for the <c>Analytics</c> section of appsettings.json.</summary>
public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    /// <summary>
    /// SQLite database path. Empty → <c>%ProgramData%\AKML SQL Site\analytics.db</c>.
    /// Environment variables are expanded. The containing directory is auto-created; on IIS the
    /// deploy step must grant the app pool identity write access to it.
    /// </summary>
    public string DatabasePath { get; set; } = "";

    /// <summary>
    /// ADM-004: days of history to keep. Rows older than this are pruned at startup — the tables
    /// previously grew without bound. 0 disables pruning (keep everything).
    /// </summary>
    public int RetentionDays { get; set; } = 400;

    /// <summary>
    /// Path to a MaxMind GeoLite2 <c>.mmdb</c> file for country/region lookup. Empty →
    /// <c>%ProgramData%\AKML SQL Site\GeoLite2-City.mmdb</c>. The file is not in source control
    /// (it needs a MaxMind licence key — see scripts/update-geoip.ps1); when it is absent,
    /// visits are recorded without location and nothing else changes.
    /// </summary>
    public string GeoDatabasePath { get; set; } = "";
}

/// <summary>
/// SQLite-backed store for page-visit and installer-download metrics (plain ADO.NET, no EF Core).
/// One shared connection guarded by a lock — writes arrive from a single background consumer and
/// reads from the /admin dashboard, so contention is negligible. Parameterized commands only.
/// <para>
/// Privacy: the raw client IP is never persisted. The store writes
/// <c>SHA256(ip | utc-date | per-install salt)</c>, where the salt is 32 random bytes generated
/// once and kept next to the database in <c>salt.bin</c> — per-day hashes allow unique-visitor
/// counting within a day without making IPs linkable across days or recoverable at rest.
/// </para>
/// </summary>
public sealed class AnalyticsStore : IDisposable
{
    /// <summary>File name of the metrics database.</summary>
    public const string DatabaseFileName = "analytics.db";

    /// <summary>File name of the per-install salt, stored next to the database.</summary>
    public const string SaltFileName = "salt.bin";

    /// <summary>Display folder under ProgramData when no path is configured.</summary>
    private const string DefaultFolderName = "AKML SQL Site";

    private const int TopRowLimit = 10;

    private readonly object _gate = new();
    private readonly SqliteConnection _connection;
    private readonly byte[] _salt;

    public AnalyticsStore(AnalyticsOptions options)
        : this(options?.DatabasePath)
    {
    }

    /// <summary>Opens (creating if needed) the database at the configured/default path.</summary>
    public AnalyticsStore(string? databasePath)
    {
        DatabasePath = ResolveDatabasePath(databasePath);
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            // The IIS deploy step ACLs this folder for the app pool identity; creating it here
            // keeps first-run/dev boxes working without manual setup.
            Directory.CreateDirectory(directory);
        }

        _salt = LoadOrCreateSalt(Path.Combine(directory ?? ".", SaltFileName));

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        _connection = new SqliteConnection(connectionString.ConnectionString);
        _connection.Open();
        InitializeSchema();
    }

    /// <summary>Resolved absolute database path.</summary>
    public string DatabasePath { get; }

    /// <summary>Resolves the configured path (env-var expanded) or the ProgramData default.</summary>
    public static string ResolveDatabasePath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // == %ProgramData%
            DefaultFolderName,
            DatabaseFileName);
    }

    /// <summary>
    /// Per-day salted IP hash — the only client-IP-derived value ever stored. Public so the
    /// privacy behavior (salt persistence across restarts) is directly testable.
    /// </summary>
    public string ComputeIpHash(string? ipAddress, DateOnly utcDate)
    {
        var prefix = Encoding.UTF8.GetBytes(string.Concat(
            ipAddress ?? "",
            "|",
            utcDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "|"));
        var buffer = new byte[prefix.Length + _salt.Length];
        prefix.CopyTo(buffer, 0);
        _salt.CopyTo(buffer, prefix.Length);
        return Convert.ToHexStringLower(SHA256.HashData(buffer));
    }

    /// <summary>Idle gap that ends a session (the analytics convention).</summary>
    public const int SessionIdleMinutes = 30;

    /// <summary>Records one page view, assigning it to a session.</summary>
    public void LogVisit(VisitInfo visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentException.ThrowIfNullOrWhiteSpace(visit.Path);

        var day = DateOnly.FromDateTime(visit.Utc.UtcDateTime);
        var hash = ComputeIpHash(visit.IpAddress, day);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO visits (utc, day, path, referrer_host, ua_family, ip_hash, " +
                "ip_prefix, country_code, country, region, city, time_zone, " +
                "device, os_family, os_version, browser_version, language, referrer_url, " +
                "utm_source, utm_medium, utm_campaign, utm_term, utm_content, session_id, duration_ms) " +
                "VALUES ($utc, $day, $path, $referrer, $ua, $hash, " +
                "$prefix, $countryCode, $country, $region, $city, $timeZone, " +
                "$device, $osFamily, $osVersion, $browserVersion, $language, $referrerUrl, " +
                "$utmSource, $utmMedium, $utmCampaign, $utmTerm, $utmContent, $session, $duration);";

            command.Parameters.AddWithValue("$utc", FormatUtc(visit.Utc));
            command.Parameters.AddWithValue("$day", FormatDay(day));
            command.Parameters.AddWithValue("$path", visit.Path);
            command.Parameters.AddWithValue("$referrer", (object?)visit.ReferrerHost ?? DBNull.Value);
            command.Parameters.AddWithValue("$ua", (object?)visit.UaFamily ?? DBNull.Value);
            command.Parameters.AddWithValue("$hash", hash);
            // The full address is used above for the hash and by the caller for geo; only the
            // network prefix is stored.
            command.Parameters.AddWithValue("$prefix", (object?)IpAnonymizer.ToPrefix(visit.IpAddress) ?? DBNull.Value);
            command.Parameters.AddWithValue("$countryCode", (object?)visit.Location.CountryCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$country", (object?)visit.Location.CountryName ?? DBNull.Value);
            command.Parameters.AddWithValue("$region", (object?)visit.Location.Region ?? DBNull.Value);
            command.Parameters.AddWithValue("$city", (object?)visit.Location.City ?? DBNull.Value);
            command.Parameters.AddWithValue("$timeZone", (object?)visit.Location.TimeZone ?? DBNull.Value);
            command.Parameters.AddWithValue("$device", (object?)visit.UserAgent.Device ?? DBNull.Value);
            command.Parameters.AddWithValue("$osFamily", (object?)visit.UserAgent.Os ?? DBNull.Value);
            command.Parameters.AddWithValue("$osVersion", (object?)visit.UserAgent.OsVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("$browserVersion", (object?)visit.UserAgent.BrowserVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("$language", (object?)visit.Language ?? DBNull.Value);
            command.Parameters.AddWithValue("$referrerUrl", (object?)visit.ReferrerUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$utmSource", (object?)visit.Campaign.Source ?? DBNull.Value);
            command.Parameters.AddWithValue("$utmMedium", (object?)visit.Campaign.Medium ?? DBNull.Value);
            command.Parameters.AddWithValue("$utmCampaign", (object?)visit.Campaign.Campaign ?? DBNull.Value);
            command.Parameters.AddWithValue("$utmTerm", (object?)visit.Campaign.Term ?? DBNull.Value);
            command.Parameters.AddWithValue("$utmContent", (object?)visit.Campaign.Content ?? DBNull.Value);
            command.Parameters.AddWithValue("$session", ResolveSessionId(hash, visit.Utc, visit.UaFamily));
            command.Parameters.AddWithValue("$duration", (object?)visit.DurationMs ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Session id for a visitor: the id of their most recent visit when it was less than
    /// <see cref="SessionIdleMinutes"/> ago, otherwise a new one.
    /// <para>
    /// Keyed on the per-day IP hash AND the user-agent family. The IP alone merges everyone
    /// behind one NAT — an office, a household, a phone on carrier-grade NAT — into a single
    /// session, which silently inflates pages-per-session and deflates the session count. Adding
    /// the agent separates the obvious case (a phone and a laptop on the same connection) at no
    /// extra cost. Two identical browsers behind one NAT still merge; without a cookie there is
    /// no honest way to split them, and a cookie is exactly what this design avoids.
    /// </para>
    /// <para>
    /// A session cannot span midnight UTC either, because the salt is re-mixed daily by design.
    /// Sessions describe behaviour within a visit; they are not a way to follow people over time.
    /// </para>
    /// Caller must hold <c>_gate</c>.
    /// </summary>
    private string ResolveSessionId(string ipHash, DateTimeOffset now, string? uaFamily)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT session_id, utc FROM visits " +
            "WHERE ip_hash = $hash AND session_id IS NOT NULL " +
            "  AND (ua_family IS $ua OR ua_family = $ua) " +
            "ORDER BY utc DESC LIMIT 1;";
        command.Parameters.AddWithValue("$hash", ipHash);
        command.Parameters.AddWithValue("$ua", (object?)uaFamily ?? DBNull.Value);

        using (var reader = command.ExecuteReader())
        {
            if (reader.Read()
                && DateTimeOffset.TryParse(
                    reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var previous)
                && now - previous < TimeSpan.FromMinutes(SessionIdleMinutes))
            {
                return reader.GetString(0);
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    /// <summary>Records one installer download.</summary>
    public void LogDownload(DownloadInfo download)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentException.ThrowIfNullOrWhiteSpace(download.File);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            var day = DateOnly.FromDateTime(download.Utc.UtcDateTime);
            var hash = ComputeIpHash(download.IpAddress, day);

            command.CommandText =
                "INSERT INTO downloads (utc, day, file, referrer_host, ua_family, ip_hash, " +
                "ip_prefix, country_code, country, device, os_family, browser_version, " +
                "language, referrer_url, utm_source, utm_medium, utm_campaign, session_id) " +
                "VALUES ($utc, $day, $file, $referrer, $ua, $hash, " +
                "$prefix, $countryCode, $country, $device, $osFamily, $browserVersion, " +
                "$language, $referrerUrl, $utmSource, $utmMedium, $utmCampaign, $session);";
            command.Parameters.AddWithValue("$utc", FormatUtc(download.Utc));
            command.Parameters.AddWithValue("$day", FormatDay(day));
            command.Parameters.AddWithValue("$file", download.File);
            command.Parameters.AddWithValue("$referrer", (object?)download.ReferrerHost ?? DBNull.Value);
            command.Parameters.AddWithValue("$ua", (object?)download.UaFamily ?? DBNull.Value);
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$prefix", (object?)IpAnonymizer.ToPrefix(download.IpAddress) ?? DBNull.Value);
            command.Parameters.AddWithValue("$countryCode", (object?)download.Location.CountryCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$country", (object?)download.Location.CountryName ?? DBNull.Value);
            command.Parameters.AddWithValue("$device", (object?)download.UserAgent.Device ?? DBNull.Value);
            command.Parameters.AddWithValue("$osFamily", (object?)download.UserAgent.Os ?? DBNull.Value);
            command.Parameters.AddWithValue("$browserVersion", (object?)download.UserAgent.BrowserVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("$language", (object?)download.Language ?? DBNull.Value);
            command.Parameters.AddWithValue("$referrerUrl", (object?)download.ReferrerUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$utmSource", (object?)download.Campaign.Source ?? DBNull.Value);
            command.Parameters.AddWithValue("$utmMedium", (object?)download.Campaign.Medium ?? DBNull.Value);
            command.Parameters.AddWithValue("$utmCampaign", (object?)download.Campaign.Campaign ?? DBNull.Value);
            // Ties the install back to the browsing session that led to it.
            command.Parameters.AddWithValue("$session", ResolveSessionId(hash, download.Utc, download.UaFamily));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// ADM-008: records one 404. Nothing IP-derived is stored — a missing page is a content
    /// problem, so the useful facts are the path and where the link came from.
    /// </summary>
    public void LogNotFound(NotFoundInfo notFound)
    {
        ArgumentNullException.ThrowIfNull(notFound);
        ArgumentException.ThrowIfNullOrWhiteSpace(notFound.Path);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO not_found (utc, day, path, referrer_host) VALUES ($utc, $day, $path, $referrer);";
            command.Parameters.AddWithValue("$utc", FormatUtc(notFound.Utc));
            command.Parameters.AddWithValue("$day", FormatDay(DateOnly.FromDateTime(notFound.Utc.UtcDateTime)));
            command.Parameters.AddWithValue("$path", notFound.Path);
            command.Parameters.AddWithValue("$referrer", (object?)notFound.ReferrerHost ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Dashboard summary anchored at the current UTC instant.</summary>
    public AnalyticsSummary GetSummary(int days) => GetSummary(days, DateTimeOffset.UtcNow);

    /// <summary>Dashboard summary anchored at <paramref name="now"/> (injectable for tests).</summary>
    public AnalyticsSummary GetSummary(int days, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var sinceWindow = today.AddDays(-(days - 1));
        var since7 = today.AddDays(-6);

        lock (_gate)
        {
            var sessionStats = QuerySessionStats(sinceWindow);

            return new AnalyticsSummary
            {
                Days = days,
                // ADM-001: every visitor-facing figure excludes crawlers. Counting them made the
                // headline numbers, the daily chart and the top-pages table meaningless.
                VisitsToday = CountRows("visits", $"day = $day AND {HumanOnly}", ("$day", FormatDay(today))),
                VisitsLast7Days = CountRows("visits", $"day >= $day AND {HumanOnly}", ("$day", FormatDay(since7))),
                VisitsWindow = CountRows("visits", $"day >= $day AND {HumanOnly}", ("$day", FormatDay(sinceWindow))),
                BotVisitsWindow = CountRows("visits", $"day >= $day AND NOT ({HumanOnly})", ("$day", FormatDay(sinceWindow))),
                DownloadsTotal = CountRows("downloads", $"{HumanOnly}", null),
                DownloadsLast7Days = CountRows("downloads", $"day >= $day AND {HumanOnly}", ("$day", FormatDay(since7))),
                DownloadsWindow = CountRows("downloads", $"day >= $day AND {HumanOnly}", ("$day", FormatDay(sinceWindow))),
                UniqueVisitorsToday = CountRows("visits", $"day = $day AND {HumanOnly}", ("$day", FormatDay(today)), distinctColumn: "ip_hash"),
                TopPages = QueryCountRows(
                    $"SELECT path, COUNT(*) FROM visits WHERE day >= $day AND {HumanOnly} " +
                    "GROUP BY path ORDER BY COUNT(*) DESC, path LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
                DownloadsByFile = QueryCountRows(
                    $"SELECT file, COUNT(*) FROM downloads WHERE {HumanOnly} " +
                    "GROUP BY file ORDER BY COUNT(*) DESC, file LIMIT $limit;",
                    null),
                // ADM-002: the browser mix was recorded on every row and never displayed.
                BrowserMix = QueryCountRows(
                    $"SELECT COALESCE(ua_family, 'other'), COUNT(*) FROM visits WHERE day >= $day AND {HumanOnly} " +
                    "GROUP BY COALESCE(ua_family, 'other') ORDER BY COUNT(*) DESC, 1 LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
                DailyVisits = QueryDailySeries("visits", sinceWindow, today, distinctColumn: null),
                DailyUniqueVisitors = QueryDailySeries("visits", sinceWindow, today, distinctColumn: "ip_hash"),
                // ADM-005: downloads had only two scalars; conversion over time is the metric a
                // product owner actually watches.
                DailyDownloads = QueryDailySeries("downloads", sinceWindow, today, distinctColumn: null),
                TopReferrers = QueryCountRows(
                    "SELECT referrer_host, COUNT(*) FROM visits " +
                    $"WHERE referrer_host IS NOT NULL AND referrer_host <> '' AND day >= $day AND {HumanOnly} " +
                    "GROUP BY referrer_host ORDER BY COUNT(*) DESC, referrer_host LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
                // ADM-008: broken inbound links, invisible while only 2xx responses were tracked.
                TopNotFound = QueryCountRows(
                    "SELECT path, COUNT(*) FROM not_found WHERE day >= $day " +
                    "GROUP BY path ORDER BY COUNT(*) DESC, path LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),

                // --- Enrichment dimensions. Each ignores rows that lack the value, so history
                // written before a column existed (or while the geo database was absent) simply
                // does not appear rather than showing up as a bogus "unknown" bucket.
                Countries = TopBy("country", sinceWindow),
                Cities = QueryCountRows(
                    "SELECT COALESCE(city || ', ' || region, city, region), COUNT(*) FROM visits " +
                    $"WHERE day >= $day AND {HumanOnly} AND (city IS NOT NULL OR region IS NOT NULL) " +
                    "GROUP BY 1 ORDER BY COUNT(*) DESC, 1 LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
                Devices = TopBy("device", sinceWindow),
                OperatingSystems = QueryCountRows(
                    "SELECT os_family || COALESCE(' ' || os_version, ''), COUNT(*) FROM visits " +
                    $"WHERE day >= $day AND {HumanOnly} AND os_family IS NOT NULL " +
                    "GROUP BY 1 ORDER BY COUNT(*) DESC, 1 LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
                Languages = TopBy("language", sinceWindow),
                Campaigns = QueryCountRows(
                    "SELECT COALESCE(utm_campaign, utm_source, utm_medium) || " +
                    "       COALESCE(' / ' || utm_medium, ''), COUNT(*) FROM visits " +
                    $"WHERE day >= $day AND {HumanOnly} " +
                    "  AND (utm_campaign IS NOT NULL OR utm_source IS NOT NULL OR utm_medium IS NOT NULL) " +
                    "GROUP BY 1 ORDER BY COUNT(*) DESC, 1 LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
                ReferrerUrls = TopBy("referrer_url", sinceWindow),
                // Mean handling time per page. AVG returns a float; the read model carries longs,
                // so it is rounded to whole milliseconds — sub-millisecond precision is noise here.
                SlowestPages = QueryCountRows(
                    "SELECT path, CAST(ROUND(AVG(duration_ms)) AS INTEGER) FROM visits " +
                    $"WHERE day >= $day AND {HumanOnly} AND duration_ms IS NOT NULL " +
                    "GROUP BY path HAVING COUNT(*) >= 3 ORDER BY AVG(duration_ms) DESC, path LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
                EntryPages = QuerySessionEdgePages(sinceWindow, first: true),
                ExitPages = QuerySessionEdgePages(sinceWindow, first: false),
                Sessions = sessionStats.Sessions,
                BounceRatePercent = sessionStats.BounceRatePercent,
                PagesPerSession = sessionStats.PagesPerSession,
                AverageSessionSeconds = sessionStats.AverageSeconds,
            };
        }
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Loads the persisted salt, or generates and persists a fresh 32-byte one on first run.</summary>
    private static byte[] LoadOrCreateSalt(string saltPath)
    {
        if (File.Exists(saltPath))
        {
            var existing = File.ReadAllBytes(saltPath);
            if (existing.Length >= 16)
            {
                return existing;
            }
        }

        var salt = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(saltPath, salt);
        return salt;
    }

    /// <summary>
    /// Creates the schema and brings an existing database up to date.
    /// <para>
    /// ADM-004: every query filters on <c>substr(utc, 1, 10) &gt;= $day</c> — an expression over
    /// the column, which SQLite cannot serve from an index on <c>utc</c>, so <c>ix_visits_utc</c>
    /// was dead weight and every dashboard load full-scanned both tables. A stored <c>day</c>
    /// column carries the same value as a plain column and IS indexable. It is backfilled for
    /// existing rows, so upgrading an installed database needs no migration step.
    /// </para>
    /// </summary>
    private void InitializeSchema()
    {
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS visits (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    utc TEXT NOT NULL,
                    path TEXT NOT NULL,
                    referrer_host TEXT NULL,
                    ua_family TEXT NULL,
                    ip_hash TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS downloads (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    utc TEXT NOT NULL,
                    file TEXT NOT NULL,
                    referrer_host TEXT NULL,
                    ua_family TEXT NULL,
                    ip_hash TEXT NOT NULL
                );
                -- ADM-008: 404s. No ip_hash column: a missing page is a content problem, not a
                -- visitor measurement, so nothing here needs to identify who asked for it.
                CREATE TABLE IF NOT EXISTS not_found (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    utc TEXT NOT NULL,
                    day TEXT NULL,
                    path TEXT NOT NULL,
                    referrer_host TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        AddDayColumnIfMissing("visits");
        AddDayColumnIfMissing("downloads");

        // Enrichment columns. Added rather than baked into CREATE TABLE so an installed database
        // gains them in place — the deployed site has months of history and must not be reset.
        // Every one is nullable: rows written before a column existed simply have no value, and
        // rows written while the geo database is absent have no location.
        foreach (var column in EnrichmentColumns)
        {
            AddColumnIfMissing("visits", column);
        }

        // Downloads carry the same acquisition context, so "which campaign produced installs?"
        // is answerable without joining back to the visit.
        foreach (var column in DownloadEnrichmentColumns)
        {
            AddColumnIfMissing("downloads", column);
        }

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE visits SET day = substr(utc, 1, 10) WHERE day IS NULL;
                UPDATE downloads SET day = substr(utc, 1, 10) WHERE day IS NULL;
                UPDATE not_found SET day = substr(utc, 1, 10) WHERE day IS NULL;
                CREATE INDEX IF NOT EXISTS ix_not_found_day ON not_found (day);
                CREATE INDEX IF NOT EXISTS ix_visits_day ON visits (day);
                CREATE INDEX IF NOT EXISTS ix_downloads_day ON downloads (day);
                CREATE INDEX IF NOT EXISTS ix_visits_day_ua ON visits (day, ua_family);
                DROP INDEX IF EXISTS ix_visits_utc;
                DROP INDEX IF EXISTS ix_downloads_utc;
                """;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Enrichment columns on <c>visits</c>: network prefix, location, device/OS/browser detail,
    /// language, campaign, full referrer, session and timing.
    /// </summary>
    private static readonly (string Name, string Type)[] EnrichmentColumns =
    [
        ("ip_prefix", "TEXT"),        // truncated /24 or /48 — never the full address
        ("country_code", "TEXT"),
        ("country", "TEXT"),
        ("region", "TEXT"),
        ("city", "TEXT"),
        ("time_zone", "TEXT"),
        ("device", "TEXT"),           // desktop | mobile | tablet | bot
        ("os_family", "TEXT"),
        ("os_version", "TEXT"),
        ("browser_version", "TEXT"),
        ("language", "TEXT"),
        ("referrer_url", "TEXT"),
        ("utm_source", "TEXT"),
        ("utm_medium", "TEXT"),
        ("utm_campaign", "TEXT"),
        ("utm_term", "TEXT"),
        ("utm_content", "TEXT"),
        ("session_id", "TEXT"),
        ("duration_ms", "INTEGER"),
    ];

    /// <summary>Acquisition context mirrored onto <c>downloads</c>.</summary>
    private static readonly (string Name, string Type)[] DownloadEnrichmentColumns =
    [
        ("ip_prefix", "TEXT"),
        ("country_code", "TEXT"),
        ("country", "TEXT"),
        ("device", "TEXT"),
        ("os_family", "TEXT"),
        ("browser_version", "TEXT"),
        ("language", "TEXT"),
        ("referrer_url", "TEXT"),
        ("utm_source", "TEXT"),
        ("utm_medium", "TEXT"),
        ("utm_campaign", "TEXT"),
        ("session_id", "TEXT"),
    ];

    /// <summary>Adds the <c>day</c> column when an older database predates it.</summary>
    private void AddDayColumnIfMissing(string table) => AddColumnIfMissing(table, ("day", "TEXT"));

    /// <summary>Adds one nullable column when an older database predates it (idempotent).</summary>
    private void AddColumnIfMissing(string table, (string Name, string Type) column)
    {
        using var check = _connection.CreateCommand();
        // Table and column names are fixed internal literals, never user input.
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column.Name}';";
        if ((long)(check.ExecuteScalar() ?? 0L) > 0)
        {
            return;
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column.Name} {column.Type} NULL;";
        alter.ExecuteNonQuery();
    }

    /// <summary>
    /// ADM-004: deletes rows older than <paramref name="retentionDays"/>. Returns the number of
    /// rows removed. A non-positive retention keeps everything.
    /// </summary>
    public int Prune(int retentionDays) => Prune(retentionDays, DateTimeOffset.UtcNow);

    /// <summary>Retention prune anchored at <paramref name="now"/> (injectable for tests).</summary>
    public int Prune(int retentionDays, DateTimeOffset now)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var cutoff = FormatDay(DateOnly.FromDateTime(now.UtcDateTime).AddDays(-retentionDays));

        lock (_gate)
        {
            var removed = 0;
            foreach (var table in (string[])["visits", "downloads", "not_found"])
            {
                using var command = _connection.CreateCommand();
                command.CommandText = $"DELETE FROM {table} WHERE day < $cutoff;";
                command.Parameters.AddWithValue("$cutoff", cutoff);
                removed += command.ExecuteNonQuery();
            }

            return removed;
        }
    }

    /// <summary>
    /// ADM-001: the predicate that keeps crawler traffic out of visitor figures.
    /// <see cref="UserAgentBuckets"/> already classifies bots on every row; nothing consumed it.
    /// </summary>
    private const string HumanOnly = "(ua_family IS NULL OR ua_family <> 'bot')";

    private long CountRows(
        string table,
        string whereClause,
        (string Name, object Value)? parameter,
        string? distinctColumn = null)
    {
        using var command = _connection.CreateCommand();
        // Table name, WHERE clause and distinct column are fixed internal fragments (never user
        // input); values are parameterized.
        var selector = distinctColumn is null ? "COUNT(*)" : $"COUNT(DISTINCT {distinctColumn})";
        command.CommandText = $"SELECT {selector} FROM {table} WHERE {whereClause};";
        if (parameter is { } p)
        {
            command.Parameters.AddWithValue(p.Name, p.Value);
        }

        return (long)(command.ExecuteScalar() ?? 0L);
    }

    /// <summary>
    /// Top values of one enrichment column, excluding rows where it is null or empty. Nulls are
    /// skipped rather than bucketed as "unknown": history written before the column existed would
    /// otherwise dominate every one of these tables with a meaningless top row.
    /// </summary>
    private IReadOnlyList<CountRow> TopBy(string column, DateOnly since) =>
        QueryCountRows(
            // Column name is a fixed internal literal, never user input.
            $"SELECT {column}, COUNT(*) FROM visits " +
            $"WHERE day >= $day AND {HumanOnly} AND {column} IS NOT NULL AND {column} <> '' " +
            $"GROUP BY {column} ORDER BY COUNT(*) DESC, {column} LIMIT $limit;",
            ("$day", FormatDay(since)));

    /// <summary>Aggregate session shape for the window.</summary>
    private readonly record struct SessionStats(
        long Sessions,
        double BounceRatePercent,
        double PagesPerSession,
        double AverageSeconds);

    /// <summary>
    /// Session counts, bounce rate and duration. A "bounce" is a session with exactly one page
    /// view; duration is last-minus-first view, so a single-page session is 0 seconds — the
    /// standard limitation of server-side timing (there is no signal for when the last page was
    /// closed without a client-side beacon).
    /// Caller must hold <c>_gate</c>.
    /// </summary>
    private SessionStats QuerySessionStats(DateOnly since)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*), " +
            "       SUM(CASE WHEN views = 1 THEN 1 ELSE 0 END), " +
            "       AVG(views), " +
            "       AVG(span_seconds) " +
            "FROM (SELECT session_id, COUNT(*) AS views, " +
            "             (julianday(MAX(utc)) - julianday(MIN(utc))) * 86400.0 AS span_seconds " +
            "      FROM visits " +
            $"      WHERE day >= $day AND {HumanOnly} AND session_id IS NOT NULL " +
            "      GROUP BY session_id);";
        command.Parameters.AddWithValue("$day", FormatDay(since));

        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0))
        {
            return new SessionStats(0, 0, 0, 0);
        }

        var sessions = reader.GetInt64(0);
        if (sessions == 0)
        {
            return new SessionStats(0, 0, 0, 0);
        }

        var bounces = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
        var pagesPerSession = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
        var averageSeconds = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);

        return new SessionStats(
            sessions,
            Math.Round(bounces * 100.0 / sessions, 1),
            Math.Round(pagesPerSession, 2),
            Math.Round(averageSeconds, 0));
    }

    /// <summary>
    /// Pages that begin (<paramref name="first"/>) or end a session. Entry pages say where people
    /// actually arrive — which is rarely the home page once search and deep links are in play —
    /// and exit pages say where they stop.
    /// Caller must hold <c>_gate</c>.
    /// </summary>
    private IReadOnlyList<CountRow> QuerySessionEdgePages(DateOnly since, bool first)
    {
        var edge = first ? "MIN" : "MAX";
        return QueryCountRows(
            "SELECT path, COUNT(*) FROM visits WHERE id IN (" +
            $"    SELECT {edge}(id) FROM visits " +
            $"    WHERE day >= $day AND {HumanOnly} AND session_id IS NOT NULL " +
            "    GROUP BY session_id) " +
            "GROUP BY path ORDER BY COUNT(*) DESC, path LIMIT $limit;",
            ("$day", FormatDay(since)));
    }

    private IReadOnlyList<CountRow> QueryCountRows(string sql, (string Name, object Value)? parameter)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        if (parameter is { } p)
        {
            command.Parameters.AddWithValue(p.Name, p.Value);
        }

        command.Parameters.AddWithValue("$limit", TopRowLimit);

        var rows = new List<CountRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CountRow(reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }

    /// <summary>
    /// Zero-filled daily series for a table over the window. <paramref name="distinctColumn"/>
    /// switches between total events and distinct values (unique visitors per day).
    /// </summary>
    private IReadOnlyList<DailyCount> QueryDailySeries(
        string table,
        DateOnly since,
        DateOnly today,
        string? distinctColumn)
    {
        var countsByDay = new Dictionary<DateOnly, long>();
        using (var command = _connection.CreateCommand())
        {
            // Table name and distinct column are fixed internal fragments; the date is parameterized.
            var selector = distinctColumn is null ? "COUNT(*)" : $"COUNT(DISTINCT {distinctColumn})";
            command.CommandText =
                $"SELECT day, {selector} FROM {table} WHERE day >= $day AND {HumanOnly} GROUP BY day;";
            command.Parameters.AddWithValue("$day", FormatDay(since));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var day = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                countsByDay[day] = reader.GetInt64(1);
            }
        }

        var series = new List<DailyCount>(today.DayNumber - since.DayNumber + 1);
        for (var day = since; day <= today; day = day.AddDays(1))
        {
            series.Add(new DailyCount(day, countsByDay.GetValueOrDefault(day)));
        }

        return series;
    }

    private static string FormatUtc(DateTimeOffset utc) => utc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    private static string FormatDay(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
