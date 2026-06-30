using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 — Parity2: Comment alignment / multiline comment formatting.
///
/// Guards:
/// 1. DEFAULT profile (MultilineFormatting = "preserve") is a byte-exact no-op for block comments.
/// 2. OPT-IN profile (MultilineFormatting = "normaliseIndent") re-indents block comment body lines
///    to the surrounding context indent (IndentLevel × tabSize), stripping each body line's leading
///    whitespace and re-applying the target indent.
/// 3. Header/banner comments (body lines dominated by repeated decoration chars: *, =, -, #)
///    are skipped by RecognizeCommonPatterns=true (default) even when normaliseIndent is active.
/// 4. DEFAULT profile: single-line trailing comments and leading -- comments are also untouched.
/// </summary>
public class Parity2_CommentFormattingTests
{
    private readonly TextEmitter _emitter = new();

    // Helper: emit a single multiline-comment LayoutNode and return the result.
    private string EmitBlockComment(string commentText, int indentLevel, FormattingProfile profile)
    {
        var node = new LayoutNode
        {
            TokenType = TSqlTokenType.MultilineComment,
            OriginalText = commentText,
            FormattedText = commentText,
            IndentLevel = indentLevel,
            PrecedingBreak = BreakType.NewLine,
            PrecedingSpaces = 0
        };
        // Suppress final-newline and trailing-whitespace changes so we can assert exact text.
        var testProfile = new FormattingProfile
        {
            Whitespace =
            {
                FinalNewline = "none",
                TrailingWhitespace = "keep",
                TabStyle = "spaces",
                TabSize = 4
            },
            Comments = profile.Comments
        };
        // A preceding non-comment node is needed so PrecedingBreak=NewLine has something to follow.
        var anchor = new LayoutNode
        {
            TokenType = TSqlTokenType.Select,
            OriginalText = "SELECT",
            FormattedText = "SELECT",
            PrecedingBreak = BreakType.None,
            PrecedingSpaces = 0
        };
        var nodes = new List<LayoutNode> { anchor, node };
        return _emitter.Emit(nodes, testProfile);
    }

    // ── Guard: default profile is a no-op ────────────────────────────────────

    [Fact]
    public void DefaultProfile_BlockComment_IsUntouched()
    {
        // A raggedly indented block comment must come through character-for-character unchanged
        // under the default profile (MultilineFormatting = "preserve").
        const string raw = "/*\n  This is a comment\n    with varied indentation\n*/";
        var profile = new FormattingProfile(); // defaults: MultilineFormatting = "preserve"

        var result = EmitBlockComment(raw, indentLevel: 1, profile);

        Assert.Contains(raw, result);
    }

    [Fact]
    public void DefaultProfile_TrailingLineComment_IsUntouched()
    {
        // Trailing -- comments stored in TrailingComment.Text must also be unchanged.
        var profile = new FormattingProfile();
        var node = new LayoutNode
        {
            TokenType = TSqlTokenType.Select,
            OriginalText = "SELECT 1",
            FormattedText = "SELECT 1",
            PrecedingBreak = BreakType.None,
            TrailingComment = new CommentAttachment { Text = "-- keep me" }
        };
        var testProfile = new FormattingProfile
        {
            Whitespace = { FinalNewline = "none", TrailingWhitespace = "keep" },
            Comments = profile.Comments
        };
        var result = _emitter.Emit(new List<LayoutNode> { node }, testProfile);

        Assert.Contains("-- keep me", result);
    }

    // ── normaliseIndent: re-indent block comment body ─────────────────────────

