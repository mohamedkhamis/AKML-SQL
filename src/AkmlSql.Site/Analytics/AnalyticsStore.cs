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
                "INSERT INTO visits (utc, day, path, referrer_host, ua_family, ip_hash) " +
                "VALUES ($utc, $day, $path, $referrer, $ua, $hash);";
            command.Parameters.AddWithValue("$utc", FormatUtc(visit.Utc));
            command.Parameters.AddWithValue("$day", FormatDay(DateOnly.FromDateTime(visit.Utc.UtcDateTime)));
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
                "INSERT INTO downloads (utc, day, file, referrer_host, ua_family, ip_hash) " +
                "VALUES ($utc, $day, $file, $referrer, $ua, $hash);";
            command.Parameters.AddWithValue("$utc", FormatUtc(download.Utc));
            command.Parameters.AddWithValue("$day", FormatDay(DateOnly.FromDateTime(download.Utc.UtcDateTime)));
            command.Parameters.AddWithValue("$file", download.File);
            command.Parameters.AddWithValue("$referrer", (object?)download.ReferrerHost ?? DBNull.Value);
            command.Parameters.AddWithValue("$ua", (object?)download.UaFamily ?? DBNull.Value);
            command.Parameters.AddWithValue("$hash", ComputeIpHash(download.IpAddress, DateOnly.FromDateTime(download.Utc.UtcDateTime)));
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

    /// <summary>Adds the <c>day</c> column when an older database predates it.</summary>
    private void AddDayColumnIfMissing(string table)
    {
        using var check = _connection.CreateCommand();
        // Table name is a fixed internal literal, never user input.
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = 'day';";
        if ((long)(check.ExecuteScalar() ?? 0L) > 0)
        {
            return;
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN day TEXT NULL;";
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
