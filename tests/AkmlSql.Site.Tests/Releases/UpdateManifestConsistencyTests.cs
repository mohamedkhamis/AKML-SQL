using System.Text.Json;
using Xunit;

namespace AkmlSql.Site.Tests.Releases;

/// <summary>
/// Spec 036 US5 / FR-036: the update channel and the download page must never drift apart.
/// <c>scripts/deploy-site-iis.ps1</c> emits <c>src/AkmlSql.Site/wwwroot/update-manifest.json</c>
/// from the same <c>$entry</c> object it prepends to <c>releases.json</c>, so the newest entry
/// and the manifest must name the same version, the same file and the same SHA-256.
///
/// The manifest is generated at deploy time, so it is absent on a machine that has never
/// deployed — the consistency assertions are skipped then (nothing to be inconsistent with),
/// and mandatory whenever the file exists.
/// </summary>
public sealed class UpdateManifestConsistencyTests
{
    [Fact]
    public void Manifest_AndNewestReleaseEntry_NameSameVersionFileAndHash()
    {
        var manifestPath = ManifestPath();
        if (!File.Exists(manifestPath))
        {
            return; // never deployed on this machine — see class summary
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var releases = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(WwwRoot(), "releases.json")));

        var newest = releases.RootElement.GetProperty("releases")[0];

        Assert.Equal(
            newest.GetProperty("version").GetString(),
            manifest.RootElement.GetProperty("version").GetString());

        Assert.Equal(
            newest.GetProperty("sha256Hash").GetString(),
            manifest.RootElement.GetProperty("sha256Hash").GetString());

        // Same file: releases.json carries the site-relative "downloads/<file>", the manifest
        // an absolute URL — compare the file names both name.
        var releaseFile = Path.GetFileName(
            newest.GetProperty("downloadUrl").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        var manifestUrl = new Uri(manifest.RootElement.GetProperty("downloadUrl").GetString()!);
        var manifestFile = Path.GetFileName(manifestUrl.LocalPath);
        Assert.Equal(releaseFile, manifestFile);
    }

    [Fact]
    public void Manifest_DownloadUrl_IsAlwaysAbsoluteHttps()
    {
        var manifestPath = ManifestPath();
        if (!File.Exists(manifestPath))
        {
            return; // never deployed on this machine — see class summary
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var downloadUrl = manifest.RootElement.GetProperty("downloadUrl").GetString();

        // Never a site-relative path: the updater cannot resolve one (FR-036, contract §2).
        Assert.True(
            Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
            $"update-manifest.json downloadUrl must be absolute HTTPS, got: {downloadUrl}");
    }

    private static string ManifestPath() => Path.Combine(WwwRoot(), "update-manifest.json");

    private static string WwwRoot() =>
        Path.Combine(RepositoryRoot(), "src", "AkmlSql.Site", "wwwroot");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "AkmlSql.Site", "AkmlSql.Site.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
