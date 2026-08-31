using System.Collections.Concurrent;

namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Rules the user has switched off for the current session.
///
/// <para>
/// "Session" is the lifetime of the engine process, and the engine process is started per shell
/// instance (the named pipe is keyed on the shell PID), so this is exactly "until I close SSMS /
/// Visual Studio". Nothing is written to disk: that is the point of the scope — a way to silence a
/// rule while working without leaving a directive in the script or an entry in config.json.
/// </para>
///
/// <para>
/// Applied as a post-filter over the finished diagnostics rather than by removing the rule from the
/// run set, so it needs no invalidation of <c>AnalysisEngine</c>'s batch cache: cached batches keep
/// their diagnostics and the filter re-applies on every response, which also makes un-suppressing
/// take effect immediately.
/// </para>
/// </summary>
public sealed class SessionSuppressionStore
{
    // Value is unused; ConcurrentDictionary is the set with the concurrency guarantees we need.
    private readonly ConcurrentDictionary<string, byte> _rules =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Suppresses <paramref name="ruleId"/> for the rest of the session. No-op if blank.</summary>
    public void Add(string? ruleId)
    {
        var id = Normalize(ruleId);
        if (id is null) return;
        _rules[id] = 0;
    }

    /// <summary>Lifts the session suppression on <paramref name="ruleId"/>.</summary>
    public void Remove(string? ruleId)
    {
        var id = Normalize(ruleId);
        if (id is null) return;
        _rules.TryRemove(id, out _);
    }

    /// <summary>Lifts every session suppression.</summary>
    public void Clear() => _rules.Clear();

    /// <summary>True when <paramref name="ruleId"/> is suppressed for this session.</summary>
    public bool IsSuppressed(string? ruleId)
    {
        var id = Normalize(ruleId);
        return id is not null && _rules.ContainsKey(id);
    }

    /// <summary>How many rules are currently suppressed for the session.</summary>
    public int Count => _rules.Count;

    /// <summary>The suppressed rule ids, sorted, as a snapshot the caller owns.</summary>
    public string[] Snapshot()
    {
        var ids = _rules.Keys.ToArray();
        Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
        return ids;
    }

    private static string? Normalize(string? ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return null;
        return ruleId.Trim().ToUpperInvariant();
    }
}
