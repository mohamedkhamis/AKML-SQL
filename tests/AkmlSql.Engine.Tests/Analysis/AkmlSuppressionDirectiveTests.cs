using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// The <c>-- akml-disable</c> directive family — the syntax the user guide, the analysis-rules
/// reference, the configuration reference and the in-product Options page have always documented,
/// but which <see cref="SuppressionParser"/> did not implement (it understood only <c>-- noqa</c>).
/// Typing the documented comment silently did nothing.
///
/// <para>
/// These cover the three scopes the directives express: one line, a range, and — via a disable with
/// no matching enable — the whole script.
/// </para>
/// </summary>
public sealed class AkmlSuppressionDirectiveTests
{
    private static readonly TsqlParserService Parser;

    static AkmlSuppressionDirectiveTests()
    {
        Parser = new TsqlParserService();
        Parser.SetServerVersion(160);
    }

    private static (SuppressionMap Map, List<AnalysisDiagnostic> Meta) Parse(string sql)
    {
        var tokens = Parser.GetTokenStream(sql);
        var map = SuppressionParser.Parse(tokens, out var meta);
        return (map, meta);
    }

    // -- line scope -----------------------------------------------------------

    [Fact]
    public void DisableLine_SuppressesOnlyThatRuleOnThatLine()
    {
        var (map, _) = Parse("DELETE FROM dbo.Orders  -- akml-disable-line PE003");

        Assert.True(map.IsSuppressed(1, "PE003"));
        Assert.False(map.IsSuppressed(1, "PE001"));
        Assert.False(map.IsSuppressed(2, "PE003"));
    }

    [Fact]
    public void DisableLine_AcceptsSeveralRules()
    {
        var (map, _) = Parse("SELECT * FROM foo  -- akml-disable-line PE001, BP004");

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.True(map.IsSuppressed(1, "BP004"));
        Assert.False(map.IsSuppressed(1, "SE002"));
    }

    [Fact]
    public void DisableLine_WithNoRuleIds_SuppressesEveryRuleOnThatLine()
    {
        var (map, _) = Parse("SELECT * FROM foo  -- akml-disable-line");

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.True(map.IsSuppressed(1, "ANYTHING999"));
    }

    // -- block scope ----------------------------------------------------------

