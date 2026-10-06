using AkmlSql.Site.Telemetry;
using Microsoft.Data.Sqlite;

namespace AkmlSql.Site.Analytics;

public sealed partial class AnalyticsStore
{
    private const string ClientErrorFilter = "($level IS NULL OR level=$level) AND " +
        "($version IS NULL OR product_version=$version) AND " +
        "($search='' OR instr(lower(message || ' ' || COALESCE(exception,'')), lower($search))>0)";

    private static void BindErrorFilters(SqliteCommand command, string? level, string? version, string? search)
    {
        command.Parameters.AddWithValue("$level", (object?)level ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", (object?)version ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", search ?? "");
    }

    public long CountClientErrors(ReportWindow window, string? level, string? version, string? search)
    {
        using var snapshot = OpenReadSnapshot();
        using var command = snapshot.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM client_errors WHERE {ErrorsInWindow} AND {ClientErrorFilter}";
        BindWindow(command, window);
        BindErrorFilters(command, level, version, search);
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<ClientErrorRow> GetClientErrorPage(ReportWindow window, string? level,
        string? version, string? search, int page, int size)
    {
        using var snapshot = OpenReadSnapshot();
        size = Math.Clamp(size, 1, 500);
        return snapshot.QueryRecentClientErrors(window, level, size, (long)Math.Max(0, page) * size, version, search);
    }

    public IEnumerable<ClientErrorRow> EnumerateClientErrors(ReportWindow window, string? level, string? version, string? search)
    {
        using var snapshot = OpenReadSnapshot();
        using var command = snapshot.CreateCommand();
        command.CommandText = "SELECT id, event_utc, level, product_version, host, install_id, message, exception " +
            $"FROM client_errors WHERE {ErrorsInWindow} AND {ClientErrorFilter} ORDER BY id DESC";
        BindWindow(command, window);
        BindErrorFilters(command, level, version, search);
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return new ClientErrorRow(reader.GetInt64(0), ParseUtc(reader.GetString(1)),
            reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7));
    }
}
