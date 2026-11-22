using System.Text.RegularExpressions;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace AkmlSql.Formatting.Tests.Parity;

/// <summary>
/// Spec 047 (FMT-001 … FMT-005, contracts/formatter.md §2): rules the formatted output must satisfy in every style,
/// asserted on the text itself rather than on captured goldens, so a golden can never bless a broken layout. Runs the
/// eight built-in .akmlstyle styles, the web edition's two built-ins (AKML Default, ANSI-compact) and a SQL Prompt
/// document style, over the committed tests/format-parity/corpus/sp047-*.sql inputs.
/// </summary>
public sealed class Sp047FormatterRulesTests
{
    private static readonly string[] BuiltInStyles =
    {
        "default", "compact", "indented", "aligned-left-bracket", "leading-commas", "minimalist", "khamis-style", "collapsed",
    };

    public static IEnumerable<object[]> Styles() =>
        BuiltInStyles.Concat(new[] { "web-default", "web-ansi", "sqlprompt-default" }).Select(s => new object[] { s });

    private static FormattingProfile Profile(string style)
    {
        switch (style)
        {
            case "web-default":
            {
                // Mirrors src/AkmlSql.Web/Services/IProfileStore.cs CreateBuiltInProfile("default").
                var p = new FormattingProfile();
                p.Metadata.Name = "AKML Default";
                return p;
            }
            case "web-ansi":
            {
                // Mirrors CreateBuiltInProfile("ansi").
                var p = new FormattingProfile();
                p.Metadata.Name = "ANSI-compact";
                p.Casing.ReservedKeywords = "uppercase";
                return p;
            }
            case "sqlprompt-default":
                return SqlPromptStyles.ToProfile(SqlPromptStyleDocument.CreateDefault("sp047"));
            default:
                var path = Path.Combine(RepoRoot, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", style + ".akmlstyle");
                return ProfileSerializer.Deserialize(File.ReadAllText(path));
        }
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && dir.GetFiles("*.slnx").Length == 0) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static string Corpus(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot, "tests", "format-parity", "corpus", name + ".sql"));

    private static string Format(string sql, string style) =>
        ParityHarness.Normalise(ParityHarness.Format(sql, Profile(style)));

    private static string[] Lines(string text) => text.Split('\n');

    private static int Indent(string line) => line.Length - line.TrimStart(' ', '\t').Length;

    // ── FMT-001: a sized parameter type stays on one line ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Styles))]
    public void Fmt001_a_sized_parameter_type_stays_on_one_line(string style)
    {
        var output = Format(Corpus("sp047-01-proc-params"), style);

        foreach (var line in Lines(output))
            Assert.False(Regex.IsMatch(line, @"\b(n?var)?char\s*\(\s*$|\bdecimal\s*\(\s*$", RegexOptions.IgnoreCase),
                $"[{style}] a type's '(' ends the line:\n{output}");
        Assert.Matches(new Regex(@"@b\s+varchar\s*\(\s*10\s*\)", RegexOptions.IgnoreCase), output);
        Assert.Matches(new Regex(@"@b\s+nvarchar\s*\(\s*max\s*\)", RegexOptions.IgnoreCase), output);
        Assert.Matches(new Regex(@"@c\s+decimal\s*\(\s*10\s*,\s*2\s*\)", RegexOptions.IgnoreCase), output);
    }

    // ── FMT-002: BEGIN/END of IF, ELSE and WHILE blocks sit at their owner's level, never at column 0 ───────────

    [Theory]
    [MemberData(nameof(Styles))]
    public void Fmt002_begin_and_end_align_with_their_owner_and_the_body_is_deeper(string style)
    {
        var output = Format(Corpus("sp047-02-control-flow"), style);
        var lines = Lines(output).Where(l => l.Trim().Length > 0).ToArray();

        // The procedure's own BEGIN and END are the outermost block and may be at column 0; no other BEGIN/END may.
        var blockLines = lines.Select((l, i) => (Line: l, Index: i))
            .Where(x => StartsWithBlockKeyword(x.Line)).ToList();
        Assert.True(blockLines.Count >= 2, $"[{style}] no BEGIN/END lines:\n{output}");
        foreach (var (line, index) in blockLines.Skip(1).Take(blockLines.Count - 2))
            Assert.True(Indent(line) > 0, $"[{style}] line {index + 1} '{line.Trim()}' is at column 0:\n{output}");

        // Every END that starts a line lines up with the line that opened its block; the block's lines are deeper. A CASE
        // expression's END closes the CASE, not a block, and BEGIN TRAN opens no block.
        var open = new Stack<int>();   // the indent of each open block; CaseMark for an open CASE
        foreach (var line in lines)
        {
            var block = open.Where(o => o != CaseMark).Cast<int?>().FirstOrDefault();
            if (!StartsWithBlockKeyword(line) && block is int inner)
                Assert.True(Indent(line) > inner,
                    $"[{style}] body line '{line.Trim()}' is not deeper than its block ({inner}):\n{output}");

            var first = true;
            foreach (Match w in BlockWords.Matches(line.ToUpperInvariant()))
            {
                var startsLine = first && line.TrimStart().StartsWith(w.Value, StringComparison.OrdinalIgnoreCase);
                first = false;
                if (w.Groups[3].Value is "TRAN" or "TRANSACTION") continue;
                switch (w.Groups[1].Value)
                {
                    case "CASE":
                        open.Push(CaseMark);
                        break;
                    case "BEGIN":
                        open.Push(Indent(line));
                        break;
                    default:
                        if (open.Count == 0) break;
                        var owner = open.Pop();
                        if (owner != CaseMark && startsLine)
                            Assert.True(Indent(line) == owner,
                                $"[{style}] '{line.Trim()}' is at {Indent(line)}, its block opened at {owner}:\n{output}");
                        break;
                }
            }
        }
    }

