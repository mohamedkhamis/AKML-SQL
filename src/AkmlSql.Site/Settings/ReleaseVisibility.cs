using AkmlSql.Site.Releases;

namespace AkmlSql.Site.Settings;

/// <summary>
/// How much release history the public download page advertises (spec 038 US2, FR-011).
/// <para>
/// The manifest is append-only and grows fast — 16 releases at the time this was written, ten of
/// them from a single day — so "show whatever the manifest holds" turned the download page into a
/// wall of near-identical build numbers. The owner chooses how much of it is published, and the
/// choice also bounds how much per-release work the page does (contract R2.1).
/// </para>
/// </summary>
public enum ReleaseVisibilityMode
{
    /// <summary>Only the newest downloadable release. The history section is not rendered at all.</summary>
    LatestOnly,

    /// <summary>The newest N downloadable releases, N from <c>release_visibility_count</c>.</summary>
    LatestN,

    /// <summary>Every downloadable release.</summary>
    All,
}

/// <summary>Bounds and defaults for <see cref="ReleaseVisibilityMode"/>.</summary>
public static class ReleaseVisibilityBounds
{
    /// <summary>Smallest accepted value for the "latest N" count.</summary>
    public const int MinCount = 1;

    /// <summary>
    /// Largest accepted value for the "latest N" count. A hand-typed 10,000 is user input, not an
    /// instruction — it is rejected with a message rather than silently clamped (FR-012).
    /// </summary>
    public const int MaxCount = 50;

    /// <summary>
    /// Count applied when nothing has been saved. FR-016: the default MUST NOT be "show every
    /// release" — the current 16-entry list is the defect, not the baseline.
    /// </summary>
    public const int DefaultCount = 3;

    /// <summary>Mode applied when nothing has been saved.</summary>
    public const ReleaseVisibilityMode DefaultMode = ReleaseVisibilityMode.LatestN;
}

/// <summary>Applies a visibility choice to a newest-first release list.</summary>
public static class ReleaseVisibility
{
    /// <summary>
    /// The releases the public page may advertise, as a prefix of <paramref name="newestFirst"/>.
    /// <para>
    /// Ordering is never changed — <see cref="ReleasesManifest"/> has already sorted newest-first and
    /// derived <c>IsLatest</c> from position, so this only ever selects a prefix (contract R2.2).
    /// A count larger than the list is not an error; all of them are shown (contract R2.5).
    /// </para>
    /// </summary>
    public static IReadOnlyList<Release> Apply(
        ReleaseVisibilityMode mode,
        int count,
        IReadOnlyList<Release>? newestFirst)
    {
        if (newestFirst is null || newestFirst.Count == 0)
        {
            return [];
        }

        var take = mode switch
        {
            ReleaseVisibilityMode.LatestOnly => 1,
            ReleaseVisibilityMode.All => newestFirst.Count,
            _ => count,
        };

        if (take >= newestFirst.Count)
        {
            return newestFirst;
        }

        return take <= 0 ? [] : newestFirst.Take(take).ToList();
    }

    /// <summary>Plain-language description of what a choice publishes, for the settings page (FR-011).</summary>
    public static string Label(ReleaseVisibilityMode mode, int count) => mode switch
    {
        ReleaseVisibilityMode.LatestOnly => "Latest release only",
        ReleaseVisibilityMode.All => "All releases",
        _ => count == 1 ? "Latest release only" : $"Latest {count} releases",
    };

    /// <summary>Longer explanation shown beside each option on the settings page.</summary>
    public static string Description(ReleaseVisibilityMode mode) => mode switch
    {
        ReleaseVisibilityMode.LatestOnly =>
            "The download page offers the current release and nothing else. No history section is shown.",
        ReleaseVisibilityMode.All =>
            "Every release in the manifest is offered. The page grows with each build you publish.",
        _ =>
            "The current release, plus a few recent ones listed below it.",
    };

    /// <summary>Parses the stored value; anything unrecognised falls back to the default mode.</summary>
    public static ReleaseVisibilityMode Parse(string? value) =>
        Enum.TryParse<ReleaseVisibilityMode>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ReleaseVisibilityBounds.DefaultMode;
}
