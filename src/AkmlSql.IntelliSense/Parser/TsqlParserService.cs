using System.Diagnostics.CodeAnalysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Parser;

[SuppressMessage("ReSharper", "UnusedMember.Local")]
public partial class TsqlParserService
{
    private readonly object _lock = new();
    private TSqlParser? _parser;
    private int _serverVersion;

    public void SetServerVersion(int serverVersion)
    {
        lock (_lock)
        {
            if (_serverVersion == serverVersion && _parser != null)
                return;
            _serverVersion = serverVersion;
            _parser = CreateParser(serverVersion);
        }
    }

    /// <summary>
    /// Spec 042 (SC-010 evidence): how many times ANY instance in this process has tokenized or parsed T-SQL. A PostgreSQL
    /// session must leave it where it was; the engine tests read it before and after. Diagnostic seam only.
    /// </summary>
    internal static long TotalInvocations => System.Threading.Interlocked.Read(ref _totalInvocations);
    private static long _totalInvocations;

    /// Fast tier: tokenize only (~50-70ms for 10K lines)
    public IList<TSqlParserToken> GetTokenStream(string sql)
    {
        System.Threading.Interlocked.Increment(ref _totalInvocations);
        var parser = GetParser();
        using var reader = new StringReader(sql);
        return parser.GetTokenStream(reader, out _);
    }

    /// <summary>
    /// Count of full AST parses performed by this instance. Diagnostic seam only — completion
    /// escalates through several repair attempts on the keystroke path, and a full parse is the
    /// single most expensive thing that path does, so the attempt count is worth pinning in tests.
    /// </summary>
    internal int ParseCount => _parseCount;
    private int _parseCount;

