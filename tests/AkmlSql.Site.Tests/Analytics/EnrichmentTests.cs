using AkmlSql.Site.Analytics;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// The analysis signals added on top of the basic visit record: IP truncation, user-agent detail,
/// language, campaign attribution, sessions, and the geo seam.
/// </summary>
public sealed class EnrichmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    // --- IP truncation ------------------------------------------------------

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.0")]
    [InlineData("8.8.8.8", "8.8.8.0")]
    [InlineData("192.168.1.55", "192.168.1.0")]
    // IPv4-mapped IPv6 is what Kestrel reports for an IPv4 client on a dual-stack socket; it must
    // truncate as IPv4, not be treated as a /48 IPv6 network.
    [InlineData("::ffff:203.0.113.7", "203.0.113.0")]
    public void Ipv4_IsTruncatedToItsSlash24(string input, string expected) =>
        Assert.Equal(expected, IpAnonymizer.ToPrefix(input));

    [Theory]
    [InlineData("2001:db8:abcd:1234:5678::1", "2001:db8:abcd::")]
    [InlineData("2a00:1450:4009:81f::200e", "2a00:1450:4009::")]
    public void Ipv6_IsTruncatedToItsSlash48(string input, string expected) =>
        Assert.Equal(expected, IpAnonymizer.ToPrefix(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    public void UnparseableAddresses_YieldNoPrefix(string? input) =>
        Assert.Null(IpAnonymizer.ToPrefix(input));

    [Fact]
    public void TheStoredPrefix_CannotDistinguishHostsOnTheSameNetwork()
    {
        // The whole point of truncating: two machines behind one /24 are indistinguishable.
        Assert.Equal(IpAnonymizer.ToPrefix("203.0.113.7"), IpAnonymizer.ToPrefix("203.0.113.200"));
        Assert.NotEqual(IpAnonymizer.ToPrefix("203.0.113.7"), IpAnonymizer.ToPrefix("203.0.114.7"));
    }

    [Fact]
    public void FullAddresses_AreNeverWrittenToTheDatabase()
    {
        // The strongest form of this test: read the raw file back and look for the address.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "analytics.db");
        const string ip = "203.0.113.199";

        using (var store = new AnalyticsStore(path))
        {
            store.LogVisit(new VisitInfo(Now, "/", null, "Chrome", ip));
            store.LogDownload(new DownloadInfo(Now, "setup.exe", null, "Chrome", ip));
        }

        // Disposing the store closes its connection, but Microsoft.Data.Sqlite pools the
        // underlying handle and the file stays locked until the pool is cleared.
        SqliteConnection.ClearAllPools();

        // Read the raw file rather than querying columns: this asserts the address appears
        // nowhere at all — not in a column, an index, or a stale WAL page.
        var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.DoesNotContain(ip, text, StringComparison.Ordinal);
        Assert.Contains("203.0.113.0", text, StringComparison.Ordinal); // the prefix is there
    }

    // --- User agent ---------------------------------------------------------

    [Theory]
    // Windows Chrome
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Chrome", "120", "Windows", "10/11", "desktop")]
    // Edge shadows both Chrome and Safari tokens
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
        "Edge", "120", "Windows", "10/11", "desktop")]
    // iPhone Safari reports its real version in Version/
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1",
        "Safari", "17", "iOS", "17.2", "mobile")]
    // iPad is a tablet even though its UA says "Mac OS X"
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/604.1",
        "Safari", "17", "iOS", "17.2", "tablet")]
    // Android phone: says Mobile
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
        "Chrome", "120", "Android", "14", "mobile")]
    // Android tablet: omits Mobile
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-X700) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
        "Chrome", "119", "Android", "13", "tablet")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15",
        "Safari", "17", "macOS", "10.15.7", "desktop")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Chrome", "120", "Linux", null, "desktop")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0",
        "Firefox", "121", "Windows", "10/11", "desktop")]
    public void UserAgent_IsParsedIntoBrowserOsAndDevice(
        string ua, string browser, string? browserVersion, string os, string? osVersion, string device)
    {
        var parsed = UserAgentDetailsParser.Parse(ua);

        Assert.Equal(browser, parsed.Browser);
        Assert.Equal(browserVersion, parsed.BrowserVersion);
        Assert.Equal(os, parsed.Os);
        Assert.Equal(osVersion, parsed.OsVersion);
        Assert.Equal(device, parsed.Device);
    }

    [Fact]
    public void Crawlers_AreClassifiedAsBots_NotAsWhicheverBrowserTheyImpersonate()
    {
        var parsed = UserAgentDetailsParser.Parse(
            "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");

        Assert.Equal("bot", parsed.Browser);
        Assert.Equal("bot", parsed.Device);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AMissingUserAgent_YieldsTheUnknownShape(string? ua) =>
        Assert.Equal(UserAgentDetailsParser.Unknown, UserAgentDetailsParser.Parse(ua));

    // --- Language -----------------------------------------------------------

    [Theory]
    [InlineData("en-GB,en;q=0.9", "en-gb")]
    [InlineData("ar-EG,ar;q=0.9,en;q=0.8", "ar-eg")]
    // Quality ordering wins over header order.
    [InlineData("de;q=0.5,fr;q=0.9", "fr")]
    [InlineData("*", null)]
    [InlineData("", null)]
    public void PrimaryLanguage_IsTakenFromAcceptLanguage(string header, string? expected)
    {
        var context = new DefaultHttpContext();
        if (header.Length > 0)
        {
            context.Request.Headers.AcceptLanguage = header;
        }

        Assert.Equal(expected, HttpRequestFacts.Language(context.Request));
    }

    // --- Campaign attribution ----------------------------------------------

    [Fact]
    public void UtmParameters_AreCapturedFromTheQueryString()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(
            "?utm_source=newsletter&utm_medium=email&utm_campaign=v1-launch&utm_term=sql&utm_content=cta-top");

        var campaign = HttpRequestFacts.Campaign(context.Request);

        Assert.True(campaign.IsPresent);
        Assert.Equal("newsletter", campaign.Source);
        Assert.Equal("email", campaign.Medium);
        Assert.Equal("v1-launch", campaign.Campaign);
        Assert.Equal("sql", campaign.Term);
        Assert.Equal("cta-top", campaign.Content);
    }

    [Fact]
    public void NoUtmParameters_YieldsAnAbsentCampaign()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?page=2");

        Assert.False(HttpRequestFacts.Campaign(context.Request).IsPresent);
    }

    // --- Referrer -----------------------------------------------------------

    [Fact]
    public void SameOriginReferrers_AreDropped()
    {
        // Internal navigation is not an acquisition source; keeping it would swamp the table.
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("akml.khamis.work");
        context.Request.Headers.Referer = "https://akml.khamis.work/docs";

        Assert.Null(HttpRequestFacts.ReferrerUrl(context.Request));
    }

    [Fact]
    public void ExternalReferrers_KeepTheirPath()
    {
        // The host answers "who links to me"; the path answers "which post".
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("akml.khamis.work");
        context.Request.Headers.Referer = "https://news.example.com/posts/sql-tools?ref=digest";

        Assert.Equal("https://news.example.com/posts/sql-tools?ref=digest", HttpRequestFacts.ReferrerUrl(context.Request));
        Assert.Equal("news.example.com", HttpRequestFacts.ReferrerHost(context.Request));
    }

    // --- Sessions -----------------------------------------------------------

    [Fact]
    public void ConsecutiveVisits_ShareASession_AndAGapStartsANewOne()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        const string ip = "203.0.113.5";

        store.LogVisit(new VisitInfo(Now, "/", null, "Chrome", ip));
        store.LogVisit(new VisitInfo(Now.AddMinutes(2), "/docs", null, "Chrome", ip));
        store.LogVisit(new VisitInfo(Now.AddMinutes(5), "/download", null, "Chrome", ip));
        // Past the idle window: a new session.
        store.LogVisit(new VisitInfo(Now.AddMinutes(5 + AnalyticsStore.SessionIdleMinutes + 1), "/", null, "Chrome", ip));

        var summary = store.GetSummary(30, Now.AddHours(2));

        Assert.Equal(2, summary.Sessions);
        Assert.Equal(2, summary.PagesPerSession); // (3 + 1) / 2
        Assert.Equal(50, summary.BounceRatePercent); // the second session is a single page
    }

    [Fact]
    public void DifferentDevicesBehindOneAddress_DoNotShareASession()
    {
        // Sessions key on the IP hash AND the agent. Without the agent, a phone and a laptop on
        // one home connection merge into a single session, inflating pages-per-session and
        // hiding one of the two visitors.
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        const string sharedIp = "203.0.113.50";

        store.LogVisit(new VisitInfo(Now, "/", null, "Chrome", sharedIp));
        store.LogVisit(new VisitInfo(Now.AddMinutes(1), "/features", null, "Chrome", sharedIp));
        store.LogVisit(new VisitInfo(Now.AddMinutes(2), "/docs", null, "Safari", sharedIp));

        var summary = store.GetSummary(30, Now.AddMinutes(5));

        Assert.Equal(2, summary.Sessions);
    }

    [Fact]
    public void DifferentVisitors_NeverShareASession()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        store.LogVisit(new VisitInfo(Now, "/", null, "Chrome", "203.0.113.1"));
        store.LogVisit(new VisitInfo(Now.AddMinutes(1), "/", null, "Chrome", "203.0.113.2"));

        Assert.Equal(2, store.GetSummary(30, Now).Sessions);
    }

    [Fact]
    public void EntryAndExitPages_AreTheFirstAndLastOfEachSession()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        const string ip = "203.0.113.5";

        store.LogVisit(new VisitInfo(Now, "/docs/intellisense", null, "Chrome", ip));
        store.LogVisit(new VisitInfo(Now.AddMinutes(1), "/features", null, "Chrome", ip));
        store.LogVisit(new VisitInfo(Now.AddMinutes(2), "/download", null, "Chrome", ip));

        var summary = store.GetSummary(30, Now);

        // People arrive on a deep link far more often than on the home page.
        Assert.Equal("/docs/intellisense", summary.EntryPages.Single().Key);
        Assert.Equal("/download", summary.ExitPages.Single().Key);
    }

    // --- Dimensions round-trip ---------------------------------------------

    [Fact]
    public void EnrichedVisits_AggregateIntoTheirDimensions()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        store.LogVisit(new VisitInfo(Now, "/", null, "Chrome", "203.0.113.1")
        {
            UserAgent = new UserAgentDetails("Chrome", "120", "Windows", "10/11", "desktop"),
            Location = new GeoLocation("EG", "Egypt", "Cairo Governorate", "Cairo", "Africa/Cairo"),
            Language = "ar-eg",
            Campaign = new CampaignInfo("newsletter", "email", "v1-launch", null, null),
            ReferrerUrl = "https://news.example.com/post",
            DurationMs = 40,
        });
        store.LogVisit(new VisitInfo(Now.AddMinutes(1), "/features", null, "Safari", "198.51.100.1")
        {
            UserAgent = new UserAgentDetails("Safari", "17", "iOS", "17.2", "mobile"),
            Location = new GeoLocation("GB", "United Kingdom", "England", "London", "Europe/London"),
            Language = "en-gb",
            DurationMs = 10,
        });

        var summary = store.GetSummary(30, Now.AddMinutes(2));

        Assert.Contains(summary.Countries, r => r.Key == "Egypt" && r.Count == 1);
        Assert.Contains(summary.Countries, r => r.Key == "United Kingdom" && r.Count == 1);
        Assert.Contains(summary.Cities, r => r.Key == "Cairo, Cairo Governorate");
        Assert.Contains(summary.Devices, r => r.Key == "desktop" && r.Count == 1);
        Assert.Contains(summary.Devices, r => r.Key == "mobile" && r.Count == 1);
        Assert.Contains(summary.OperatingSystems, r => r.Key == "Windows 10/11");
        Assert.Contains(summary.OperatingSystems, r => r.Key == "iOS 17.2");
        Assert.Contains(summary.Languages, r => r.Key == "ar-eg");
        Assert.Contains(summary.Campaigns, r => r.Key.Contains("v1-launch", StringComparison.Ordinal));
        Assert.Contains(summary.ReferrerUrls, r => r.Key == "https://news.example.com/post");
    }

    [Fact]
    public void RowsWithoutEnrichment_AreOmittedRatherThanBucketedAsUnknown()
    {
        // History written before these columns existed must not dominate every table with a
        // meaningless top row.
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        store.LogVisit(new VisitInfo(Now, "/", null, "Chrome", "203.0.113.1")); // no location, no language

        var summary = store.GetSummary(30, Now);

        Assert.Empty(summary.Countries);
        Assert.Empty(summary.Languages);
        Assert.Empty(summary.Campaigns);
        // The visit itself is still counted.
        Assert.Equal(1, summary.VisitsToday);
    }

    [Fact]
    public void SlowestPages_NeedEnoughSamplesToBeMeaningful()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));

        // One slow outlier should not top the table on a single sample.
        store.LogVisit(new VisitInfo(Now, "/rare", null, "Chrome", "203.0.113.1") { DurationMs = 5000 });
        for (var i = 0; i < 3; i++)
        {
            store.LogVisit(new VisitInfo(Now.AddSeconds(i), "/common", null, "Chrome", "203.0.113.2") { DurationMs = 100 });
        }

        var slowest = store.GetSummary(30, Now.AddMinutes(1)).SlowestPages;

        Assert.DoesNotContain(slowest, r => r.Key == "/rare");
        Assert.Contains(slowest, r => r.Key == "/common" && r.Count == 100);
    }

    // --- Geo seam -----------------------------------------------------------

    [Fact]
    public void GeoLookup_WithoutADatabase_IsInertRatherThanFatal()
    {
        using var dir = new TempDirectory();
        using var geo = new GeoLookup(Path.Combine(dir.Path, "absent.mmdb"));

        Assert.False(geo.IsAvailable);
        Assert.Equal(GeoLocation.Unknown, geo.Locate("8.8.8.8"));
    }

    [Fact]
    public void GeoLookup_WithACorruptDatabase_DegradesInsteadOfThrowing()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "corrupt.mmdb");
        File.WriteAllText(path, "this is not a MaxMind database");

        using var geo = new GeoLookup(path);

        Assert.False(geo.IsAvailable);
        Assert.Equal(GeoLocation.Unknown, geo.Locate("8.8.8.8"));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.5")]
    [InlineData(null)]
    public void GeoLookup_ReturnsUnknownForAddressesNoDatabaseCovers(string? ip)
    {
        using var dir = new TempDirectory();
        using var geo = new GeoLookup(Path.Combine(dir.Path, "absent.mmdb"));

        Assert.Equal(GeoLocation.Unknown, geo.Locate(ip));
    }

    // --- Schema migration ---------------------------------------------------

    [Fact]
    public void AnOlderDatabase_GainsTheEnrichmentColumnsInPlace()
    {
        // The deployed site has months of history; adding columns must not reset it.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "analytics.db");

        using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE visits (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, utc TEXT NOT NULL, path TEXT NOT NULL,
                    referrer_host TEXT NULL, ua_family TEXT NULL, ip_hash TEXT NOT NULL);
                CREATE TABLE downloads (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, utc TEXT NOT NULL, file TEXT NOT NULL,
                    referrer_host TEXT NULL, ua_family TEXT NULL, ip_hash TEXT NOT NULL);
                INSERT INTO visits (utc, path, ua_family, ip_hash)
                VALUES ('2026-08-28T09:00:00.0000000Z', '/legacy', 'Chrome', 'deadbeef');
                """;
            command.ExecuteNonQuery();
        }

        using var store = new AnalyticsStore(path);
        store.LogVisit(new VisitInfo(Now, "/new", null, "Chrome", "203.0.113.1")
        {
            Location = new GeoLocation("EG", "Egypt", null, null, null),
        });

        var summary = store.GetSummary(30, Now);

        // Old row survives and is counted; new row carries the enrichment.
        Assert.Equal(2, summary.VisitsToday);
        Assert.Contains(summary.TopPages, r => r.Key == "/legacy");
        Assert.Contains(summary.Countries, r => r.Key == "Egypt" && r.Count == 1);
    }
}
