using System;
using System.Text.RegularExpressions;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 030 loop fix — decides whether the editor may auto-re-run a batch to refresh the results
/// grid after an inline-CRUD Apply. Re-running on the engine's PERSISTENT connection repeats every
/// side effect: a <c>CREATE TABLE #t; …; SELECT</c> batch errors ("object already exists") and an
/// <c>INSERT …; SELECT</c> batch would DUPLICATE the inserted rows. So the refresh is allowed only
/// for a single, unambiguously read-only SELECT.
/// </summary>
internal static class QueryRefreshSafety
{
    private static readonly Regex IntoKeyword = new(@"\bINTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// True only when <paramref name="sql"/> is a single statement that begins with SELECT and is
    /// not a <c>SELECT … INTO</c> (which creates a table). Deliberately conservative: a statement
    /// with an embedded ';' or the word "INTO" inside a string literal is treated as unsafe and
    /// returns false. It NEVER returns true for a mutating or multi-statement batch, so a refresh
    /// re-run can never repeat a side effect.
    /// </summary>
    public static bool IsSingleReadOnlySelect(string? sql)
    {
        var s = sql?.Trim() ?? string.Empty;
        if (s.Length == 0) return false;
        if (s.EndsWith(";", StringComparison.Ordinal)) s = s[..^1].TrimEnd();   // tolerate one trailing ';'
        if (s.IndexOf(';') >= 0) return false;                                  // multiple statements → unsafe
        if (!s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return false;
        return !IntoKeyword.IsMatch(s);                                         // exclude SELECT … INTO
    }
}
