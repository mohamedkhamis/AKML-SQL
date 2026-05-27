namespace AkmlSql.Web.Tests.Parity;

/// <summary>
/// Spec 024 T006 — accepted-with-reason registry for parity divergences.
///
/// When the web edition's formatter or analyser output diverges from the desktop
/// baseline AND the divergence is a known, documented limitation (typically tracked
/// against a spec-020 tasks.md entry), the divergence is registered here so the
/// parity test treats it as <c>ACCEPTED_WITH_REASON</c> rather than a regression
/// failure. Every entry MUST carry a non-empty <c>ReasonLink</c> per FR-008 / FR-011.
///
/// Starts empty. Entries are added during US2 / US3 triage (tasks T021 / T024) as
/// real divergences are surfaced and explained.
/// </summary>
public static class ParityDispositionsRegistry
{
    /// <summary>Disposition entry. <c>RuleId</c> is null for formatter dispositions and non-null for analyser.</summary>
    public sealed record Entry(string CorpusId, string ProfileId, string? RuleId, string ReasonLink);

    /// <summary>
    /// The accepted-with-reason set. To add: append a new <see cref="Entry"/> with a
    /// <c>ReasonLink</c> pointing at a <c>specs/020-sqlprompt-visual-parity/tasks.md</c>
    /// entry (e.g. <c>"specs/020-sqlprompt-visual-parity/tasks.md#t074"</c>) or an
    /// equivalent recorded limitation in <c>doc/progress.md</c>.
    /// </summary>
    private static readonly Entry[] _entries = Array.Empty<Entry>();

    /// <summary>
    /// Returns the <c>ReasonLink</c> if the (corpus, profile, [rule]) tuple is registered as
    /// accepted-with-reason, or <c>null</c> if no entry matches (= a true failure).
    /// </summary>
    public static string? AcceptedReason(string corpusId, string profileId, string? ruleId = null)
    {
        foreach (var e in _entries)
        {
            if (!string.Equals(e.CorpusId, corpusId, StringComparison.Ordinal)) continue;
            if (!string.Equals(e.ProfileId, profileId, StringComparison.Ordinal)) continue;
            if (e.RuleId is null && ruleId is null) return e.ReasonLink;
            if (e.RuleId is not null && string.Equals(e.RuleId, ruleId, StringComparison.Ordinal))
            {
                return e.ReasonLink;
            }
        }
        return null;
    }

    /// <summary>Exposed for diagnostics / progress reporting.</summary>
    public static IReadOnlyList<Entry> All => _entries;
}
