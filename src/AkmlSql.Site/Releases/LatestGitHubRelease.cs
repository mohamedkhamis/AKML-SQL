using System.Collections.Concurrent;
using System.Text.Json;

namespace AkmlSql.Site.Releases;

/// <summary>Refreshes public release metadata off the request path; keeps the last usable installer.</summary>
public sealed class LatestGitHubRelease : BackgroundService
{
    public const string ApiUrl = "https://api.github.com/repos/mohamedkhamis/AKML-SQL/releases/latest";
    private const string AssetPrefix = "https://github.com/mohamedkhamis/AKML-SQL/releases/download/";
    private readonly HttpClient _http;
    private readonly ILogger<LatestGitHubRelease> _logger;
    private readonly string? _cachePath;
    private readonly ConcurrentDictionary<string, Release> _known = new(StringComparer.OrdinalIgnoreCase);
    private Release? _current;
    public Release? Current => Volatile.Read(ref _current);

    public LatestGitHubRelease(HttpClient http, ReleasesManifest manifest,
        ILogger<LatestGitHubRelease> logger, string? cachePath = null)
    {
        _http = http;
        _logger = logger;
        _cachePath = cachePath;
        if (manifest.Releases.FirstOrDefault() is { } seed && IsInstaller(seed)) Remember(seed);
        try
        {
            if (cachePath is not null && File.Exists(cachePath)
                && JsonSerializer.Deserialize<Release>(File.ReadAllText(cachePath)) is { } saved
                && IsInstaller(saved) && saved.SizeBytes > 0
                && (Current is null || saved.ReleasedAt > Current.ReleasedAt
                    || (saved.ReleasedAt == Current.ReleasedAt && CompareVersion(saved.Version, Current.Version) >= 0)))
                Remember(saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { _logger.LogWarning(ex, "Could not load cached installer metadata; using the release manifest."); }
    }

    private static int CompareVersion(string left, string right) =>
        Version.TryParse(left, out var a) && Version.TryParse(right, out var b) ? a.CompareTo(b) : 0;

    private void Remember(Release release)
    {
        _known[Path.GetFileName(release.DownloadUrl)] = release;
        Volatile.Write(ref _current, release);
    }

    public Release? Find(string? file) => file is not null && _known.TryGetValue(file, out var release) ? release : null;

    public IReadOnlyList<Release> WithHistory(ReleasesManifest manifest)
    {
        var current = Current;
        if (current is null) return manifest.Releases;
        current = current with { IsLatest = true,
            NotesSummary = manifest.Releases.FirstOrDefault(r => r.Version == current.Version)?.NotesSummary };
        return new[] { current }.Concat(manifest.Releases.Where(r => r.Version != current.Version)
            .Select(r => r with { IsLatest = false })).ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.UserAgent.ParseAdd("AKML-SQL-Site/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var release = Parse(json.RootElement);
            if (release is null) throw new JsonException("Latest release has no valid AKML installer asset.");
            Remember(release);
            if (_cachePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                var temporary = _cachePath + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(release), cancellationToken);
                File.Move(temporary, _cachePath, overwrite: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or KeyNotFoundException)
        { _logger.LogWarning(ex, "Latest GitHub release refresh failed; retaining the last usable installer."); }
    }

    internal static Release? Parse(JsonElement json)
    {
        if (json.GetProperty("draft").GetBoolean() || json.GetProperty("prerelease").GetBoolean()) return null;
        var tag = json.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var version = tag.TrimStart('v', 'V');
        var name = $"AKMLSQLSetup-{version}.exe";
        foreach (var asset in json.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name || asset.GetProperty("state").GetString() != "uploaded") continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || url != AssetPrefix + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name)) continue;
            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
            var hash = digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.Ordinal)
                && digest[7..].All(Uri.IsHexDigit) ? digest[7..] : "";
            return new Release
            {
                Version = version, ReleasedAt = DateOnly.FromDateTime(json.GetProperty("published_at").GetDateTimeOffset().UtcDateTime),
                SupportedHosts = ["SSMS 22"], DownloadUrl = "downloads/" + name, CdnUrl = url,
                SizeBytes = size, Sha256Hash = hash, MinimumOsVersion = "10.0.17763",   // the installer's MinVersion and the updater's platform floor
                ReleaseNotesUrl = "https://github.com/mohamedkhamis/AKML-SQL/releases/tag/" + Uri.EscapeDataString(tag),
            };
        }
        return null;
    }

    private static bool IsInstaller(Release release) => !string.IsNullOrWhiteSpace(release.CdnUrl)
        && Uri.TryCreate(release.CdnUrl, UriKind.Absolute, out var url)
        && url.Scheme == "https" && url.Host == "github.com" && url.IsDefaultPort && url.UserInfo.Length == 0
        && url.AbsoluteUri.StartsWith(AssetPrefix, StringComparison.Ordinal)
        && url.Query.Length == 0 && url.Fragment.Length == 0
        && Path.GetFileName(url.AbsolutePath) == $"AKMLSQLSetup-{release.Version}.exe";
}
