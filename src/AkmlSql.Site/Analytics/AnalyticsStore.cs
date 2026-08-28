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

    /// <summary>Records one page view.</summary>
    public void LogVisit(VisitInfo visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentException.ThrowIfNullOrWhiteSpace(visit.Path);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO visits (utc, path, referrer_host, ua_family, ip_hash) " +
                "VALUES ($utc, $path, $referrer, $ua, $hash);";
            command.Parameters.AddWithValue("$utc", FormatUtc(visit.Utc));
            command.Parameters.AddWithValue("$path", visit.Path);
            command.Parameters.AddWithValue("$referrer", (object?)visit.ReferrerHost ?? DBNull.Value);
            command.Parameters.AddWithValue("$ua", (object?)visit.UaFamily ?? DBNull.Value);
            command.Parameters.AddWithValue("$hash", ComputeIpHash(visit.IpAddress, DateOnly.FromDateTime(visit.Utc.UtcDateTime)));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Records one installer download.</summary>
    public void LogDownload(DownloadInfo download)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentException.ThrowIfNullOrWhiteSpace(download.File);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO downloads (utc, file, referrer_host, ua_family, ip_hash) " +
                "VALUES ($utc, $file, $referrer, $ua, $hash);";
            command.Parameters.AddWithValue("$utc", FormatUtc(download.Utc));
            command.Parameters.AddWithValue("$file", download.File);
            command.Parameters.AddWithValue("$referrer", (object?)download.ReferrerHost ?? DBNull.Value);
            command.Parameters.AddWithValue("$ua", (object?)download.UaFamily ?? DBNull.Value);
            command.Parameters.AddWithValue("$hash", ComputeIpHash(download.IpAddress, DateOnly.FromDateTime(download.Utc.UtcDateTime)));
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
            return new AnalyticsSummary
            {
                Days = days,
                VisitsToday = CountRows("visits", "substr(utc, 1, 10) = $day", ("$day", FormatDay(today))),
                VisitsLast7Days = CountRows("visits", "substr(utc, 1, 10) >= $day", ("$day", FormatDay(since7))),
                VisitsWindow = CountRows("visits", "substr(utc, 1, 10) >= $day", ("$day", FormatDay(sinceWindow))),
                DownloadsTotal = CountRows("downloads", "1 = 1", null),
                DownloadsLast7Days = CountRows("downloads", "substr(utc, 1, 10) >= $day", ("$day", FormatDay(since7))),
                TopPages = QueryCountRows(
                    "SELECT path, COUNT(*) FROM visits WHERE substr(utc, 1, 10) >= $day " +
                    "GROUP BY path ORDER BY COUNT(*) DESC, path LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
                DownloadsByFile = QueryCountRows(
                    "SELECT file, COUNT(*) FROM downloads " +
                    "GROUP BY file ORDER BY COUNT(*) DESC, file LIMIT $limit;",
                    null),
                DailyVisits = QueryDailySeries(sinceWindow, today),
                TopReferrers = QueryCountRows(
                    "SELECT referrer_host, COUNT(*) FROM visits " +
                    "WHERE referrer_host IS NOT NULL AND referrer_host <> '' AND substr(utc, 1, 10) >= $day " +
                    "GROUP BY referrer_host ORDER BY COUNT(*) DESC, referrer_host LIMIT $limit;",
                    ("$day", FormatDay(sinceWindow))),
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

    private void InitializeSchema()
    {
        using var command = _connection.CreateCommand();
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
            CREATE INDEX IF NOT EXISTS ix_visits_utc ON visits (utc);
            CREATE INDEX IF NOT EXISTS ix_downloads_utc ON downloads (utc);
            """;
        command.ExecuteNonQuery();
    }

    private long CountRows(string table, string whereClause, (string Name, object Value)? parameter)
    {
        using var command = _connection.CreateCommand();
        // Table name and WHERE clause are fixed internal fragments (never user input); values are parameterized.
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {whereClause};";
        if (parameter is { } p)
        {
            command.Parameters.AddWithValue(p.Name, p.Value);
        }

        return (long)(command.ExecuteScalar() ?? 0L);
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

    private IReadOnlyList<DailyCount> QueryDailySeries(DateOnly since, DateOnly today)
    {
        var countsByDay = new Dictionary<DateOnly, long>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "SELECT substr(utc, 1, 10), COUNT(*) FROM visits WHERE substr(utc, 1, 10) >= $day " +
                "GROUP BY substr(utc, 1, 10);";
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
