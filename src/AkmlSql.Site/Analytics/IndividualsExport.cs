using System.Globalization;
using System.Text;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// Spec 038 T072 (US3): CSV export for the downloads and people views.
/// <para>
/// Separate from <see cref="MetricsExport"/> because these are row exports, not the long-format
/// summary: a people export is one line per individual, which is what makes it pivotable.
/// </para>
/// <para>
/// Every file opens with a comment header naming the window and the active filters, so a file found
/// on a disk months later still says what it describes (contract M6.2). Files carrying an address or
/// an identifier also carry a personal-data label (FR-049, M6.3).
/// </para>
/// </summary>
public static class IndividualsExport
{
    /// <summary>Marker line on any export containing personal data.</summary>
    public const string PersonalDataNotice =
        "# CONTAINS PERSONAL DATA: IP addresses and persistent visitor identifiers. Handle accordingly.";

    /// <summary>Downloads grouped by country.</summary>
    public static string CountriesToCsv(IReadOnlyList<DownloadCountryRow> rows, int days, DateTimeOffset generatedAt)
    {
        var builder = Header(days, generatedAt, filters: null, personalData: false);
        builder.Append("country,country_code,downloads,share_percent\n");

        foreach (var row in rows ?? [])
        {
            builder.Append(Escape(row.Label)).Append(',')
                   .Append(Escape(row.CountryCode ?? "")).Append(',')
                   .Append(row.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(row.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Downloads grouped by release version.</summary>
    public static string VersionsToCsv(IReadOnlyList<DownloadVersionRow> rows, int days, DateTimeOffset generatedAt)
    {
        var builder = Header(days, generatedAt, filters: null, personalData: false);
        builder.Append("release_version,file,downloads,share_percent\n");

        foreach (var row in rows ?? [])
        {
            builder.Append(Escape(row.Label)).Append(',')
                   .Append(Escape(row.File ?? "")).Append(',')
                   .Append(row.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(row.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The people list — <b>the filtered rows currently displayed</b>, not the whole table
    /// (FR-026, contract M6.1).
    /// </summary>
    public static string IndividualsToCsv(
        IReadOnlyList<IndividualRow> rows,
        CoverageSummary coverage,
        IndividualFilter filter,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var builder = Header(filter.Days, generatedAt, DescribeFilters(filter), personalData: true);

        // The coverage caveat travels with the data. A spreadsheet of individuals detached from
        // "this is 12% of traffic" is exactly how a partial list gets read as the whole audience.
        if (coverage is not null)
        {
            builder.Append("# Coverage: ")
                   .Append(coverage.AttributedVisits.ToString(CultureInfo.InvariantCulture))
                   .Append(" of ")
                   .Append(coverage.TotalVisits.ToString(CultureInfo.InvariantCulture))
                   .Append(" visits are attributable to an individual (")
                   .Append((100 - coverage.UnattributedSharePercent).ToString("0.#", CultureInfo.InvariantCulture))
                   .Append("%). The rest declined tracking or have not answered.\n");
        }

        builder.Append("visitor_id,first_seen_utc,last_seen_utc,country,country_code,ip_address,network,device,os,browser,visits,downloads,downloaded,returning\n");

        foreach (var row in rows ?? [])
        {
            builder.Append(Escape(row.VisitorId)).Append(',')
                   .Append(row.FirstSeen.ToString("u", CultureInfo.InvariantCulture)).Append(',')
                   .Append(row.LastSeen.ToString("u", CultureInfo.InvariantCulture)).Append(',')
                   .Append(Escape(row.Country ?? "Unknown")).Append(',')
                   .Append(Escape(row.CountryCode ?? "")).Append(',')
                   .Append(Escape(row.IpAddress ?? "")).Append(',')
                   .Append(Escape(row.NetworkPrefix ?? "")).Append(',')
                   .Append(Escape(row.Device ?? "")).Append(',')
                   .Append(Escape(row.Os ?? "")).Append(',')
                   .Append(Escape(row.Browser ?? "")).Append(',')
                   .Append(row.VisitCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(row.DownloadCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(row.Downloaded ? "yes" : "no").Append(',')
                   .Append(row.IsReturning ? "yes" : "no").Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>File name carrying the window and filters, so the file is self-describing.</summary>
    public static string FileName(string view, IndividualFilter? filter, int days, DateTimeOffset generatedAt)
    {
        var parts = new List<string> { "akmlsql", view, $"{days}d" };

        if (filter?.CountryCode is { Length: > 0 } country)
        {
            parts.Add(country.ToLowerInvariant());
        }

        if (filter?.Downloaded is { } downloaded)
        {
            parts.Add(downloaded ? "downloaded" : "not-downloaded");
        }

        parts.Add(generatedAt.UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture));
        return string.Join('-', parts) + ".csv";
    }

    private static StringBuilder Header(int days, DateTimeOffset generatedAt, string? filters, bool personalData)
    {
        var builder = new StringBuilder();
        builder.Append("# AKML SQL site metrics\n");
        builder.Append("# Window: last ").Append(days.ToString(CultureInfo.InvariantCulture)).Append(" days\n");
        builder.Append("# Generated: ").Append(generatedAt.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)).Append('\n');

        if (!string.IsNullOrEmpty(filters))
        {
            builder.Append("# Filters: ").Append(filters).Append('\n');
        }

        if (personalData)
        {
            builder.Append(PersonalDataNotice).Append('\n');
        }

        return builder;
    }

    private static string DescribeFilters(IndividualFilter filter)
    {
        var parts = new List<string>();

        if (filter.CountryCode is { Length: > 0 } country)
        {
            parts.Add("country=" + country);
        }

        if (filter.Downloaded is { } downloaded)
        {
            parts.Add("downloaded=" + (downloaded ? "yes" : "no"));
        }

        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }

    private static string Escape(string value)
    {
        if (value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }
}