    /// Full tier: parse to AST (~300ms for 10K lines)
    public TSqlScript? Parse(string sql, out IList<ParseError> errors)
    {
        System.Threading.Interlocked.Increment(ref _parseCount);
        System.Threading.Interlocked.Increment(ref _totalInvocations);
        var parser = GetParser();
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out errors);
        return fragment as TSqlScript;
    }

    /// Parse with suffix completion for incomplete SQL
    public TSqlScript? ParseWithSuffix(string sql, out IList<ParseError> errors)
    {
        // Try normal parse first
        var result = Parse(sql, out errors);
        if (result != null && result.Batches.Count > 0 && errors.Count == 0)
        {
            return result;
        }

        // Try with suffix completion helper
        var suffixed = SuffixCompletionHelper.AppendDummyTokens(sql);
        return Parse(suffixed, out errors);
    }

    /// <summary>
    /// Spec 032 (A1/E6) — cursor-aware variant of <see cref="ParseWithSuffix(string, out IList{ParseError})"/>.
    /// When both the plain parse and the tail-repaired parse fail, tries repairing AT the
    /// caret (<see cref="SuffixCompletionHelper.RepairAtCursor"/>): documents that are broken
    /// exactly at the caret — a subquery or CTE body being typed inside <c>(...)</c> — become
    /// parseable, so AST-based scope/CTE/temp resolution keeps working there. The caret-repaired
    /// script is only used when it parses CLEAN; otherwise the tail-repaired result is returned
    /// unchanged (old behavior). Note: text is inserted at the caret, so fragment offsets at or
    /// before the caret remain valid — which is all the cursor-scope consumers rely on.
    /// </summary>
    public TSqlScript? ParseWithSuffix(string sql, int cursorOffset, out IList<ParseError> errors)
    {
        var result = Parse(sql, out errors);
        if (result != null && result.Batches.Count > 0 && errors.Count == 0)
        {
            return result;
        }

        var suffixed = SuffixCompletionHelper.AppendDummyTokens(sql);
        var tailResult = Parse(suffixed, out var tailErrors);
        if (tailResult != null && tailResult.Batches.Count > 0 && tailErrors.Count == 0)
        {
            errors = tailErrors;
            return tailResult;
        }

        var repaired = SuffixCompletionHelper.RepairAtCursor(sql, cursorOffset);
        if (!string.Equals(repaired, sql, StringComparison.Ordinal))
        {
            // With the caret at end-of-document — the dominant typing position — RepairAtCursor
            // reattaches an empty suffix and so returns exactly `suffixed`, which just failed
            // above. Re-parsing it is a guaranteed-identical failure, not a second chance.
            if (!string.Equals(repaired, suffixed, StringComparison.Ordinal))
            {
                var repairedResult = Parse(repaired, out var repairedErrors);
                if (repairedResult != null && repairedResult.Batches.Count > 0 && repairedErrors.Count == 0)
                {
                    errors = repairedErrors;
                    return repairedResult;
                }
            }

            // Caret repair + tail repair combined (e.g. broken at the caret AND incomplete tail).
            var both = SuffixCompletionHelper.AppendDummyTokens(repaired);
            if (!string.Equals(both, suffixed, StringComparison.Ordinal) &&
                !string.Equals(both, repaired, StringComparison.Ordinal))
            {
                var bothResult = Parse(both, out var bothErrors);
                if (bothResult != null && bothResult.Batches.Count > 0 && bothErrors.Count == 0)
                {
                    errors = bothErrors;
                    return bothResult;
                }
            }
        }

        errors = tailErrors;
        return tailResult;
    }

    private TSqlParser GetParser()
    {
        lock (_lock)
        {
            return _parser ??= CreateParser(_serverVersion);
        }
    }

    /// <summary>The refusal SSMS gives for anything but a repeat count and a comment after <c>GO</c>.</summary>
    public const string GoSeparatorError = "Incorrect syntax was encountered while parsing GO.";

    /// <summary>
    /// Spec 041 (research R16, N3) — splits a script into the batches SSMS would send, on <c>GO</c>.
    /// ScriptDom's lexer decides what a separator is: a <c>GO</c> token at the start of a line, never
    /// inside a string, a comment or <c>[GO]</c> (a block comment before it on the same line still lets it
    /// separate). On the GO line only whitespace, one repeat count (<c>GO 3</c>, at least 1) and a comment
    /// may follow; anything else (<c>GO SELECT 2</c>, <c>GO;</c>, <c>GO 0</c>) sets
    /// <see cref="SqlBatchSplit.SeparatorError"/> and the caller refuses the whole script, as SSMS does.
    /// Each batch runs verbatim from the line after the previous GO line to the GO token, so the server's
    /// line 1 is the batch's <see cref="SqlBatchSpan.StartLine"/>. Whitespace-only batches are skipped; an
    /// unterminated string swallows the rest of the script into the last batch. A script with no
    /// <c>go</c> in it at all is not lexed.
    /// </summary>
    public SqlBatchSplit SplitBatches(string? sql)
    {
        var batches = new List<SqlBatchSpan>();
        if (string.IsNullOrEmpty(sql)) return new SqlBatchSplit(batches, null);

        var lineStarts = LineStarts(sql);
        if (sql.IndexOf("go", StringComparison.OrdinalIgnoreCase) < 0)
        {
            AddBatch(batches, sql, 0, sql.Length, 1, lineStarts);
            return new SqlBatchSplit(batches, null);
        }

        var tokens = GetTokenStream(sql);
        int batchStart = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var go = tokens[i];
            if (go.TokenType != TSqlTokenType.Go) continue;

            // What follows GO on its line: whitespace, at most one repeat count, comments.
            int repeat = 1;
            bool sawCount = false;
            int lineEnd = go.Offset + go.Text.Length;
            for (int k = i + 1; k < tokens.Count; k++)
            {
                var t = tokens[k];
                if (t.TokenType == TSqlTokenType.EndOfFile) break;
                if (t.TokenType == TSqlTokenType.WhiteSpace)
                {
                    if (t.Text.IndexOf('\n') >= 0) break;   // the GO line ends here
                    continue;
                }
                if (t.TokenType == TSqlTokenType.SingleLineComment || t.TokenType == TSqlTokenType.MultilineComment)
                {
                    lineEnd = t.Offset + t.Text.Length;
                    if (t.Text.IndexOf('\n') >= 0) break;
                    continue;
                }
                if (t.TokenType == TSqlTokenType.Integer && !sawCount
                    && int.TryParse(t.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
                    && n >= 1)
                {
                    sawCount = true;
                    repeat = n;
                    lineEnd = t.Offset + t.Text.Length;
                    continue;
                }
                return new SqlBatchSplit(batches, GoSeparatorError);
            }

            AddBatch(batches, sql, batchStart, go.Offset, repeat, lineStarts);
            int newline = sql.IndexOf('\n', lineEnd);
            batchStart = newline < 0 ? sql.Length : newline + 1;
        }
        AddBatch(batches, sql, batchStart, sql.Length, 1, lineStarts);
        return new SqlBatchSplit(batches, null);
    }

    private static void AddBatch(List<SqlBatchSpan> batches, string sql, int start, int end, int repeat, List<int> lineStarts)
    {
        if (end <= start) return;
        var text = sql.Substring(start, end - start);
        if (string.IsNullOrWhiteSpace(text)) return;
        int line = lineStarts.BinarySearch(start);
        if (line < 0) line = ~line - 1;
        batches.Add(new SqlBatchSpan(batches.Count, start, end, line + 1, repeat, text));
    }

    private static List<int> LineStarts(string sql)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < sql.Length; i++)
            if (sql[i] == '\n') starts.Add(i + 1);
        return starts;
    }

    private static TSqlParser CreateParser(int serverVersion)
    {
        return serverVersion switch
        {
            >= 17 => new TSql170Parser(initialQuotedIdentifiers: true),
            16 => new TSql160Parser(initialQuotedIdentifiers: true),
            15 => new TSql150Parser(initialQuotedIdentifiers: true),
            14 => new TSql140Parser(initialQuotedIdentifiers: true),
            _ => new TSql130Parser(initialQuotedIdentifiers: true),
        };
    }
}
