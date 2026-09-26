using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// The install instructions the download page publishes.
/// <para>
/// These are the only statements on the site that tell a visitor to run a command, and they are
/// the easiest kind of content to get quietly wrong: the page and the installer live in different
/// projects, nothing links them, and a switch renamed in the Inno script leaves the page confidently
/// instructing people to use a flag that no longer exists. The docs already drifted this way once —
/// CLAUDE.md documents <c>/TARGETS=20,22,2022</c>, while the installer has parsed
/// <c>ssms22</c> (and, until Visual Studio support was removed, <c>vs2026</c>) for some time.
/// </para>
/// <para>
/// So every switch the page prints is checked against the installer source. If the installer
/// changes, this fails rather than the page misleading someone.
/// </para>
/// </summary>
public sealed class DownloadInstructionsTests : IDisposable
{
    private readonly TempDirectory _downloads = new();

    public void Dispose() => _downloads.Dispose();

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

    private static string InstallerSource()
    {
        var installerDir = Path.Combine(RepoRoot(), "src", "AkmlSql.Installer");
        Assert.True(Directory.Exists(installerDir), $"Installer sources not found at {installerDir}.");

        // The switches are split across the main script and the environment scanner, so read the
        // whole set rather than guessing which file owns which flag.
        return string.Join(
            "\n",
            Directory.EnumerateFiles(installerDir, "*.iss").Select(File.ReadAllText));
    }

    private BunitContext NewCtx()
    {
        var release = new Release
        {
            Version = "1.2.0",
            ReleasedAt = new DateOnly(2026, 9, 13),
            SupportedHosts = ["SSMS 22", "VS 2026"],
            DownloadUrl = "downloads/AKMLSQLSetup-1.2.0.exe",
            Sha256Hash = new string('c', 64),
            MinimumOsVersion = "10.0",
        };

        File.WriteAllText(Path.Combine(_downloads.Path, "AKMLSQLSetup-1.2.0.exe"), "installer payload");

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(ReleasesManifest.Create([release], product: "AKML SQL"));
        ctx.Services.AddSingleton(new ReleaseAvailability(_downloads.Path));

        var store = new SiteSettingsStore(Path.Combine(_downloads.Path, "settings.db"));
        store.CreateTableIfMissing();
        store.Load();
        ctx.Services.AddSingleton(store);
        return ctx;
    }

    [Fact]
    public void TheSilentInstallCommand_UsesSwitchesTheInstallerActuallyParses()
    {
        using var ctx = NewCtx();
        var command = ctx.Render<Download>().Find(".download-cmd").TextContent;

        // It must be a command for the file the page is offering, not a generic placeholder.
        Assert.Contains("AKMLSQLSetup-1.2.0.exe", command, StringComparison.Ordinal);

        var installer = InstallerSource();
        foreach (var token in (string[])["/VERYSILENT", "/ACCEPTEULA", "/TARGETS="])
        {
            Assert.Contains(token, command, StringComparison.Ordinal);
            Assert.Contains(
                token.Trim('/', '='),
                installer,
                StringComparison.OrdinalIgnoreCase);
        }

        // The target names are the ones ApplySilentTargets matches on. "22" and "2026" alone would
        // pass a naive substring check against the script, so the full tokens are required.
        foreach (var target in (string[])["ssms22"])
        {
            Assert.Contains(target, command, StringComparison.Ordinal);
            Assert.Contains($"'{target}'", installer, StringComparison.Ordinal);
        }

        // Visual Studio is no longer a target: the page must not offer it.
        Assert.DoesNotContain("vs2026", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheInstallSteps_DoNotTellPeopleToCloseTheirEditorFirst()
    {
        // CloseApplications=yes with a filter for Ssms.exe means the installer offers to close
        // SSMS itself. Telling people to close them first would be busywork the
        // product does not require -- and the sort of instruction nobody ever revisits.
        var installer = InstallerSource();
        Assert.Contains("CloseApplications=yes", installer, StringComparison.Ordinal);

        using var ctx = NewCtx();
        var steps = ctx.Render<Download>().Find(".download-steps").TextContent;

        Assert.Contains("close", steps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Close SSMS before", steps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Visual Studio", steps, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheChecksumIsVisible_NotHiddenBehindADisclosure()
    {
        // It used to sit inside a collapsed <details>. A checksum nobody can see is a checksum
        // nobody checks, and the collapsed panel also made the page look like it had nothing in it.
        using var ctx = NewCtx();
        var cut = ctx.Render<Download>();

        var code = cut.Find("code#latest-sha256");
        Assert.Equal(new string('c', 64), code.TextContent.Trim());

        // No ancestor of the digest may be a <details>.
        for (var node = code.ParentElement; node is not null; node = node.ParentElement)
        {
            Assert.False(
                node.TagName.Equals("DETAILS", StringComparison.OrdinalIgnoreCase),
                "The SHA-256 digest is inside a <details>, so it is hidden until the visitor expands it.");
        }
    }

    [Fact]
    public void TheFactsPanelAnswersTheQuestionsAskedBeforeClicking()
    {
        // Version, size and host support decide whether this is the right file. They belong beside
        // the button; the previous layout had them three sections below it.
        using var ctx = NewCtx();
        var facts = ctx.Render<Download>().Find(".release-facts-panel").TextContent;

        foreach (var label in (string[])["Version", "Released", "Download size", "Supported hosts", "License", "File"])
        {
            Assert.Contains(label, facts, StringComparison.Ordinal);
        }

        Assert.Contains("1.2.0", facts, StringComparison.Ordinal);
        Assert.Contains("Windows 10 or later", facts, StringComparison.Ordinal);
        Assert.Contains("AKMLSQLSetup-1.2.0.exe", facts, StringComparison.Ordinal);
    }
}
