using Xunit;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Tests.Parser;

/// <summary>
/// Pins the cost of the completion repair ladder. A full <c>TSql170Parser</c> pass is the most
/// expensive thing the keystroke path does, and <c>ParseWithSuffix</c> escalates through several
/// repair attempts when the document does not parse cleanly — which, mid-typing, is the norm
/// rather than the exception.
///
/// <para>The bug this locks down: with the caret at end-of-document (the dominant typing
/// position), <c>RepairAtCursor(sql, sql.Length)</c> returns <c>repairedPrefix + ""</c> — byte
/// for byte what <c>AppendDummyTokens(sql)</c> already produced. The guard compared the result
/// against <c>sql</c> and never against the already-parsed <c>suffixed</c>, so the ladder spent a
/// whole extra parse re-parsing an identical string.</para>
/// </summary>
public class ParseLadderCostTests
{
    /// <summary>
    /// Unclosed paren: every repair variant still fails to parse, so the ladder runs to the end
    /// and its full cost is observable (an early success would mask the duplicate).
    /// </summary>
    private const string UnparseableTail = "SELECT * FROM (SELECT ";

    [Fact]
    public void CaretAtEndOfDocument_SkipsTheByteIdenticalReparse()
    {
        var service = new TsqlParserService();

        service.ParseWithSuffix(UnparseableTail, UnparseableTail.Length, out _);

        // 1) the original, 2) the tail-repaired variant, 3) the double-dummy combination.
        // The caret-repair attempt is skipped: it is identical to (2), which already failed.
        Assert.Equal(3, service.ParseCount);
    }

    [Fact]
    public void CleanSql_ParsesExactlyOnce()
    {
        var service = new TsqlParserService();
        const string sql = "SELECT a FROM dbo.T;";

        service.ParseWithSuffix(sql, sql.Length, out _);

        Assert.Equal(1, service.ParseCount);
    }

    /// <summary>
    /// A caret in the MIDDLE still gets its distinct repair attempt — the skip must key on the
    /// strings actually being equal, not on "the caret repair is redundant in general".
    /// </summary>
    [Fact]
    public void CaretInMiddle_StillAttemptsCaretRepair()
    {
        var service = new TsqlParserService();
        const string prefix = "SELECT * FROM (SELECT ";
        const string sql = prefix + " FROM T WHERE";

        service.ParseWithSuffix(sql, prefix.Length, out _);

        Assert.True(service.ParseCount >= 3,
            $"caret-repair attempt should still run for a mid-document caret (was {service.ParseCount})");
    }
}
