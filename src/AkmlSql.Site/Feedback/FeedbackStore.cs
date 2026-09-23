using System.Globalization;
using AkmlSql.Site.Analytics;
using Microsoft.Data.Sqlite;

namespace AkmlSql.Site.Feedback;

/// <summary>
/// Feedback sent from the site, kept in its own table inside analytics.db -- the file the deploy
/// already creates, ACLs for the app pool and backs up.
/// <para>
/// What is NOT stored matters as much as what is. There is no IP address and no visitor id: a
/// complaint should not quietly become a tracking record, and rate limiting happens in memory
/// without needing either. The optional email is the only personal data, and only because the
/// visitor typed it in order to be answered.
/// </para>
/// </summary>
public sealed class FeedbackStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public FeedbackStore(AnalyticsOptions options)
        : this(AnalyticsStore.ResolveDatabasePath(options?.DatabasePath))
    {
    }

    public FeedbackStore(string databasePath)
    {
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

        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS feedback (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                received_utc TEXT NOT NULL,
                category TEXT NOT NULL,
                message TEXT NOT NULL,
                email TEXT NULL,
                page TEXT NULL,
                country TEXT NULL,
                browser TEXT NULL,
                handled INTEGER NOT NULL DEFAULT 0,
                handled_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_feedback_handled ON feedback (handled, id);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Stores a validated submission and returns it with its id.</summary>
    public FeedbackItem Add(
        FeedbackCategory category, string message, string? email, string? page,
        string? country, string? browser, DateTimeOffset receivedUtc)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT INTO feedback (received_utc, category, message, email, page, country, browser) " +
                "VALUES ($received, $category, $message, $email, $page, $country, $browser); " +
                "SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$received", FormatUtc(receivedUtc));
            command.Parameters.AddWithValue("$category", category.Key);
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$email", (object?)email ?? DBNull.Value);
            command.Parameters.AddWithValue("$page", (object?)page ?? DBNull.Value);
            command.Parameters.AddWithValue("$country", (object?)country ?? DBNull.Value);
            command.Parameters.AddWithValue("$browser", (object?)browser ?? DBNull.Value);
            var id = (long)(command.ExecuteScalar() ?? 0L);

            return new FeedbackItem(id, receivedUtc, category, message, email, page, country, browser, false, null);
        }
    }

    /// <summary>Messages the owner has not dealt with yet -- the badge in the portal navigation.</summary>
    public long CountOpen()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM feedback WHERE handled = 0;";
            return (long)(command.ExecuteScalar() ?? 0L);
        }
    }

    /// <summary>Messages newest first. <paramref name="handled"/> null = all.</summary>
    public IReadOnlyList<FeedbackItem> List(bool? handled, int limit = 200)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT id, received_utc, category, message, email, page, country, browser, handled, handled_utc " +
                "FROM feedback WHERE ($handled IS NULL OR handled = $handled) ORDER BY id DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$handled", handled is null ? DBNull.Value : handled.Value ? 1 : 0);
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1_000));

            var items = new List<FeedbackItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(Read(reader));
            }

            return items;
        }
    }

    /// <summary>Counts for the inbox tabs.</summary>
    public (long Open, long Handled) Counts()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT COALESCE(SUM(CASE WHEN handled = 0 THEN 1 ELSE 0 END), 0), " +
                "       COALESCE(SUM(CASE WHEN handled = 1 THEN 1 ELSE 0 END), 0) FROM feedback;";
            using var reader = command.ExecuteReader();
            reader.Read();
            return (reader.GetInt64(0), reader.GetInt64(1));
        }
    }

    /// <summary>Marks a message handled (or open again). Returns false for an unknown id.</summary>
    public bool SetHandled(long id, bool handled, DateTimeOffset now)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE feedback SET handled = $handled, handled_utc = $at WHERE id = $id;";
            command.Parameters.AddWithValue("$handled", handled ? 1 : 0);
            command.Parameters.AddWithValue("$at", handled ? FormatUtc(now) : DBNull.Value);
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() == 1;
        }
    }

    /// <summary>Deletes a message permanently -- including any email address it carried.</summary>
    public bool Delete(long id)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM feedback WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public void Dispose() => _connection.Dispose();

    private static FeedbackItem Read(SqliteDataReader reader) => new(
        Id: reader.GetInt64(0),
        ReceivedUtc: ParseUtc(reader.GetString(1)),
        // A category removed from the list later still displays, as "Something else".
        Category: FeedbackCategory.Find(reader.GetString(2)) ?? FeedbackCategory.Other,
        Message: reader.GetString(3),
        Email: reader.IsDBNull(4) ? null : reader.GetString(4),
        Page: reader.IsDBNull(5) ? null : reader.GetString(5),
        Country: reader.IsDBNull(6) ? null : reader.GetString(6),
        Browser: reader.IsDBNull(7) ? null : reader.GetString(7),
        Handled: reader.GetInt64(8) == 1,
        HandledUtc: reader.IsDBNull(9) ? null : ParseUtc(reader.GetString(9)));

    private static string FormatUtc(DateTimeOffset utc) => utc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
