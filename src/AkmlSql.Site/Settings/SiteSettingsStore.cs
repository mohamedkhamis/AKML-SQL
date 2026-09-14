using System.Globalization;
using AkmlSql.Site.Analytics;
using Microsoft.Data.Sqlite;

namespace AkmlSql.Site.Settings;

/// <summary>
/// Owner-editable settings, stored in a <c>site_settings</c> table inside the existing
/// <c>analytics.db</c> (spec 038 US2; research R4).
/// <para>
/// The database already exists, the deploy script already ACLs its folder for the app pool identity,
/// and it is backed up together with the metrics these settings govern — so a new table inherits all
/// of that with no deploy change, no second file to protect, and no second thing to remember.
/// </para>
/// <para>
/// It opens its OWN connection rather than sharing <see cref="AnalyticsStore"/>'s. Sharing that
/// class's single lock would make every settings read wait behind a metrics write; WAL mode (already
/// enabled) lets a second connection read concurrently.
/// </para>
/// <para>
/// <b>The public download page reads <see cref="Current"/> and nothing else.</b> The render path
/// never touches SQLite, so a settings failure can neither break nor slow the site's primary call to
/// action (FR-016a, contract R2.7/R2.8).
/// </para>
/// </summary>
public sealed class SiteSettingsStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SiteSettingsStore>? _logger;
    private readonly object _gate = new();

    public SiteSettingsStore(AnalyticsOptions options, ILogger<SiteSettingsStore>? logger = null)
        : this(AnalyticsStore.ResolveDatabasePath(options?.DatabasePath), logger)
    {
    }

    /// <summary>Opens (creating if needed) the settings table in the database at <paramref name="databasePath"/>.</summary>
    public SiteSettingsStore(string databasePath, ILogger<SiteSettingsStore>? logger = null)
    {
        _logger = logger;
        DatabasePath = databasePath;

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString);
        _connection.Open();
    }

    /// <summary>Resolved absolute database path.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// The settings in force. Read by the download page on every render, so it is an in-memory
    /// property and never a query. Replaced wholesale on save.
    /// </summary>
    public SiteSettings Current { get; private set; } = SiteSettings.Defaults;

    /// <summary>True when the last <see cref="Load"/> failed and <see cref="Current"/> is the fallback.</summary>
    public bool LoadFailed { get; private set; }

    /// <summary>Why the last load failed, for the health probe and the portal warning.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Creates the settings table when absent. Safe to call on every start.</summary>
    public void CreateTableIfMissing()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS site_settings (
                    key TEXT PRIMARY KEY NOT NULL,
                    value TEXT NOT NULL,
                    updated_utc TEXT NOT NULL,
                    updated_by TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Reads every stored key into <see cref="Current"/>.
    /// <para>
    /// <b>Never throws.</b> A missing key falls back to its default; a locked, corrupt or missing
    /// table falls back to <see cref="SiteSettings.Defaults"/> with <see cref="LoadFailed"/> set.
    /// Failing here would take down the public download page for a reason unrelated to releases —
    /// strictly worse than advertising three of them (FR-016a, contract R2.8).
    /// </para>
    /// </summary>
    public void Load()
    {
        try
        {
            var stored = ReadAll();

            Current = new SiteSettings
            {
                Visibility = stored.TryGetValue(SiteSettings.VisibilityKey, out var mode)
                    ? ReleaseVisibility.Parse(mode)
                    : ReleaseVisibilityBounds.DefaultMode,
                VisibilityCount = ParseIntOrDefault(
                    stored, SiteSettings.VisibilityCountKey, ReleaseVisibilityBounds.DefaultCount),
                IdentifiableRetentionDays = ParseIntOrDefault(
                    stored, SiteSettings.IdentifiableRetentionKey, SiteSettings.DefaultRetentionDays),
            };

            LoadFailed = false;
            LoadError = null;
        }
        catch (Exception ex)
        {
            Current = SiteSettings.Defaults;
            LoadFailed = true;
            LoadError = ex.Message;
            _logger?.LogWarning(
                ex,
                "Site settings could not be loaded from {DatabasePath}; serving documented defaults.",
                DatabasePath);
        }
    }

    /// <summary>
    /// Validates and persists <paramref name="candidate"/>, returning the validation errors that
    /// prevented the write (empty on success).
    /// <para>
    /// Last save wins, inside a transaction. <c>updated_utc</c> / <c>updated_by</c> make the outcome
    /// of a concurrent edit visible rather than silent (FR-035).
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Save(SiteSettings candidate, string? updatedBy)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var errors = candidate.Validate();
        if (errors.Count > 0)
        {
            return errors;
        }

        var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            Write(transaction, SiteSettings.VisibilityKey, candidate.Visibility.ToString(), now, updatedBy);
            Write(transaction, SiteSettings.VisibilityCountKey, candidate.VisibilityCount.ToString(CultureInfo.InvariantCulture), now, updatedBy);
            Write(transaction, SiteSettings.IdentifiableRetentionKey, candidate.IdentifiableRetentionDays.ToString(CultureInfo.InvariantCulture), now, updatedBy);

            transaction.Commit();
        }

        Current = candidate;
        LoadFailed = false;
        LoadError = null;
        return [];
    }

    /// <summary>When the settings were last changed and by whom, for the portal's confirmation display.</summary>
    public (DateTimeOffset? UpdatedUtc, string? UpdatedBy) LastChange()
    {
        try
        {
            lock (_gate)
            {
                using var command = _connection.CreateCommand();
                command.CommandText =
                    "SELECT updated_utc, updated_by FROM site_settings ORDER BY updated_utc DESC LIMIT 1;";

                using var reader = command.ExecuteReader();
                if (reader.Read()
                    && DateTimeOffset.TryParse(
                        reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc))
                {
                    return (utc, reader.IsDBNull(1) ? null : reader.GetString(1));
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException)
        {
            // Display-only; a failure here must not surface as an error page.
        }

        return (null, null);
    }

    public void Dispose() => _connection.Dispose();

    private Dictionary<string, string> ReadAll()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM site_settings;";

            using var reader = command.ExecuteReader();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                values[reader.GetString(0)] = reader.GetString(1);
            }

            return values;
        }
    }

    private void Write(SqliteTransaction transaction, string key, string value, string nowUtc, string? updatedBy)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO site_settings (key, value, updated_utc, updated_by)
            VALUES ($key, $value, $utc, $by)
            ON CONFLICT(key) DO UPDATE SET value = $value, updated_utc = $utc, updated_by = $by;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$utc", nowUtc);
        command.Parameters.AddWithValue("$by", (object?)updatedBy ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static int ParseIntOrDefault(IReadOnlyDictionary<string, string> stored, string key, int fallback) =>
        stored.TryGetValue(key, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
