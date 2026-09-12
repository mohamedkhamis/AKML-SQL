using AkmlSql.Site.Settings;
using Xunit;

namespace AkmlSql.Site.Tests.Settings;

/// <summary>
/// Spec 038 T012: the settings store behind the release-visibility control (US2).
/// <para>
/// Two properties matter more than the round-trip: out-of-range input is <b>rejected, never
/// clamped</b> (clamping leaves the owner believing they saved something they did not), and an
/// unreadable store <b>falls back instead of throwing</b> — the public download page must never
/// break for a reason unrelated to releases (FR-016a).
/// </para>
/// </summary>
public sealed class SiteSettingsStoreTests
{
    private static string DbPath(TempDirectory dir) => Path.Combine(dir.Path, "analytics.db");

    private static SiteSettingsStore NewStore(TempDirectory dir)
    {
        var store = new SiteSettingsStore(DbPath(dir));
        store.CreateTableIfMissing();
        store.Load();
        return store;
    }

    [Fact]
    public void Load_WithNothingSaved_AppliesTheDocumentedDefaults()
    {
        using var dir = new TempDirectory();

        using var store = NewStore(dir);

        // FR-016: the default MUST NOT be "show every release".
        Assert.Equal(ReleaseVisibilityMode.LatestN, store.Current.Visibility);
        Assert.Equal(3, store.Current.VisibilityCount);
        Assert.Equal(365, store.Current.IdentifiableRetentionDays);
        Assert.NotEqual(ReleaseVisibilityMode.All, store.Current.Visibility);
        Assert.False(store.LoadFailed);
    }

    [Fact]
    public void Save_ThenReopen_PersistsAcrossRestart()
    {
        using var dir = new TempDirectory();

        using (var store = NewStore(dir))
        {
            var errors = store.Save(
                store.Current with { Visibility = ReleaseVisibilityMode.LatestOnly, IdentifiableRetentionDays = 90 },
                "owner");
            Assert.Empty(errors);
        }

        // A new instance is what an app-pool recycle produces.
        using var reopened = NewStore(dir);

        Assert.Equal(ReleaseVisibilityMode.LatestOnly, reopened.Current.Visibility);
        Assert.Equal(90, reopened.Current.IdentifiableRetentionDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(999)]
    public void Save_WithCountOutOfRange_IsRejectedAndWritesNothing(int count)
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        var before = store.Current;

        var errors = store.Save(store.Current with { VisibilityCount = count }, "owner");

        // Rejected with a message naming the bound — and NOT silently clamped to 1 or 50.
        var message = Assert.Single(errors);
        Assert.Contains("between 1 and 50", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(count.ToString(), message, StringComparison.Ordinal);
        Assert.Equal(before, store.Current);

        using var reopened = NewStore(dir);
        Assert.Equal(before.VisibilityCount, reopened.Current.VisibilityCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(99999)]
    public void Save_WithRetentionOutOfRange_IsRejectedAndWritesNothing(int days)
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);
        var before = store.Current;

        var errors = store.Save(store.Current with { IdentifiableRetentionDays = days }, "owner");

        var message = Assert.Single(errors);
        Assert.Contains("between 1 and 3650 days", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, store.Current);
    }

    [Fact]
    public void Save_TwiceInSuccession_LastWinsAndRecordsWhoAndWhen()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        Assert.Empty(store.Save(store.Current with { VisibilityCount = 5 }, "session-a"));
        Assert.Empty(store.Save(store.Current with { VisibilityCount = 7 }, "session-b"));

        Assert.Equal(7, store.Current.VisibilityCount);

        var (updatedUtc, updatedBy) = store.LastChange();
        Assert.NotNull(updatedUtc);
        Assert.Equal("session-b", updatedBy);
    }

    [Fact]
    public void Load_WhenTheTableIsMissing_FallsBackToDefaultsWithoutThrowing()
    {
        using var dir = new TempDirectory();

        // Deliberately skip CreateTableIfMissing: the table does not exist.
        using var store = new SiteSettingsStore(DbPath(dir));

        var exception = Record.Exception(store.Load);

        // FR-016a: the page must serve. A throw here would take the site down for a settings problem.
        Assert.Null(exception);
        Assert.True(store.LoadFailed);
        Assert.NotNull(store.LoadError);
        Assert.Equal(SiteSettings.Defaults, store.Current);
    }

    [Fact]
    public void Load_WithAGarbageStoredValue_FallsBackPerKeyRatherThanFailing()
    {
        using var dir = new TempDirectory();

        using (var store = NewStore(dir))
        {
            Assert.Empty(store.Save(store.Current with { VisibilityCount = 9 }, "owner"));
        }

        // Corrupt one value the way a bad manual edit would.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath(dir)}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE site_settings SET value = 'not-a-number' WHERE key = $key;";
            command.Parameters.AddWithValue("$key", SiteSettings.VisibilityCountKey);
            command.ExecuteNonQuery();
        }

        using var reopened = NewStore(dir);

        // The bad key falls back to its default; the store as a whole still loaded.
        Assert.Equal(ReleaseVisibilityBounds.DefaultCount, reopened.Current.VisibilityCount);
        Assert.False(reopened.LoadFailed);
    }

    [Fact]
    public void Current_IsReadableWithoutTouchingTheDatabase()
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir);
        var expected = store.Current;

        // Dispose the connection, then read Current — the render path must not need SQLite.
        store.Dispose();

        Assert.Equal(expected, store.Current);
    }
}