    [Fact]
    public void NormaliseIndent_BlockComment_BodyLinesReindentedToContextIndent()
    {
        // Block comment body lines currently have 2 leading spaces, but the node is at IndentLevel=1
        // (4 spaces with tabSize=4). After normaliseIndent the body lines should be re-indented to
        // 4 spaces (level 1), the opening /* inherits normal line-start indent.
        const string raw = "/*\n  This is a comment\n  with two spaces\n*/";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var result = EmitBlockComment(raw, indentLevel: 1, profile);

        // Body lines (after /*) should now be indented with 4 spaces (indent level 1 × tabSize 4).
        // The opening /* line already gets 4-space indent from AppendLineStart; the body lines
        // (lines 2..n-1 inside the comment) should also carry 4-space leading indent.
        Assert.Contains("\n    This is a comment", result);
        Assert.Contains("\n    with two spaces", result);
    }

    [Fact]
    public void NormaliseIndent_BodyLinesWithExcessIndent_AreNormalized()
    {
        // Body has more leading whitespace than the target indent — excess is stripped.
        const string raw = "/*\n        Over-indented body line\n*/";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var result = EmitBlockComment(raw, indentLevel: 0, profile);

        // At indentLevel=0 the body lines should have 0 leading spaces.
        Assert.Contains("\nOver-indented body line", result);
    }

    [Fact]
    public void NormaliseIndent_SingleLineBlockComment_IsUntouched()
    {
        // A block comment with no internal newlines has no body lines to reindent.
        const string raw = "/* inline */";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var result = EmitBlockComment(raw, indentLevel: 1, profile);

        Assert.Contains("/* inline */", result);
    }

    // ── RecognizeCommonPatterns: banner/header comments are skipped ──────────

    [Fact]
    public void NormaliseIndent_BannerComment_SkippedWhenRecognizeCommonPatterns()
    {
        // A block comment whose body lines are dominated by a repeated decoration character (*)
        // should be preserved verbatim even when normaliseIndent is active, because
        // RecognizeCommonPatterns=true (the default) treats it as a header/banner.
        const string banner = "/*\n * ===================================\n * Section header\n * ===================================\n*/";
        var profile = new FormattingProfile
        {
            Comments =
            {
                MultilineFormatting = "normaliseIndent",
                RecognizeCommonPatterns = true   // default — but explicit for readability
            }
        };

        var result = EmitBlockComment(banner, indentLevel: 2, profile);

        // The body should not have been reindented — the leading " * " pattern must be intact.
        Assert.Contains(" * ===================================", result);
        Assert.Contains(" * Section header", result);
    }

    [Fact]
    public void NormaliseIndent_BannerComment_ReindentedWhenRecognizeCommonPatternsOff()
    {
        // Same banner but with RecognizeCommonPatterns=false — the re-indent SHOULD fire.
        const string banner = "/*\n * Section header\n*/";
        var profile = new FormattingProfile
        {
            Comments =
            {
                MultilineFormatting = "normaliseIndent",
                RecognizeCommonPatterns = false
            }
        };

        var result = EmitBlockComment(banner, indentLevel: 1, profile);

        // With RecognizeCommonPatterns off, the " * Section header" line should be reindented
        // to 4 spaces (level 1), trimming the leading space+* prefix to nothing and re-prefixing.
        // The body text after trim-leading-whitespace is "* Section header", re-indented to 4 spaces.
        Assert.Contains("\n    * Section header", result);
    }

    // ── Idempotency at the emitter level ─────────────────────────────────────

    [Fact]
    public void NormaliseIndent_AlreadyNormalized_IsIdempotent()
    {
        // After re-indenting a comment, applying normaliseIndent a second time must produce
        // identical output (a fixed point — no oscillation that would fire Stage 7).
        const string raw = "/*\n  Body line\n*/";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var pass1 = EmitBlockComment(raw, indentLevel: 1, profile);
        // Extract just the comment text from pass1 so we can run pass2
        int commentStart = pass1.IndexOf("/*");
        string commentAfterPass1 = pass1[commentStart..];
        var pass2 = EmitBlockComment(commentAfterPass1, indentLevel: 1, profile);
        int commentStart2 = pass2.IndexOf("/*");
        string commentAfterPass2 = pass2[commentStart2..];

        Assert.Equal(commentAfterPass1, commentAfterPass2);
    }
}
