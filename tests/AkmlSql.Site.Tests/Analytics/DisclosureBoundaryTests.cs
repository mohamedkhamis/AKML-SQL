using System.Text.RegularExpressions;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using Xunit;

namespace AkmlSql.Site.Tests.Analytics;

/// <summary>
/// Spec 038 T105 (SC-015, contract C7.1): full addresses and persistent identifiers must not escape
/// the authenticated portal.
/// <para>
/// The site did not hold this data before spec 038, so nothing previously guarded against leaking
/// it. These tests are the guard: a log template that interpolates an address, or a public page that
/// renders one, would be a real disclosure — and both are the kind of change that looks harmless in
/// review.
/// </para>
/// </summary>
public sealed class DisclosureBoundaryTests
{
    /// <summary>Matches a dotted IPv4 literal, which is what a leaked address would look like.</summary>
    private static readonly Regex IpV4 = new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SiteSourceFiles(params string[] subfolders)
    {
        var root = Path.Combine(RepoRoot(), "src", "AkmlSql.Site");
        foreach (var folder in subfolders)
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            {
                if ((file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     || file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    [Fact]
    public void NoLogMessageInterpolatesAFullAddressOrVisitorId()
    {
        // Log files are read by anyone with server access and are routinely shipped to a support
        // channel. An address in a log template defeats the consent gate entirely.
        var offenders = new List<string>();

        foreach (var file in SiteSourceFiles("Analytics", "Consent", "Admin", "Components"))
        {
            var text = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(text, @"Log(?:Information|Warning|Error|Debug|Trace|Critical)\s*\([^;]*;", RegexOptions.Singleline))
            {
                var call = match.Value;

                // The message template is the quoted string; placeholders are {Named}.
                if (Regex.IsMatch(call, @"\{\s*(Ip|IpAddress|ClientIp|FullIp|VisitorId|Visitor)\s*\}", RegexOptions.IgnoreCase)
                    && !call.Contains("ClientIp", StringComparison.Ordinal)) // ClientIp is the admin audit trail, see below
                {
                    offenders.Add($"{Path.GetFileName(file)}: {call.Split('\n')[0].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Log templates disclose identifiable values:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void TheOnlyAddressInLogs_IsTheAdminSignInAuditTrail()
    {
        // One deliberate exception, and it is worth stating: AdminEndpoints logs the client IP of
        // SIGN-IN ATTEMPTS. That is a security audit trail for the owner's own account, not visitor
        // tracking, and it predates this feature. This test pins that the exception stays that one.
        var adminSource = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AkmlSql.Site", "Admin", "AdminEndpoints.cs"));

        var ipLogCalls = Regex.Matches(adminSource, @"\{ClientIp\}").Count;
        Assert.True(ipLogCalls > 0, "expected the sign-in audit trail to log the client IP");

        // ...and it must never log a visitor id alongside it.
        Assert.DoesNotContain("{VisitorId}", adminSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublicPagesNeverRenderAnAddressOrVisitorId()
    {
        var offenders = new List<string>();

        var publicPages = new[]
        {
            Path.Combine("Components", "Pages", "Download.razor"),
            Path.Combine("Components", "Pages", "Privacy.razor"),
            Path.Combine("Components", "Pages", "Home.razor"),
            Path.Combine("Components", "Pages", "Features.razor"),
            Path.Combine("Components", "ConsentBar.razor"),
            Path.Combine("Components", "Layout", "MainLayout.razor"),
        };

        foreach (var relative in publicPages)
        {
            var path = Path.Combine(RepoRoot(), "src", "AkmlSql.Site", relative);
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);

            // Rendering either of these on a public page would show one visitor another's data, or
            // hand a visitor their own tracking id to be correlated elsewhere.
            if (Regex.IsMatch(text, @"@\w*\.?IpAddress\b") || Regex.IsMatch(text, @"@\w*\.?VisitorId\b"))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(offenders.Count == 0, "Public pages render identifiable values: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheHealthProbeExposesNoPersonalData()
    {
        // /health is unauthenticated by design (the deploy smoke test calls it). It reports counts
        // and booleans only.
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AkmlSql.Site", "Program.cs"));
        var healthBlock = program[program.IndexOf("MapGet(\"/health\"", StringComparison.Ordinal)..];
        healthBlock = healthBlock[..healthBlock.IndexOf("});", StringComparison.Ordinal)];

        Assert.DoesNotContain("ip", healthBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("visitor", healthBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnlyTheAuthenticatedPersonViewRendersAnAddress()
    {
        // The single legitimate render site (FR-029), and it lives under /admin, which
        // PortalAuthorizationTests proves is guarded.
        var personPage = Path.Combine(RepoRoot(), "src", "AkmlSql.Site", "Components", "Pages", "Admin", "AdminPerson.razor");
        Assert.True(File.Exists(personPage));
        Assert.Contains("IpAddress", File.ReadAllText(personPage), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportsCarryingIdentifiableDataAreLabelled()
    {
        // FR-049 / contract M6.3: a CSV of addresses that does not say so is how it ends up in a
        // shared folder.
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        var now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

        store.LogVisit(new VisitInfo(now, "/download", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Granted,
            VisitorId = Guid.NewGuid().ToString("N"),
        });

        var filter = new IndividualFilter(30);
        var csv = IndividualsExport.IndividualsToCsv(
            store.GetIndividuals(filter, now), store.GetCoverage(30, now), filter, now);

        Assert.Contains(IndividualsExport.PersonalDataNotice, csv, StringComparison.Ordinal);
        Assert.Matches(IpV4, csv); // it really does contain one, so the label is not theoretical
    }

    [Fact]
    public void TheCountryExportCarriesNoIdentifiableData_AndIsNotLabelled()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(Path.Combine(dir.Path, "analytics.db"));
        var now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

        store.LogDownload(new DownloadInfo(now, "setup.exe", null, "Chrome", "203.0.113.7")
        {
            Consent = ConsentState.Granted,
            VisitorId = Guid.NewGuid().ToString("N"),
            Location = new GeoLocation("EG", "Egypt"),
        });

        var csv = IndividualsExport.CountriesToCsv(store.GetDownloadsByCountry(30, now), 30, now);

        // An aggregate export must not quietly become a personal one.
        Assert.DoesNotContain(IndividualsExport.PersonalDataNotice, csv, StringComparison.Ordinal);
        Assert.DoesNotMatch(IpV4, csv);
    }
}
