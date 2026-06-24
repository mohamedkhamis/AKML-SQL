using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// PR #247 regression tests — INTO keyword inside a string literal or SQL comment must not cause
/// <see cref="QueryRefreshSafety.IsSingleReadOnlySelect"/> to reject an otherwise valid read-only
/// SELECT. A genuine SELECT … INTO must still be rejected.
/// </summary>
public sealed class Pr247_QueryRefreshSafetyFix
{
    // ── Cases that should be ALLOWED after the fix ────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT 'jump into' AS x")]                        // INTO inside single-quoted literal
    [InlineData("SELECT 'fall into the trap' AS msg FROM dbo.T")] // INTO inside literal with table ref
    [InlineData("SELECT 1 -- jump into temp")]                     // INTO inside line comment
    [InlineData("SELECT 1 /* into */")]                            // INTO inside block comment
    [InlineData("SELECT id FROM t -- insert into log later")]      // INTO inside line comment at end
    [InlineData("SELECT 'it''s into that' AS x")]                  // INTO inside literal with '' escape
    public void Allows_into_inside_string_or_comment(string sql)
        => Assert.True(QueryRefreshSafety.IsSingleReadOnlySelect(sql));

    // ── Cases that must still be REJECTED ────────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT * INTO #t FROM dbo.T")]                    // genuine SELECT … INTO (temp table)
    [InlineData("SELECT id INTO newtable FROM dbo.T")]             // genuine SELECT … INTO (permanent table)
    [InlineData("SELECT 'x' INTO #s FROM dbo.T")]                  // literal present but INTO is still real
    public void Rejects_genuine_select_into(string sql)
        => Assert.False(QueryRefreshSafety.IsSingleReadOnlySelect(sql));
}
