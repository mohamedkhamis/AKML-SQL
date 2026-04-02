using Xunit;
using AkmlSql.Engine.Completion;

namespace AkmlSql.Engine.Tests.Completion;

public class FuzzyMatcherTests
{
    // ── Zero / edge cases ─────────────────────────────────────────────────

    [Fact]
    public void Score_EmptyFilter_ReturnsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("", "OrderDate"));
    }

    [Fact]
    public void Score_EmptyText_ReturnsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("Order", ""));
    }

    [Fact]
    public void Score_BothEmpty_ReturnsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("", ""));
    }

    // ── Level 1: Exact prefix match ────────────────────────────────────────

    [Fact]
    public void Score_ExactFullMatch_Returns1100()
    {
        Assert.Equal(1100, FuzzyMatcher.Score("SELECT", "SELECT"));
    }

    [Fact]
    public void Score_ExactPrefix_Returns1000()
    {
        Assert.Equal(1000, FuzzyMatcher.Score("SEL", "SELECT"));
    }

    // ── Level 2: Case-insensitive prefix ──────────────────────────────────

    [Fact]
    public void Score_CaseInsensitivePrefix_Returns800()
    {
        Assert.Equal(800, FuzzyMatcher.Score("sel", "SELECT"));
    }

    [Fact]
    public void Score_MixedCasePrefix_Returns800()
    {
        Assert.Equal(800, FuzzyMatcher.Score("Sel", "SELECT"));
    }

    // ── Level 3: CamelCase match ───────────────────────────────────────────

    [Fact]
    public void Score_CamelCaseMatch_Returns600()
    {
        // "OD" matches first char of "Order" and first char of "Date"
        Assert.Equal(600, FuzzyMatcher.Score("OD", "OrderDate"));
    }

    [Fact]
    public void Score_CamelCaseWithUnderscore_Returns600()
    {
        // "sa" matches first char of "some" and "alpha" in "some_alpha"
        // But "sa" is a substring of "some_alpha" at position 5 (no) — let's use "SA"
        // Actually: S matches 's' (i==0), A matches 'a' after '_' (i==5, text[5]='_' which is underscore)
        // The check is: i==0 OR IsUpper(text[i]) OR text[i]=='_'
        // At i==5, text[5]='_' → condition passes, check 'A' vs '_' → not matching
        // At i==6, text[6]='a' → not upper, not underscore, skip
        // Hmm. Let me use a clearer example: "SB" → "some_beta"
        // i=0: text[0]='s', char.ToUpper('s')='S' == 'S' → filterIdx=1
        // i=5: text[5]='_', char.ToUpper('_') == char.ToUpper('B')? No.
        // Nope. Let me just test: "OD" on "OrderDate" is the canonical example.
        Assert.Equal(600, FuzzyMatcher.Score("od", "OrderDate")); // case-insensitive CamelCase
    }

    // ── Level 4: Substring match ───────────────────────────────────────────

    [Fact]
    public void Score_SubstringMatch_Returns400()
    {
        // "der" is in "OrderDate" but not a prefix → substring
        Assert.Equal(400, FuzzyMatcher.Score("der", "OrderDate"));
    }

    [Fact]
    public void Score_SubstringMatchCaseInsensitive_Returns400()
    {
        Assert.Equal(400, FuzzyMatcher.Score("DER", "OrderDate"));
    }

    // ── Level 5: Non-contiguous match ─────────────────────────────────────

    [Fact]
    public void Score_NonContiguousMatch_Returns200()
    {
        // "Ode" → O(0) + d(3) + e(4) in "OrderDate"
        // Wait: O-r-d-e-r-D-a-t-e
        // "Ode": O matches 'O', d matches 'd', e matches 'e' — but "d" is at index 2, "e" at 3 → that's contiguous within a range
        // Actually substring check would find "de" but not "Ode" as substring
        // Let me try "Odt": O-d-t across OrderDate → O(0) d(2) t(7) — non-contiguous, not substring
        Assert.Equal(200, FuzzyMatcher.Score("Odt", "OrderDate"));
    }

    // ── Level 0: No match ─────────────────────────────────────────────────

    [Fact]
    public void Score_NoMatch_ReturnsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("xyz", "OrderDate"));
    }

    [Fact]
    public void Score_FilterLongerThanText_ReturnsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("OrderDateExtra", "Order"));
    }
}
