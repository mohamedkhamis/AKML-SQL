namespace AkmlSql.Site.Settings;

/// <summary>
/// The owner-editable settings that change public behaviour (spec 038 US2/US3).
/// <para>
/// Immutable: the store hands out a snapshot and replaces it wholesale on save, so a render that
/// has already started can never observe a half-applied change.
/// </para>
/// <para>
/// These deliberately do NOT live in <c>appsettings.json</c>. Writing to the deployed config file
/// triggers an IIS application restart on every settings change, which would turn a one-second
/// toggle into a cold start — re-creating the very defect US1 exists to fix.
/// </para>
/// </summary>
public sealed record SiteSettings
{
    /// <summary>Storage keys in the <c>site_settings</c> table.</summary>
    public const string VisibilityKey = "release_visibility";

    /// <summary>Storage key for the "latest N" count.</summary>
    public const string VisibilityCountKey = "release_visibility_count";

    /// <summary>Storage key for the identifiable-data retention period.</summary>
    public const string IdentifiableRetentionKey = "identifiable_retention_days";

    /// <summary>Smallest accepted identifiable retention, in days.</summary>
    public const int MinRetentionDays = 1;

    /// <summary>Largest accepted identifiable retention, in days (ten years).</summary>
    public const int MaxRetentionDays = 3650;

    /// <summary>Identifiable retention applied when nothing has been saved — the owner's 2026-09-12 choice.</summary>
    public const int DefaultRetentionDays = 365;

    /// <summary>How much release history the download page advertises.</summary>
    public ReleaseVisibilityMode Visibility { get; init; } = ReleaseVisibilityBounds.DefaultMode;

    /// <summary>Number of releases shown when <see cref="Visibility"/> is <see cref="ReleaseVisibilityMode.LatestN"/>.</summary>
    public int VisibilityCount { get; init; } = ReleaseVisibilityBounds.DefaultCount;

    /// <summary>
    /// Days after which <c>ip</c> and <c>visitor_id</c> are nulled in place (FR-037). The row itself
    /// survives so country and version totals for an old period still reconcile (SC-008).
    /// </summary>
    public int IdentifiableRetentionDays { get; init; } = DefaultRetentionDays;

    /// <summary>
    /// The values applied when nothing has been saved, and the fallback when the store cannot be
    /// read at all (FR-016a). Never "show every release".
    /// </summary>
    public static SiteSettings Defaults { get; } = new();

    /// <summary>
    /// Human-readable problems with this candidate, empty when it is acceptable.
    /// <para>
    /// Errors name the bound they violate. Out-of-range input is rejected, never silently clamped:
    /// clamping leaves the owner believing they saved something they did not (contract A5.3).
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (VisibilityCount < ReleaseVisibilityBounds.MinCount || VisibilityCount > ReleaseVisibilityBounds.MaxCount)
        {
            errors.Add(
                $"Number of releases to show must be between {ReleaseVisibilityBounds.MinCount} and " +
                $"{ReleaseVisibilityBounds.MaxCount}. You entered {VisibilityCount}.");
        }

        if (IdentifiableRetentionDays < MinRetentionDays || IdentifiableRetentionDays > MaxRetentionDays)
        {
            errors.Add(
                $"Retention must be between {MinRetentionDays} and {MaxRetentionDays} days. " +
                $"You entered {IdentifiableRetentionDays}.");
        }

        return errors;
    }
}
