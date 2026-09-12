using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// Spec 038 US3: the download-centric and per-individual queries behind the portal.
/// <para>
/// Kept in a partial beside the main store rather than inside it: <c>AnalyticsStore.cs</c> is
/// already ~900 lines, and these queries are a distinct concern — reporting, not recording. They
/// share the same connection and lock, so they observe exactly what has been written.
/// </para>
/// </summary>
public sealed partial class AnalyticsStore
{
    /// <summary>Bot filter for people/visit figures — crawlers and scripted clients are not people.</summary>
    private const string PeopleHumanOnly = "(ua_family IS NULL OR ua_family NOT IN ('bot', 'curl', 'wget', 'powershell', 'python', 'java', 'go-http', 'libwww'))";

    /// <summary>
    /// Downloads grouped by country, ranked, with shares.
    /// <para>
    /// A NULL country becomes an explicit "Unknown" bucket rather than being dropped, so the rows
    /// always sum to the headline total (FR-028, contract M5.1). On this deployment that bucket is
    /// currently <b>everything</b>: the GeoLite2 database has never been installed, so no row has
    /// ever carried a country.
    /// </para>
    /// </summary>
    public IReadOnlyList<DownloadCountryRow> GetDownloadsByCountry(int days, DateTimeOffset now)
    {
        var since = FormatDay(WindowStart(days, now));

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT country, country_code, COUNT(*) AS c FROM downloads " +
                $"WHERE day >= $since AND {RealDownloadOnly} " +
                "GROUP BY country, country_code ORDER BY c DESC;";
            command.Parameters.AddWithValue("$since", since);

            var rows = new List<(string? Country, string? Code, long Count)>();
            long total = 0;

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var count = reader.GetInt64(2);
                    total += count;
                    rows.Add((
                        reader.IsDBNull(0) ? null : reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        count));
                }
            }

            return rows
                .Select(r => new DownloadCountryRow(r.Country, r.Code, r.Count, Share(r.Count, total)))
                .ToList();
        }
    }

    /// <summary>
    /// Downloads grouped by release version, ranked, with shares.
    /// <para>
    /// Version is read from the column written at download time, not resolved from the manifest now:
    /// a release later removed from the manifest would otherwise retroactively orphan its history.
    /// Rows with no version form an explicit "Unattributed" bucket (contract M5.2).
    /// </para>
    /// </summary>
    public IReadOnlyList<DownloadVersionRow> GetDownloadsByVersion(int days, DateTimeOffset now)
    {
        var since = FormatDay(WindowStart(days, now));

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT release_version, file, COUNT(*) AS c FROM downloads " +
                $"WHERE day >= $since AND {RealDownloadOnly} " +
                "GROUP BY release_version, file ORDER BY c DESC;";
            command.Parameters.AddWithValue("$since", since);

            var rows = new List<(string? Version, string? File, long Count)>();
            long total = 0;

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var count = reader.GetInt64(2);
                    total += count;
                    rows.Add((
                        reader.IsDBNull(0) ? null : reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        count));
                }
            }

            // Rows written before spec 038 have no release_version, but the installer file name
            // carries it ("AKMLSQLSetup-1.26.0901.1502.exe"). Showing "Unattributed" next to a file
            // name that literally contains the version is useless to the owner, so derive it for
            // display. The STORED value still wins wherever it exists: this is a fallback for
            // history, not a replacement for write-time attribution.
            return rows
                .Select(r => new DownloadVersionRow(
                    r.Version ?? VersionFromFileName(r.File), r.File, r.Count, Share(r.Count, total)))
                .ToList();
        }
    }

    /// <summary>
    /// What the individual-level figures can and cannot see, for the window (FR-047).
    /// </summary>
    public CoverageSummary GetCoverage(int days, DateTimeOffset now)
    {
        var since = FormatDay(WindowStart(days, now));

        lock (_gate)
        {
            long attributed = 0, unattributed = 0, automated = 0;

            using (var command = _connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT " +
                    $"  SUM(CASE WHEN visitor_id IS NOT NULL AND {PeopleHumanOnly} THEN 1 ELSE 0 END), " +
                    $"  SUM(CASE WHEN visitor_id IS NULL AND {PeopleHumanOnly} THEN 1 ELSE 0 END), " +
                    $"  SUM(CASE WHEN NOT {PeopleHumanOnly} THEN 1 ELSE 0 END) " +
                    "FROM visits WHERE day >= $since;";
                command.Parameters.AddWithValue("$since", since);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    attributed = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                    unattributed = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                    automated = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                }
            }

            long distinct = 0, returning = 0;

            using (var command = _connection.CreateCommand())
            {
                // "Returning" means first seen BEFORE the window opened — the only honest reading,
                // and the one cross-day cookie identity made possible (FR-022a).
                // The bot filter MUST match GetIndividuals exactly. Without it the headline
                // "N individuals" contradicted the table below it: automated clients that accept a
                // cookie (a headless browser in an E2E run, for instance) were counted here and
                // excluded there. A stat that disagrees with the list under it is worse than no
                // stat (FR-027, SC-008).
                command.CommandText =
                    "SELECT COUNT(*), SUM(CASE WHEN first_ever < $since THEN 1 ELSE 0 END) FROM (" +
                    "  SELECT visitor_id, MIN(day) AS first_ever FROM (" +
                    $"    SELECT visitor_id, day FROM visits WHERE visitor_id IS NOT NULL AND {PeopleHumanOnly}" +
                    "    UNION ALL" +
                    "    SELECT visitor_id, day FROM downloads WHERE visitor_id IS NOT NULL" +
                    "  ) GROUP BY visitor_id" +
                    ") WHERE visitor_id IN (" +
                    $"  SELECT visitor_id FROM visits WHERE day >= $since AND visitor_id IS NOT NULL AND {PeopleHumanOnly}" +
                    "  UNION SELECT visitor_id FROM downloads WHERE day >= $since AND visitor_id IS NOT NULL" +
                    ");";
                command.Parameters.AddWithValue("$since", since);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    distinct = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                    returning = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                }
            }

            return new CoverageSummary(
                attributed, unattributed, automated, distinct, distinct - returning, returning);
        }
    }

    /// <summary>
    /// The people list, filtered and paged.
    /// <para>
    /// Aggregated over <c>visitor_id</c> across both event tables. Filters are applied in SQL so the
    /// counts the page shows describe the same set as the rows (FR-025).
    /// </para>
    /// </summary>
    public IReadOnlyList<IndividualRow> GetIndividuals(IndividualFilter filter, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var since = FormatDay(WindowStart(filter.Days, now));

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = BuildIndividualsSql(filter, paged: true);
            BindIndividualsParameters(command, filter, since);

            using var reader = command.ExecuteReader();
            var rows = new List<IndividualRow>();
            while (reader.Read())
            {
                rows.Add(ReadIndividualRow(reader, since));
            }

            return rows;
        }
    }

    /// <summary>Total individuals matching the filter, for paging.</summary>
    public long CountIndividuals(IndividualFilter filter, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var since = FormatDay(WindowStart(filter.Days, now));

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM (" + BuildIndividualsSql(filter, paged: false) + ");";
            BindIndividualsParameters(command, filter, since, includePaging: false);

            var value = command.ExecuteScalar();
            return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// One individual with their visits and downloads interleaved in time order, or null when the
    /// id is unknown (so the page renders a clean not-found rather than an empty shell).
    /// </summary>
    public IndividualDetail? GetIndividual(string visitorId, int days, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(visitorId))
        {
            return null;
        }

        var filter = new IndividualFilter(days, Page: 0, PageSize: 1);
        var since = FormatDay(WindowStart(days, now));

        lock (_gate)
        {
            IndividualRow? summary = null;

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = BuildIndividualsSql(filter, paged: false, singleVisitor: true);
                command.Parameters.AddWithValue("$since", since);
                command.Parameters.AddWithValue("$visitorId", visitorId);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    summary = ReadIndividualRow(reader, since);
                }
            }

            if (summary is null)
            {
                return null;
            }

            using var activityCommand = _connection.CreateCommand();
            activityCommand.CommandText =
                "SELECT utc, 'visit' AS kind, path AS target, NULL AS release_version, referrer_url, utm_campaign " +
                "FROM visits WHERE visitor_id = $visitorId AND day >= $since " +
                "UNION ALL " +
                "SELECT utc, 'download' AS kind, file AS target, release_version, referrer_url, utm_campaign " +
                "FROM downloads WHERE visitor_id = $visitorId AND day >= $since " +
                "ORDER BY utc;";
            activityCommand.Parameters.AddWithValue("$visitorId", visitorId);
            activityCommand.Parameters.AddWithValue("$since", since);

            var activity = new List<IndividualActivity>();
            using (var reader = activityCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    activity.Add(new IndividualActivity(
                        ParseUtc(reader.GetString(0)),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5)));
                }
            }

            return new IndividualDetail(summary, activity);
        }
    }

    // --- helpers -----------------------------------------------------------

    private static DateOnly WindowStart(int days, DateTimeOffset now) =>
        DateOnly.FromDateTime(now.UtcDateTime).AddDays(-(Math.Max(days, 1) - 1));

    /// <summary>
    /// Version embedded in an installer file name, or null when the name does not carry one.
    /// Matches the 1.YY.MMDD.HHmm shape the build stamps.
    /// </summary>
    internal static string? VersionFromFileName(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            file, @"(\d+\.\d+\.\d+\.\d+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static double Share(long count, long total) =>
        total <= 0 ? 0 : Math.Round(count * 100.0 / total, 1);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    /// <summary>
    /// The aggregate over both event tables. <paramref name="singleVisitor"/> swaps the window
    /// predicate for an id match so the detail view reuses exactly the same projection as the list.
    /// </summary>
    private static string BuildIndividualsSql(IndividualFilter filter, bool paged, bool singleVisitor = false)
    {
        var sql =
            "SELECT e.visitor_id, MIN(e.utc), MAX(e.utc), " +
            "  MAX(e.country), MAX(e.country_code), MAX(e.ip), MAX(e.ip_prefix), " +
            "  MAX(e.device), MAX(e.os_family), MAX(e.ua_family), " +
            "  SUM(CASE WHEN e.kind = 'visit' THEN 1 ELSE 0 END), " +
            "  SUM(CASE WHEN e.kind = 'download' THEN 1 ELSE 0 END), " +
            "  MIN(e.day) " +
            "FROM (" +
            "  SELECT visitor_id, utc, day, country, country_code, ip, ip_prefix, device, os_family, ua_family, 'visit' AS kind " +
            $"  FROM visits WHERE visitor_id IS NOT NULL AND {PeopleHumanOnly} " +
            "  UNION ALL " +
            "  SELECT visitor_id, utc, day, country, country_code, ip, ip_prefix, device, os_family, ua_family, 'download' AS kind " +
            "  FROM downloads WHERE visitor_id IS NOT NULL " +
            ") e ";

        sql += singleVisitor
            ? "WHERE e.visitor_id = $visitorId AND e.day >= $since "
            : "WHERE e.day >= $since ";

        if (!singleVisitor && filter.CountryCode is not null)
        {
            sql += "AND e.country_code = $countryCode ";
        }

        sql += "GROUP BY e.visitor_id ";

        if (!singleVisitor && filter.Downloaded is not null)
        {
            sql += filter.Downloaded.Value
                ? "HAVING SUM(CASE WHEN e.kind = 'download' THEN 1 ELSE 0 END) > 0 "
                : "HAVING SUM(CASE WHEN e.kind = 'download' THEN 1 ELSE 0 END) = 0 ";
        }

        sql += "ORDER BY MAX(e.utc) DESC ";

        if (paged)
        {
            sql += "LIMIT $limit OFFSET $offset";
        }

        return sql;
    }

    private static void BindIndividualsParameters(
        SqliteCommand command, IndividualFilter filter, string since, bool includePaging = true)
    {
        command.Parameters.AddWithValue("$since", since);

        if (filter.CountryCode is not null)
        {
            command.Parameters.AddWithValue("$countryCode", filter.CountryCode);
        }

        if (includePaging)
        {
            command.Parameters.AddWithValue("$limit", Math.Clamp(filter.PageSize, 1, 500));
            command.Parameters.AddWithValue("$offset", Math.Max(0, filter.Page) * Math.Clamp(filter.PageSize, 1, 500));
        }
    }

    private static IndividualRow ReadIndividualRow(SqliteDataReader reader, string since)
    {
        var firstDay = reader.IsDBNull(12) ? null : reader.GetString(12);

        return new IndividualRow(
            VisitorId: reader.GetString(0),
            FirstSeen: ParseUtc(reader.GetString(1)),
            LastSeen: ParseUtc(reader.GetString(2)),
            Country: reader.IsDBNull(3) ? null : reader.GetString(3),
            CountryCode: reader.IsDBNull(4) ? null : reader.GetString(4),
            IpAddress: reader.IsDBNull(5) ? null : reader.GetString(5),
            NetworkPrefix: reader.IsDBNull(6) ? null : reader.GetString(6),
            Device: reader.IsDBNull(7) ? null : reader.GetString(7),
            Os: reader.IsDBNull(8) ? null : reader.GetString(8),
            Browser: reader.IsDBNull(9) ? null : reader.GetString(9),
            VisitCount: reader.GetInt64(10),
            DownloadCount: reader.GetInt64(11),
            // Within the window this is the best available reading; GetCoverage computes the
            // all-history version for the headline new/returning split.
            IsReturning: firstDay is not null && string.CompareOrdinal(firstDay, since) < 0);
    }
}
