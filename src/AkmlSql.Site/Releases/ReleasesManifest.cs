using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace AkmlSql.Site.Releases;

/// <summary>
/// Loader for the checked-in <c>wwwroot/releases.json</c> download feed (spec 034 T012).
/// Behavior is pinned by specs/034-blazor-product-site/contracts/releases-json.md:
/// releases are re-ordered newest-first with <see cref="Release.IsLatest"/> derived from
/// position; entries that violate the schema are skipped individually; a missing/unreadable
/// file, invalid JSON, or an empty/all-invalid release list yields the unavailable fallback
/// (<see cref="IsAvailable"/> false) so the download page can render its friendly message.
/// </summary>
public sealed class ReleasesManifest
{
    private const string ManifestFileName = "releases.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private ReleasesManifest(string? product, DateTimeOffset? generatedAt, IReadOnlyList<Release> releases)
    {
        Product = product;
        GeneratedAt = generatedAt;
        Releases = releases;
    }

    /// <summary>The shared fallback instance used when no usable manifest exists.</summary>
    public static ReleasesManifest Unavailable { get; } = new(product: null, generatedAt: null, releases: []);

    /// <summary>Product display name from the manifest (e.g. "AKML SQL").</summary>
    public string? Product { get; }

    /// <summary>When the manifest was generated, if present.</summary>
    public DateTimeOffset? GeneratedAt { get; }

    /// <summary>Valid releases, newest first. Empty when unavailable.</summary>
    public IReadOnlyList<Release> Releases { get; }

    /// <summary>The newest valid release (element 0), or null when unavailable.</summary>
    public Release? Latest => Releases.Count > 0 ? Releases[0] : null;

    /// <summary>True when at least one valid release is available to render.</summary>
    public bool IsAvailable => Releases.Count > 0;

    /// <summary>
    /// Builds a manifest from already-parsed releases, applying newest-first ordering and
    /// deriving <see cref="Release.IsLatest"/>. An empty sequence yields the fallback state.
    /// </summary>
    public static ReleasesManifest Create(IEnumerable<Release> releases, string? product = null, DateTimeOffset? generatedAt = null) =>
        new(product, generatedAt, OrderNewestFirst(releases));

    /// <summary>
    /// Loads <c>releases.json</c> from the web root. Missing or unreadable files and any
    /// parse failure collapse to <see cref="Unavailable"/> — the contract demands a friendly
    /// page, never an error page.
    /// </summary>
    public static ReleasesManifest Load(IWebHostEnvironment environment)
    {
        try
        {
            var file = environment.WebRootFileProvider.GetFileInfo(ManifestFileName);
            if (!file.Exists)
            {
                return Unavailable;
            }

            using var stream = file.CreateReadStream();
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Unavailable;
        }
    }

