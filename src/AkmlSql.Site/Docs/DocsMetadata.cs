using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace AkmlSql.Site.Docs;

/// <summary>
/// Loader for the generated <c>wwwroot/docs-metadata.json</c> freshness feed
/// (scripts/generate-docs-metadata.ps1): per-doc git added/updated dates driving the
/// New/Updated badges. Same failure philosophy as the releases manifest loader — a
/// missing, unreadable, or malformed file collapses to <see cref="Empty"/> (no badges),
/// never an error page. Path keys match <see cref="Document.SourcePath"/>
/// (content-root-relative, forward slashes) case-insensitively.
/// </summary>
public sealed class DocsMetadata
{
    /// <summary>File name under the web root.</summary>
    public const string MetadataFileName = "docs-metadata.json";

    private readonly IReadOnlyDictionary<string, DocDates> _datesByPath;

    private DocsMetadata(DateTimeOffset? generatedAt, IReadOnlyDictionary<string, DocDates> datesByPath)
    {
        GeneratedAt = generatedAt;
        _datesByPath = datesByPath;
    }

    /// <summary>The shared fallback instance used when no usable metadata exists.</summary>
    public static DocsMetadata Empty { get; } = new(null, new Dictionary<string, DocDates>(StringComparer.OrdinalIgnoreCase));

    /// <summary>When the metadata file was generated, if present.</summary>
    public DateTimeOffset? GeneratedAt { get; }

    /// <summary>True when at least one doc has usable dates.</summary>
    public bool IsAvailable => _datesByPath.Count > 0;

    /// <summary>Looks up the dates for a content-root-relative path (forward slashes, case-insensitive).</summary>
    public bool TryGet(string relativePath, out DocDates dates)
    {
        dates = default;
        return relativePath is not null && _datesByPath.TryGetValue(relativePath, out dates);
    }

    /// <summary>
    /// Loads <c>docs-metadata.json</c> from the web root. Missing/unreadable files and any
    /// parse failure collapse to <see cref="Empty"/>.
    /// </summary>
    public static DocsMetadata Load(IWebHostEnvironment environment)
    {
        try
        {
            var file = environment.WebRootFileProvider.GetFileInfo(MetadataFileName);
            if (!file.Exists)
            {
                return Empty;
            }

            using var stream = file.CreateReadStream();
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// Parses the metadata JSON. Invalid JSON or a non-object root yields <see cref="Empty"/>;
    /// entries with missing/unparseable dates are skipped while valid ones survive.
    /// </summary>
    public static DocsMetadata Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // The root must be an object: "[]", "null", "42" parse fine but would throw
            // InvalidOperationException from TryGetProperty below, escaping the JsonException
            // catch (same C1 trap as the releases manifest).
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            DateTimeOffset? generatedAt = root.TryGetProperty("generatedAt", out var generatedAtElement)
                && generatedAtElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(generatedAtElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedGeneratedAt)
                    ? parsedGeneratedAt
                    : null;

            var dates = new Dictionary<string, DocDates>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("docs", out var docsElement) && docsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in docsElement.EnumerateObject())
                {
                    if (TryParseDates(property.Value, out var docDates))
                    {
                        dates[property.Name] = docDates;
                    }
                }
            }

            return dates.Count > 0 ? new DocsMetadata(generatedAt, dates) : Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// Badge for a doc with known dates: <see cref="DocBadge.New"/> when <c>added</c> is within
    /// the window (new-beats-updated precedence), else <see cref="DocBadge.Updated"/> when
    /// <c>updated</c> is. The window is inclusive: today and exactly <paramref name="windowDays"/>
    /// ago count, one day further back does not.
    /// </summary>
    public static DocBadge ComputeBadge(DocDates dates, DateOnly today, int windowDays)
    {
        if (IsWithinWindow(dates.Added, today, windowDays))
        {
            return DocBadge.New;
        }

        return IsWithinWindow(dates.Updated, today, windowDays) ? DocBadge.Updated : DocBadge.None;
    }

    private static bool IsWithinWindow(DateOnly date, DateOnly today, int windowDays) =>
        date >= today.AddDays(-windowDays);

    /// <summary>Deserializes one docs entry; required: parseable yyyy-MM-dd added + updated.</summary>
    private static bool TryParseDates(JsonElement entry, out DocDates dates)
    {
        dates = default;

        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("added", out var addedElement)
            || addedElement.ValueKind != JsonValueKind.String
            || !entry.TryGetProperty("updated", out var updatedElement)
            || updatedElement.ValueKind != JsonValueKind.String
            || !DateOnly.TryParse(addedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var added)
            || !DateOnly.TryParse(updatedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated))
        {
            return false;
        }

        dates = new DocDates(added, updated);
        return true;
    }
}