    private const int CaseMark = -1;

    private static readonly Regex BlockWords = new(@"\b(BEGIN|END|CASE)\b(\s+(TRY|CATCH|TRAN|TRANSACTION)\b)?");

    [Theory]
    [MemberData(nameof(Styles))]
    public void Fmt002_statements_after_a_block_or_case_end_keep_their_own_line_and_level(string style)
    {
        var output = Format(Corpus("sp047-02-control-flow"), style);
        var lines = Lines(output).Where(l => l.Trim().Length > 0).ToArray();

        // An IF or WHILE starts its line (ELSE IF excepted): a statement after a block's END is not joined onto it.
        foreach (var line in lines)
            foreach (Match m in Regex.Matches(line, @"\b(IF|WHILE)\b", RegexOptions.IgnoreCase))
            {
                var before = line[..m.Index].Trim();
                Assert.True(before.Length == 0 || Regex.IsMatch(before, @"\bELSE$", RegexOptions.IgnoreCase),
                    $"[{style}] '{m.Value}' does not start its line: '{line.Trim()}'\n{output}");
            }

        // A line that starts with a block's END holds that END alone (TRY/CATCH, a semicolon, an ELSE or a comment aside).
        foreach (var line in lines.Where(StartsWithBlockKeyword))
            if (Regex.IsMatch(line.TrimStart(), @"^END\b", RegexOptions.IgnoreCase))
                Assert.Matches(new Regex(@"^\s*END(\s+(TRY|CATCH))?\s*;?\s*(ELSE\b.*)?(--.*)?$", RegexOptions.IgnoreCase), line);

        // The statement after a CASE expression stays at its block's level, and BEGIN TRAN indents nothing after it.
        Assert.Equal(Indent(LineStarting(lines, "SELECT CASE")), Indent(LineStarting(lines, "SELECT 4")));
        var tran = Indent(LineStarting(lines, "BEGIN TRAN"));
        Assert.Equal(tran, Indent(LineStarting(lines, "SET @a = 0")));
        Assert.Equal(tran, Indent(LineStarting(lines, "COMMIT")));
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Fmt002_every_statement_starts_its_own_line(string style)
    {
        // The root cause behind the joined blocks: a statement written without a semicolon (or after one, in a style with
        // no empty line between statements) was collapsed onto the statement before it.
        var output = Format(Corpus("sp047-06-statements"), style);
        var lines = Lines(output).Where(l => l.Trim().Length > 0).ToArray();
        foreach (var start in new[]
                 {
                     "CREATE TABLE", "SELECT 1", "DROP TABLE", "PRINT 1", "DECLARE @x", "IF @x = 1", "SELECT 1 AS a",
                     "SELECT 2 AS b", "EXEC", "SET NOCOUNT",
                 })
        {
            try { LineStarting(lines, start); }
            catch (Xunit.Sdk.XunitException) { throw new Xunit.Sdk.XunitException($"[{style}] no line starts with '{start}':\n{output}"); }
        }
    }

    private static string LineStarting(IEnumerable<string> lines, string start)
    {
        var words = Regex.Escape(start).Replace(@"\ ", @"\s+");
        return lines.FirstOrDefault(l => Regex.IsMatch(l.TrimStart(), "^" + words + @"\b", RegexOptions.IgnoreCase))
               ?? throw new Xunit.Sdk.XunitException($"no line starts with '{start}'");
    }

    private static bool StartsWithBlockKeyword(string line)
    {
        var t = line.TrimStart().ToUpperInvariant();
        if (Regex.IsMatch(t, @"^BEGIN\s+(TRAN|TRANSACTION)\b")) return false;
        return Regex.IsMatch(t, @"^(BEGIN|END)\b");
    }

    // ── FMT-003: the statement after GO / GO n starts at column 0 ──────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Styles))]
    public void Fmt003_the_line_after_every_batch_separator_starts_at_column_0(string style)
    {
        var output = Format(Corpus("sp047-03-go-count"), style);
        var lines = Lines(output);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i].Trim(), @"^GO(\s+\d+)?(\s*--.*)?$", RegexOptions.IgnoreCase)) continue;
            var next = lines.Skip(i + 1).FirstOrDefault(l => l.Trim().Length > 0);
            if (next == null) continue;   // the final GO
            Assert.True(Indent(next) == 0, $"[{style}] '{next.Trim()}' after '{lines[i].Trim()}' is indented:\n{output}");
        }
    }

    // ── FMT-004: the inside of a block comment keeps its relative indentation ──────────────────────────────────

    [Theory]
    [MemberData(nameof(Styles))]
    public void Fmt004_a_block_comment_keeps_its_relative_indentation(string style)
    {
        var lines = Lines(Format(Corpus("sp047-04-block-comment"), style));

        var first = Array.FindIndex(lines, l => l.Contains("/* first line", StringComparison.Ordinal));
        Assert.True(first >= 0, $"[{style}] the comment is gone");
        var second = lines[first + 1];
        var third = lines[first + 2];
        Assert.Contains("second line indented three", second);
        Assert.Contains("third indented five", third);
        Assert.Equal(3, Indent(second) - Indent(lines[first]));
        Assert.Equal(5, Indent(third) - Indent(lines[first]));
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void Fmt004_the_statement_after_a_leading_block_comment_keeps_its_own_line(string style)
    {
        // The script's first statement after its leading comments was decided as the script's first word, with no
        // break, which glued it to the comment's end ("third indented five */SELECT 1;").
        var output = Format(Corpus("sp047-04-block-comment"), style);

        Assert.DoesNotMatch(new Regex(@"\*/[ \t]*SELECT 1", RegexOptions.IgnoreCase), output);
        Assert.Contains(Lines(output), l => l.TrimStart().StartsWith("SELECT 1;", StringComparison.OrdinalIgnoreCase));
    }

    // ── FMT-005: no padding for a keyword that is not on the line; short lists stay whole ─────────────────────

    [Theory]
    [MemberData(nameof(Styles))]
    public void Fmt005_single_spaces_after_join_keywords_and_short_lists_stay_on_one_line(string style)
    {
        var output = Format(Corpus("sp047-05-spacing"), style);

        Assert.DoesNotMatch(new Regex(@"\bJOIN {2,}\S", RegexOptions.IgnoreCase), output);
        Assert.DoesNotMatch(new Regex(@"^\s*SET {2,}NOCOUNT", RegexOptions.IgnoreCase | RegexOptions.Multiline), output);
        // IN and its short list on one line: a style that puts a list's bracket on its own line must not leave "IN"
        // alone at the end of the line above "(10, 11)".
        Assert.Matches(new Regex(@"\bIN[ \t]*\([ \t]*10[ \t]*,[ \t]*11[ \t]*\)", RegexOptions.IgnoreCase), output);
    }

    // ── All cases: stable on a second pass, and every token kept ────────────────────────────────────────────────

    public static IEnumerable<object[]> StylesAndInputs() =>
        Styles().SelectMany(s => new[]
        {
            "sp047-01-proc-params", "sp047-02-control-flow", "sp047-03-go-count", "sp047-04-block-comment", "sp047-05-spacing",
            "sp047-06-statements",
        }.Select(i => new object[] { s[0], i }));

    [Theory]
    [MemberData(nameof(StylesAndInputs))]
    public void A_second_format_changes_nothing_and_every_token_is_kept(string style, string input)
    {
        var sql = Corpus(input);
        var once = Format(sql, style);
        var twice = Format(once, style);

        Assert.Equal(once, twice);
        Assert.Equal(Tokens(sql), Tokens(once));
    }

    /// <summary>The script's tokens, ignoring whitespace, case, [brackets], semicolons and whitespace inside comments.</summary>
    private static List<string> Tokens(string sql)
    {
        var tokens = new TSql170Parser(true).GetTokenStream(new StringReader(sql), out _);
        return tokens
            .Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.Semicolon or TSqlTokenType.EndOfFile))
            .Select(t => t.TokenType is TSqlTokenType.MultilineComment or TSqlTokenType.SingleLineComment
                ? Regex.Replace(t.Text, @"\s+", " ").Trim()
                : t.Text.Trim('[', ']').ToUpperInvariant())
            .ToList();
    }
}