    /// <summary>
    /// Parses the manifest JSON. Invalid JSON or a document with no valid releases yields
    /// <see cref="Unavailable"/>; schema-violating entries are skipped while valid ones survive.
    /// </summary>
    public static ReleasesManifest Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // The root must be an object: "[]", "null", "42", "\"x\"" parse fine but would
            // throw InvalidOperationException from TryGetProperty below, escaping the
            // JsonException catch and permanently failing the /download singleton (C1).
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Unavailable;
            }

            var product = root.TryGetProperty("product", out var productElement) && productElement.ValueKind == JsonValueKind.String
                ? productElement.GetString()
                : null;

            DateTimeOffset? generatedAt = root.TryGetProperty("generatedAt", out var generatedAtElement)
                && generatedAtElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(generatedAtElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedGeneratedAt)
                    ? parsedGeneratedAt
                    : null;

            var releases = new List<Release>();
            if (root.TryGetProperty("releases", out var releasesElement) && releasesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in releasesElement.EnumerateArray())
                {
                    if (TryParseRelease(entry, out var release))
                    {
                        releases.Add(release);
                    }
                }
            }

            return Create(releases, product, generatedAt);
        }
        catch (JsonException)
        {
            return Unavailable;
        }
    }

    /// <summary>
    /// Deserializes and validates one release entry. Required per the contract: non-empty
    /// version, parseable releasedAt, at least one supported host, non-empty downloadUrl,
    /// and a 64-char hex sha256Hash. Any violation skips the entry.
    /// </summary>
    private static bool TryParseRelease(JsonElement entry, out Release release)
    {
        release = null!;

        ReleaseDto? dto;
        try
        {
            dto = entry.Deserialize<ReleaseDto>(JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (dto is null
            || string.IsNullOrWhiteSpace(dto.Version)
            || !DateOnly.TryParse(dto.ReleasedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var releasedAt)
            || dto.SupportedHosts is null
            || dto.SupportedHosts.Count == 0
            || !IsAllowedUrl(dto.DownloadUrl)
            || !IsSha256Hex(dto.Sha256Hash))
        {
            return false;
        }

        release = new Release
        {
            Version = dto.Version,
            ReleasedAt = releasedAt,
            SupportedHosts = dto.SupportedHosts,
            DownloadUrl = dto.DownloadUrl!,
            Sha256Hash = dto.Sha256Hash!,
            // Optional: an invalid notes URL degrades to null instead of dropping the entry.
            ReleaseNotesUrl = IsAllowedUrl(dto.ReleaseNotesUrl) ? dto.ReleaseNotesUrl : null,
            NotesSummary = string.IsNullOrWhiteSpace(dto.NotesSummary) ? null : dto.NotesSummary,
            MinimumOsVersion = string.IsNullOrWhiteSpace(dto.MinimumOsVersion) ? null : dto.MinimumOsVersion,
        };
        return true;
    }

    /// <summary>
    /// URL validation (S1 — a hostile/compromised manifest must not reach the page): a URL is
    /// either an absolute <c>http:</c>/<c>https:</c> URI, or a strict site-relative path
    /// (leading <c>/</c> or the <c>downloads/</c> folder, no scheme/colon anywhere, and no
    /// protocol-relative <c>//</c> prefix). <c>javascript:</c>, <c>data:</c>, <c>file:</c>
    /// and <c>//evil/</c> are all rejected.
    /// </summary>
    internal static bool IsAllowedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https";
        }

        return (url.StartsWith('/') || url.StartsWith("downloads/", StringComparison.Ordinal))
            && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.Contains(':', StringComparison.Ordinal);
    }

    private static bool IsSha256Hex(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static IReadOnlyList<Release> OrderNewestFirst(IEnumerable<Release> releases) =>
        releases
            .OrderByDescending(r => r.ReleasedAt)
            .ThenByDescending(r => r.Version, VersionComparer.Instance)
            .Select((r, index) => r with { IsLatest = index == 0 })
            .ToList();

    /// <summary>Compares the repo's <c>1.YY.MMDD.HHmm</c> version strings via System.Version.</summary>
    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var parsedX = System.Version.TryParse(x, out var vx) ? vx : null;
            var parsedY = System.Version.TryParse(y, out var vy) ? vy : null;
            if (parsedX is not null && parsedY is not null)
            {
                return parsedX.CompareTo(parsedY);
            }

            return string.CompareOrdinal(x, y);
        }
    }

    /// <summary>Deserialization shape; camelCase property names via <see cref="JsonSerializerDefaults.Web"/>.</summary>
    private sealed class ReleaseDto
    {
        public string? Version { get; set; }
        public string? ReleasedAt { get; set; }
        public List<string>? SupportedHosts { get; set; }
        public string? DownloadUrl { get; set; }
        public string? Sha256Hash { get; set; }
        public string? ReleaseNotesUrl { get; set; }
        public string? NotesSummary { get; set; }
        public string? MinimumOsVersion { get; set; }
    }
}
