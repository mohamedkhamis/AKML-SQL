using AkmlSql.Site.Analytics;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Spec 038 follow-up: proves the installed geo database actually resolves countries.
/// <para>
/// The site shipped for months with no geo database at all — 3,000+ visits, every one recorded with
/// no country — and nothing failed, because geo is an enrichment and its absence is handled
/// gracefully. That graceful degradation is exactly why the gap went unnoticed, so this test exists
/// to make "installed and working" an assertion rather than an assumption.
/// </para>
/// <para>
/// Skipped when no database is present, so a developer without one still gets a green run.
/// </para>
/// </summary>
public sealed class GeoDatabaseSmokeTests
{
    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AKML SQL Site",
        GeoLookup.DefaultFileName);

    private static bool Installed => File.Exists(DefaultPath);

    [Fact]
    public void TheDatabase_ResolvesWellKnownPublicAddresses()
    {
        if (!Installed)
        {
            // No database on this machine: the site degrades to "Unknown", which other tests cover.
            return;
        }

        using var geo = new GeoLookup(DefaultPath);
        Assert.True(geo.IsAvailable, "A database file exists but the reader could not open it.");

        // Stable, well-known allocations. Asserting the country CODE rather than the name keeps the
        // test independent of the provider's naming ("United States" vs "United States of America").
        // Verified against the installed database rather than assumed. Two corrections worth
        // keeping: 1.1.1.1 resolves to AU because Cloudflare's resolver address is registered to
        // APNIC in Australia, and 212.35.64.1 is a Jordanian allocation, not an Egyptian one.
        var cases = new (string Ip, string Expected)[]
        {
            ("8.8.8.8", "US"),         // Google DNS
            ("9.9.9.9", "US"),         // Quad9
            ("1.1.1.1", "AU"),         // Cloudflare -- APNIC registration, genuinely AU
            ("156.160.0.1", "EG"),     // TE Data, Egypt
        };

        foreach (var (ip, expected) in cases)
        {
            var located = geo.Locate(ip);
            Assert.Equal(expected, located.CountryCode);
            Assert.False(string.IsNullOrWhiteSpace(located.CountryName), $"{ip} resolved a code but no name.");
        }
    }

    [Fact]
    public void PrivateAndLoopbackAddresses_StayUnknown()
    {
        if (!Installed)
        {
            return;
        }

        using var geo = new GeoLookup(DefaultPath);

        // These are never in any geo database; the lookup is skipped so the logs are not full of
        // expected misses when testing locally.
        foreach (var ip in (string[])["127.0.0.1", "::1", "192.168.1.10", "10.0.0.5", "172.16.4.2"])
        {
            Assert.Equal(GeoLocation.Unknown, geo.Locate(ip));
        }
    }

    [Fact]
    public void GarbageInput_IsHandledWithoutThrowing()
    {
        if (!Installed)
        {
            return;
        }

        using var geo = new GeoLookup(DefaultPath);

        foreach (var ip in (string?[])[null, "", "not-an-ip", "999.999.999.999", "<script>"])
        {
            Assert.Equal(GeoLocation.Unknown, geo.Locate(ip));
        }
    }
}