    [Fact]
    public void DisableEnableBlock_IsScopedToTheNamedRule()
    {
        var sql = """
            -- akml-disable PE001
            SELECT * FROM foo
            DELETE FROM bar
            -- akml-enable PE001
            SELECT * FROM baz
            """;
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(2, "PE001"));
        // A different rule inside the block is NOT silenced — that is the whole point of naming one.
        Assert.False(map.IsSuppressed(3, "PE003"));
        // After the enable, the rule reports again.
        Assert.False(map.IsSuppressed(5, "PE001"));
    }

    [Fact]
    public void DisableEnableBlock_WithNoRuleIds_SuppressesEveryRuleInRange()
    {
        var sql = """
            -- akml-disable
            SELECT * FROM foo
            DELETE FROM bar
            -- akml-enable
            SELECT * FROM baz
            """;
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(2, "PE001"));
        Assert.True(map.IsSuppressed(3, "PE003"));
        Assert.False(map.IsSuppressed(5, "PE001"));
    }

    [Fact]
    public void BareEnable_ClosesEveryOpenRuleScopedBlock()
    {
        var sql = """
            -- akml-disable PE001, BP004
            SELECT * FROM foo
            -- akml-enable
            SELECT * FROM bar
            """;
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(2, "PE001"));
        Assert.True(map.IsSuppressed(2, "BP004"));
        Assert.False(map.IsSuppressed(4, "PE001"));
        Assert.False(map.IsSuppressed(4, "BP004"));
    }

    [Fact]
    public void EnablingOneRule_LeavesTheOtherSuppressedToEndOfFile()
    {
        var sql = """
            -- akml-disable PE001, BP004
            SELECT * FROM foo
            -- akml-enable PE001
            SELECT * FROM bar
            """;
        var (map, _) = Parse(sql);

        Assert.False(map.IsSuppressed(4, "PE001"));   // re-enabled
        Assert.True(map.IsSuppressed(4, "BP004"));    // still open
        Assert.True(map.IsSuppressed(9999, "BP004")); // ...to end of file
    }

    // -- whole-script scope ---------------------------------------------------

    [Fact]
    public void DisableWithoutEnable_CoversTheWholeScript_AndRaisesNoDiagnostic()
    {
        var sql = """
            -- akml-disable PE001
            SELECT * FROM foo
            SELECT * FROM bar
            """;
        var (map, meta) = Parse(sql);

        Assert.True(map.IsSuppressed(1, "PE001"));      // including the directive's own line
        Assert.True(map.IsSuppressed(3, "PE001"));
        Assert.True(map.IsSuppressed(9999, "PE001"));

        // Unlike an unclosed noqa-begin, this is the documented whole-script form and the shape the
        // "Disable ... in this script" quick fix writes, so warning about it would be warning about
        // our own output.
        Assert.Empty(meta);

        // Still scoped to the named rule.
        Assert.False(map.IsSuppressed(3, "PE003"));
    }

    // -- syntax tolerance -----------------------------------------------------

    [Fact]
    public void DirectivesAreCaseInsensitive()
    {
        var (map, _) = Parse("SELECT * FROM foo  -- AKML-DISABLE-LINE pe001");

        Assert.True(map.IsSuppressed(1, "PE001"));
    }

    [Fact]
    public void AnOptionalColonAfterTheVerbIsAccepted()
    {
        var (map, _) = Parse("SELECT * FROM foo  -- akml-disable-line: PE001");

        Assert.True(map.IsSuppressed(1, "PE001"));
    }

    [Fact]
    public void BlockCommentFormIsRecognised()
    {
        var sql = """
            /* akml-disable PE001 */
            SELECT * FROM foo
            /* akml-enable PE001 */
            SELECT * FROM bar
            """;
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(2, "PE001"));
        Assert.False(map.IsSuppressed(4, "PE001"));
    }

    [Fact]
    public void ATrailingReasonIsIgnoredRatherThanReadAsARule()
    {
        var (map, _) = Parse("SELECT * FROM foo  -- akml-disable-line PE001 legacy report, keep as is");

        Assert.True(map.IsSuppressed(1, "PE001"));
        // The prose must not turn this into a blanket suppression.
        Assert.False(map.IsSuppressed(1, "PE003"));
    }

    [Fact]
    public void DirectiveInsideAStringLiteralIsNotADirective()
    {
        var (map, _) = Parse("SELECT 'akml-disable PE001' AS note");

        Assert.False(map.IsSuppressed(1, "PE001"));
    }

    [Fact]
    public void ProseThatMerelyMentionsADirectiveIsNotADirective()
    {
        // The directive has to be what the comment says, not something it talks about — otherwise
        // a note to self would silently switch PE001 off for the rest of the file.
        var sql = """
            -- TODO: we could akml-disable PE001 here once the report is rewritten
            SELECT * FROM foo
            """;
        var (map, _) = Parse(sql);

        Assert.False(map.IsSuppressed(2, "PE001"));
    }

    [Fact]
    public void ExtraDashesAndNoSpaceStillCount()
    {
        // "----akml-disable-line PE001" and "--akml-disable-line PE001" are the same intent.
        var (map, _) = Parse("SELECT * FROM foo  ----akml-disable-line PE001");
        Assert.True(map.IsSuppressed(1, "PE001"));

        var (tight, _) = Parse("SELECT * FROM bar  --akml-disable-line PE001");
        Assert.True(tight.IsSuppressed(1, "PE001"));
    }

    // -- interoperability with the original noqa form -------------------------

    [Fact]
    public void NoqaAndAkmlDirectivesCoexistInOneScript()
    {
        var sql = """
            SELECT * FROM foo  -- noqa: PE001
            DELETE FROM bar    -- akml-disable-line PE003
            SELECT * FROM baz
            """;
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.True(map.IsSuppressed(2, "PE003"));
        Assert.False(map.IsSuppressed(3, "PE001"));
    }

    [Fact]
    public void TwoDirectivesOnOneLineMergeInsteadOfReplacingEachOther()
    {
        // A block comment and a line comment on the same physical line: both must apply. The map
        // used to key one entry per line and overwrite, so the first directive was lost.
        var (map, _) = Parse("SELECT * FROM foo /* akml-disable-line PE001 */ -- noqa: BP004");

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.True(map.IsSuppressed(1, "BP004"));
    }
}
