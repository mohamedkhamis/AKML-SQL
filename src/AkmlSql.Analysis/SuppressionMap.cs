namespace AkmlSql.Engine.Analysis;

/// <summary>
/// A contiguous range of lines over which a set of rules — or every rule — is suppressed.
/// </summary>
/// <param name="StartLine">First suppressed line (1-based, inclusive).</param>
/// <param name="EndLine">Last suppressed line (inclusive). <see cref="int.MaxValue"/> = to end of file.</param>
/// <param name="RuleIds">
/// The rules this range covers, or <see langword="null"/> for "every rule". Sets built by
/// <see cref="SuppressionParser"/> are <see cref="StringComparer.OrdinalIgnoreCase"/>.
/// </param>
public readonly record struct SuppressionRange(int StartLine, int EndLine, HashSet<string>? RuleIds)
{
    /// <summary>True when <paramref name="ruleId"/> on <paramref name="line"/> falls in this range.</summary>
    public bool Covers(int line, string ruleId) =>
        line >= StartLine && line <= EndLine && (RuleIds is null || RuleIds.Contains(ruleId));

    /// <summary>
    /// Shorthand for an all-rules range, so <c>SuppressedBlocks.Add((10, 20))</c> keeps working
    /// for callers that predate rule-scoped ranges (the legacy <c>-- noqa-begin/end</c> form).
    /// </summary>
    public static implicit operator SuppressionRange((int StartLine, int EndLine) range) =>
        new(range.StartLine, range.EndLine, null);
}

/// <summary>
/// The suppressions found in one document: per-line and per-range, each either rule-scoped or
/// blanket. Built by <see cref="SuppressionParser"/> and consulted by the analysis pipeline
/// after the rules have run.
/// </summary>
public class SuppressionMap
{
    /// <summary>Line → set of suppressed rule IDs. Null set means all rules suppressed.</summary>
    public Dictionary<int, HashSet<string>?> SuppressedLines { get; } = new();

    /// <summary>Line ranges (inclusive on both ends), each covering specific rules or all rules.</summary>
    public List<SuppressionRange> SuppressedBlocks { get; } = [];

    /// <summary>
    /// Records a line suppression, MERGING with anything already recorded for that line so two
    /// directives on one line (say a <c>-- noqa: PE001</c> and an <c>-- akml-disable-line BP004</c>)
    /// both take effect instead of the second silently replacing the first. A blanket suppression
    /// (<paramref name="ruleIds"/> null) always wins.
    /// </summary>
    public void SuppressLine(int line, HashSet<string>? ruleIds)
    {
        if (!SuppressedLines.TryGetValue(line, out var existing))
        {
            SuppressedLines[line] = ruleIds;
            return;
        }

        if (existing is null) return;               // already blanket — nothing to add
        if (ruleIds is null) { SuppressedLines[line] = null; return; }

        foreach (var id in ruleIds) existing.Add(id);
    }

    public bool IsSuppressed(int line, string ruleId)
    {
        foreach (var range in SuppressedBlocks)
        {
            if (range.Covers(line, ruleId)) return true;
        }

        if (SuppressedLines.TryGetValue(line, out var ruleSet))
            return ruleSet == null || ruleSet.Contains(ruleId);

        return false;
    }

    public static SuppressionMap Empty { get; } = new();
}
