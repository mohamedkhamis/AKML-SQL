namespace AkmlSql.Engine.Analysis;

public class SuppressionMap
{
    /// <summary>Line → set of suppressed rule IDs. Null set means all rules suppressed.</summary>
    public Dictionary<int, HashSet<string>?> SuppressedLines { get; } = new();

    /// <summary>Line ranges where all rules are suppressed (inclusive on both ends).</summary>
    public List<(int StartLine, int EndLine)> SuppressedBlocks { get; } = [];

    public bool IsSuppressed(int line, string ruleId)
    {
        // Check blocks
        foreach (var (start, end) in SuppressedBlocks)
        {
            if (line >= start && line <= end)
                return true;
        }

        // Check per-line suppression
        if (SuppressedLines.TryGetValue(line, out var ruleSet))
            return ruleSet == null || ruleSet.Contains(ruleId);

        return false;
    }

    public static SuppressionMap Empty { get; } = new();
}
