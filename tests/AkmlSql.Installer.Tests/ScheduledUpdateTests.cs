using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AkmlSql.Core.Update;
using AkmlSql.Updater;
using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// The scheduled update task's updater modes: <c>--scheduled</c> (check, download, notify once),
/// <c>--install</c> (launch only a verified installer from the user's own cache) and
/// <c>--configure</c> (the installer's options page, written as the signed-in user).
/// Everything runs in-proc against temp paths; no network, no notification, no process start.
/// </summary>
public sealed class ScheduledUpdateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "akml-scheduled-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_root, "config.json");
    private string ResultPath => Path.Combine(_root, "update-available.json");
    private string CacheDir => Path.Combine(_root, "cache");

    public ScheduledUpdateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

    private string SeedVerifiedInstaller(string version = "1.26.1001.0900", DateTimeOffset? notifiedAt = null)
    {
        Directory.CreateDirectory(CacheDir);
        var path = Path.Combine(CacheDir, $"AKMLSQLSetup-{version}.exe");
        var bytes = Encoding.ASCII.GetBytes("installer " + version);
        File.WriteAllBytes(path, bytes);
        UpdateResultStore.SaveAtomic(new UpdateResult
        {
            Available = true,
            Version = version,
            DownloadUrl = "https://example.com/setup.exe",
            ReleaseNotesUrl = "https://example.com/notes",
            Sha256Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            DownloadState = UpdateDownloadStates.Verified,
            VerifiedInstallerPath = path,
            NotifiedAt = notifiedAt,
        }, ResultPath);
        return path;
    }

    private void SeedOffer(string version = "1.26.1001.0900") =>
        UpdateResultStore.SaveAtomic(new UpdateResult
        {
            Available = true,
            Version = version,
            DownloadUrl = "https://example.com/setup.exe",
            Sha256Hash = "00",
        }, ResultPath);

    private const string Installed = "1.26.0923.0723";

    private ScheduledUpdate Scheduled(Counter counter, bool notifyWorks = true, Func<Task>? check = null,
        Func<CancellationToken, Task<int>>? download = null) =>
        new(ConfigPath, ResultPath, Installed,
            check ?? (() => { counter.Checks++; return Task.CompletedTask; }),
            download ?? (_ => { counter.Downloads++; return Task.FromResult(0); }),
            new RecordingNotifications(counter, notifyWorks),
            () => Now);

    private sealed class Counter
    {
        public int Checks;
        public int Downloads;
        public List<string> Notified { get; } = [];
        public List<string> Said { get; } = [];
    }

    private sealed class RecordingNotifications(Counter counter, bool readyWorks) : IUpdateNotifications
    {
        public bool Ready(string version) { counter.Notified.Add(version); return readyWorks; }
        public void UpToDate(string currentVersion) => counter.Said.Add("up to date " + currentVersion);
        public void CouldNotCheck() => counter.Said.Add("could not check");
        public void CouldNotDownload(string version) => counter.Said.Add("could not download " + version);
    }

    // --- --scheduled -----------------------------------------------------------------------

    [Fact]
    public async Task TurnedOff_DoesNothingAtAll()
    {
        WriteConfig("""{ "autoUpdateEnabled": false }""");
        SeedVerifiedInstaller();
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);

        Assert.Equal(0, counter.Checks);
        Assert.Empty(counter.Notified);
    }

    [Fact]
    public async Task NoConfigYet_CountsAsOn()
    {
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);

        Assert.Equal(1, counter.Checks);
    }

    [Fact]
    public async Task ARecentCheck_IsNotRepeated()
    {
        WriteConfig($$"""{ "lastUpdateCheck": "{{Now.AddHours(-3):O}}" }""");
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);

        Assert.Equal(0, counter.Checks);
    }

    [Fact]
    public async Task AnOlderCheck_IsRepeated()
    {
        WriteConfig($$"""{ "lastUpdateCheck": "{{Now.AddHours(-13):O}}" }""");
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);

        Assert.Equal(1, counter.Checks);
    }

    [Fact]
    public async Task AnOffer_IsDownloaded()
    {
        SeedOffer();
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);

        Assert.Equal(1, counter.Downloads);
        Assert.Empty(counter.Notified); // the stub download verified nothing
    }

    [Fact]
    public async Task AVerifiedUpdate_IsAnnouncedOnce()
    {
        SeedVerifiedInstaller("1.26.1001.0900");
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);
        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);

        Assert.Equal(["1.26.1001.0900"], counter.Notified);
        Assert.Equal(0, counter.Downloads);
        Assert.Equal(Now, UpdateResultStore.Load(ResultPath)!.NotifiedAt);
    }

    [Fact]
    public async Task ANotificationWindowsRefused_IsTriedAgainNextRun()
    {
        SeedVerifiedInstaller();
        var counter = new Counter();

        await Scheduled(counter, notifyWorks: false).RunAsync(interactive: false, CancellationToken.None);

        Assert.Null(UpdateResultStore.Load(ResultPath)!.NotifiedAt);
    }

    [Fact]
    public async Task NoNetwork_IsQuiet_AndAReadyUpdateIsStillAnnounced()
    {
        SeedVerifiedInstaller();
        var counter = new Counter();

        var exit = await Scheduled(counter, check: () => throw new HttpRequestException("offline"))
            .RunAsync(interactive: false, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Single(counter.Notified);
    }

    [Fact]
    public async Task AVerifiedFileThatDisappeared_IsDownloadedAgain()
    {
        var path = SeedVerifiedInstaller();
        File.Delete(path);
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);

        Assert.Equal(1, counter.Downloads);
    }

    [Fact]
    public void ARecheckOfTheSameVersion_KeepsItsNotifiedAndPromptedTimes()
    {
        var existing = new UpdateResult
        {
            Available = true, Version = "2.0", NotifiedAt = Now, ShellPromptedAt = Now.AddHours(1),
            DownloadState = UpdateDownloadStates.Verified,
        };
        var fresh = new UpdateResult { Available = true, Version = "2.0" };
        var newer = new UpdateResult { Available = true, Version = "2.1" };

        UpdateResultStore.CarryForwardDownloadState(fresh, existing);
        UpdateResultStore.CarryForwardDownloadState(newer, existing);

        Assert.Equal(Now, fresh.NotifiedAt);
        Assert.Equal(Now.AddHours(1), fresh.ShellPromptedAt);
        Assert.Null(newer.NotifiedAt); // a new version is news again
    }

    // --- --install ---------------------------------------------------------------------------

    private (InstallLauncher Launcher, List<string> Started) Launcher()
    {
        var started = new List<string>();
        return (new InstallLauncher(ResultPath, CacheDir, Installed, info => { started.Add(info.FileName); return true; }), started);
    }

    [Fact]
    public void Install_LaunchesTheVerifiedInstaller()
    {
        var path = SeedVerifiedInstaller();
        var (launcher, started) = Launcher();

        Assert.Equal(0, launcher.Run("install"));
        Assert.Equal([Path.GetFullPath(path)], started);
    }

    [Fact]
    public void Install_RefusesAFileThatChangedSinceItWasVerified()
    {
        var path = SeedVerifiedInstaller();
        File.WriteAllText(path, "tampered");
        var (launcher, started) = Launcher();

        Assert.Equal(2, launcher.Run("install"));
        Assert.DoesNotContain(started, s => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(UpdateDownloadStates.Failed, UpdateResultStore.Load(ResultPath)!.DownloadState);
    }

    [Fact]
    public void Install_RefusesAPathOutsideTheUpdateCache()
    {
        SeedVerifiedInstaller();
        var result = UpdateResultStore.Load(ResultPath)!;
        var outside = Path.Combine(_root, "elsewhere.exe");
        File.Copy(result.VerifiedInstallerPath!, outside);
        result.VerifiedInstallerPath = outside;
        UpdateResultStore.SaveAtomic(result, ResultPath);
        var (launcher, started) = Launcher();

        Assert.Equal(2, launcher.Run("install"));
        Assert.Empty(started);
    }

    [Fact]
    public void Install_WithNothingReady_OpensTheDownloadPage()
    {
        var (launcher, started) = Launcher();

        Assert.Equal(0, launcher.Run("install"));
        Assert.Equal(["https://akml.khamis.work/download"], started);
    }

    [Fact]
    public void Details_OpensTheReleaseNotes()
    {
        SeedVerifiedInstaller();
        var (launcher, started) = Launcher();

        launcher.Run("details");

        Assert.Equal(["https://example.com/notes"], started);
    }

    [Theory]
    [InlineData("akmlsql-update:install", "install")]
    [InlineData("akmlsql-update:details", "details")]
    [InlineData("akmlsql-update://details/", "details")]
    [InlineData("akmlsql-update:C:\\evil.exe", "install")]
    [InlineData(null, "install")]
    public void TheUrlOnlyChoosesAnAction_NeverAFile(string? argument, string expected)
    {
        Assert.Equal(expected, InstallLauncher.ActionFrom(argument));
    }

    // --- --configure -------------------------------------------------------------------------

    [Fact]
    public void Configure_CreatesTheFile_WhenThereIsNone()
    {
        var values = UpdaterPreferences.ParseConfigureArgs(["auto-update=on", "error-reports=off"], out _)!;

        UpdaterPreferences.Write(ConfigPath, values);

        var config = JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();
        Assert.True(config["autoUpdateEnabled"]!.GetValue<bool>());
        Assert.False(config["telemetryEnabled"]!.GetValue<bool>());
        Assert.Equal(1, config["configVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Configure_ChangesOnlyWhatItWasGiven()
    {
        WriteConfig("""{ "configVersion": 1, "telemetryEnabled": false, "theme": "dark", "formatter": { "activeProfile": "Khamis" } }""");

        UpdaterPreferences.Write(ConfigPath, UpdaterPreferences.ParseConfigureArgs(["auto-update=off"], out _)!);

        var config = JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();
        Assert.False(config["autoUpdateEnabled"]!.GetValue<bool>());
        Assert.False(config["telemetryEnabled"]!.GetValue<bool>());
        Assert.Equal("dark", config["theme"]!.GetValue<string>());
        Assert.Equal("Khamis", config["formatter"]!["activeProfile"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("auto-update=maybe")]
    [InlineData("telemetry=on")]
    [InlineData("auto-update")]
    public void Configure_RejectsAnythingItDoesNotKnow(string argument)
    {
        Assert.Null(UpdaterPreferences.ParseConfigureArgs([argument], out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Preferences_DefaultToOn_AndReadWhatIsThere()
    {
        Assert.True(UpdaterPreferences.Read(ConfigPath).AutoUpdateEnabled);
        Assert.True(UpdaterPreferences.Read(ConfigPath).ErrorReportsEnabled);

        WriteConfig("""{ "autoUpdateEnabled": false, "telemetryEnabled": false }""");

        Assert.False(UpdaterPreferences.Read(ConfigPath).AutoUpdateEnabled);
        Assert.False(UpdaterPreferences.Read(ConfigPath).ErrorReportsEnabled);
    }

    // --- the notification ----------------------------------------------------------------------

    [Fact]
    public void TheNotification_IsValidXml_WithBothActionsOnTheUpdateScheme()
    {
        var toast = XElement.Parse(UpdateToast.ReadyXml("1.26.1001.0900"));

        Assert.Equal("akmlsql-update:details", toast.Attribute("launch")!.Value);
        Assert.Contains("AKML SQL 1.26.1001.0900 is ready to install", toast.ToString(), StringComparison.Ordinal);
        var actions = toast.Descendants("action").ToList();
        Assert.Equal("akmlsql-update:install", actions[0].Attribute("arguments")!.Value);
        Assert.Equal("dismiss", actions[1].Attribute("arguments")!.Value);
    }

    [Fact]
    public void TheNotification_EscapesTheVersion()
    {
        // The version comes from the network; it must never be able to change the XML's shape.
        var toast = XElement.Parse(UpdateToast.ReadyXml("1.0\"/><action arguments=\"x"));

        Assert.Equal(2, toast.Descendants("action").Count());
    }

    // --- after an update, and the Start-menu check ---------------------------------------------

    [Fact]
    public async Task AnOfferForTheInstalledVersion_IsCleared_NotAnnounced()
    {
        SeedVerifiedInstaller(Installed);
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: false, CancellationToken.None);

        Assert.Empty(counter.Notified);
        Assert.False(File.Exists(ResultPath));
    }

    [Fact]
    public void Install_OfTheInstalledVersion_OpensTheDownloadPageInstead()
    {
        SeedVerifiedInstaller(Installed);
        var (launcher, started) = Launcher();

        launcher.Run("install");

        Assert.Equal(["https://akml.khamis.work/download"], started);
    }

    [Fact]
    public async Task CheckNow_ChecksEvenWhenAutomaticUpdatesAreOff_AndSaysUpToDate()
    {
        WriteConfig($$"""{ "autoUpdateEnabled": false, "lastUpdateCheck": "{{Now.AddMinutes(-5):O}}" }""");
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: true, CancellationToken.None);

        Assert.Equal(1, counter.Checks);
        Assert.Equal(["up to date " + Installed], counter.Said);
    }

    [Fact]
    public async Task CheckNow_SaysWhenItCouldNotCheck()
    {
        var counter = new Counter();

        await Scheduled(counter, check: () => throw new HttpRequestException("offline"))
            .RunAsync(interactive: true, CancellationToken.None);

        Assert.Equal(["could not check"], counter.Said);
    }

    [Fact]
    public async Task CheckNow_AnnouncesAReadyUpdateAgain_EvenIfAlreadyAnnounced()
    {
        SeedVerifiedInstaller(notifiedAt: Now.AddDays(-1));
        var counter = new Counter();

        await Scheduled(counter).RunAsync(interactive: true, CancellationToken.None);

        Assert.Single(counter.Notified);
    }

    [Fact]
    public void TheOtherNotifications_AreValidXml()
    {
        var up = XElement.Parse(UpdateToast.MessageXml("AKML SQL is up to date", "You have the latest version, 1.0."));

        Assert.Equal(2, up.Descendants("text").Count());
    }
}
