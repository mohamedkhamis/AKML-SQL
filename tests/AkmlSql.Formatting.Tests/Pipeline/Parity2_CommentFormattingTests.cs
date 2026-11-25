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
/// 2. OPT-IN profile (MultilineFormatting = "normaliseIndent") moves a block comment as one block:
///    its other lines shift by as many columns as its /* moved, so the comment's inner layout is
///    kept (spec 047, FMT-004 / FR-014 — it used to strip every body line's indentation and apply
///    the code's, which flattened the comment).
/// 3. Header/banner comments (body lines dominated by repeated decoration chars: *, =, -, #)
///    are skipped by RecognizeCommonPatterns=true (default) even when normaliseIndent is active.
/// 4. DEFAULT profile: single-line trailing comments and leading -- comments are also untouched.
/// </summary>
public class Parity2_CommentFormattingTests
{
    private readonly TextEmitter _emitter = new();

    // Helper: emit a single multiline-comment LayoutNode and return the result. originalColumn is where the comment's
    // /* stood in the input (the layout engine records it); the emitter places the /* at indentLevel × 4.
    private string EmitBlockComment(string commentText, int indentLevel, FormattingProfile profile, int originalColumn = 0)
    {
        var node = new LayoutNode
        {
            TokenType = TSqlTokenType.MultilineComment,
            OriginalText = commentText,
            FormattedText = commentText,
            IndentLevel = indentLevel,
            PrecedingBreak = BreakType.NewLine,
            PrecedingSpaces = 0,
            OriginalColumn = originalColumn,
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

    // ── normaliseIndent: the block moves as one ───────────────────────────────

    [Fact]
    public void NormaliseIndent_BlockComment_MovesWithItsOpener()
    {
        // The comment stood at column 0; its /* now lands at column 4 (IndentLevel 1 × tabSize 4).
        // Every other line moves the same 4 columns, so the comment's own layout is kept.
        const string raw = "/*\n  This is a comment\n    with varied indentation\n*/";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var result = EmitBlockComment(raw, indentLevel: 1, profile, originalColumn: 0);

        Assert.Contains("    /*\n      This is a comment\n        with varied indentation\n    */", result);
    }

    [Fact]
    public void NormaliseIndent_BodyLines_KeepTheirIndentRelativeToTheOpener()
    {
        // The /* stood at column 4 and lands at column 0: every line moves 4 columns left, no further
        // (spec 047, FMT-004: the old pass stripped the body's indentation altogether).
        const string raw = "/*\n        Over-indented body line\n    */";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var result = EmitBlockComment(raw, indentLevel: 0, profile, originalColumn: 4);

        Assert.Contains("/*\n    Over-indented body line\n*/", result);
    }

    [Fact]
    public void NormaliseIndent_UnmovedComment_IsWrittenExactlyAsItWas()
    {
        const string raw = "/* first line\n   second line indented three\n     third indented five */";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var result = EmitBlockComment(raw, indentLevel: 1, profile, originalColumn: 4);

        Assert.Contains(raw, result);
    }

    [Fact]
    public void NormaliseIndent_UnknownOriginalColumn_IsWrittenExactlyAsItWas()
    {
        const string raw = "/*\n  body\n*/";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var result = EmitBlockComment(raw, indentLevel: 2, profile, originalColumn: -1);

        Assert.Contains(raw, result);
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
    public void NormaliseIndent_BannerComment_MovedWhenRecognizeCommonPatternsOff()
    {
        // Same banner but with RecognizeCommonPatterns=false — the comment moves with its /* (column 0 → 4),
        // its " * " pattern intact.
        const string banner = "/*\n * Section header\n*/";
        var profile = new FormattingProfile
        {
            Comments =
            {
                MultilineFormatting = "normaliseIndent",
                RecognizeCommonPatterns = false
            }
        };

        var result = EmitBlockComment(banner, indentLevel: 1, profile, originalColumn: 0);

        Assert.Contains("\n     * Section header\n    */", result);
    }

    // ── Idempotency at the emitter level ─────────────────────────────────────

    [Fact]
    public void NormaliseIndent_AlreadyNormalized_IsIdempotent()
    {
        // After moving a comment, applying normaliseIndent a second time must produce identical
        // output (a fixed point — no oscillation that would fire Stage 7). On the second pass the
        // comment's /* already stands where the first pass put it (column 4).
        const string raw = "/*\n  Body line\n*/";
        var profile = new FormattingProfile
        {
            Comments = { MultilineFormatting = "normaliseIndent" }
        };

        var pass1 = EmitBlockComment(raw, indentLevel: 1, profile, originalColumn: 0);
        // Extract just the comment text from pass1 so we can run pass2
        int commentStart = pass1.IndexOf("/*");
        string commentAfterPass1 = pass1[commentStart..];
        var pass2 = EmitBlockComment(commentAfterPass1, indentLevel: 1, profile, originalColumn: 4);
        int commentStart2 = pass2.IndexOf("/*");
        string commentAfterPass2 = pass2[commentStart2..];

        Assert.Equal(commentAfterPass1, commentAfterPass2);
    }
}
