using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Schema;
using AkmlSql.PostgreSql.Language;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis;

/// <summary>
/// Spec 042 (task T162) - everything a PostgreSQL rule may look at: the text, its statements with their parse trees and tokens, the
/// schema cache of the connection (null when there is none), and the settings in effect. Built once per analysis; the rules read it
/// and never change it.
/// </summary>
public sealed class PgAnalysisContext
{
    private readonly int[] _lineStarts;
    private readonly PgLexToken[] _lexArray;

    private PgAnalysisContext(string text, IReadOnlyList<PgLexToken> lexTokens, DatabaseCache? schemaCache, ResolvedAnalysisSettings settings, CancellationToken ct)
    {
        Text = text;
        LexTokens = lexTokens;
        _lexArray = lexTokens.ToArray();
        SchemaCache = schemaCache;
        Settings = settings;
        CancellationToken = ct;
        _lineStarts = LineStartsOf(text);
        Statements = Array.Empty<PgScriptStatement>();
    }

    public string Text { get; }

    /// <summary>Every token of the script, white space and comments included (UTF-16 offsets).</summary>
    public IReadOnlyList<PgLexToken> LexTokens { get; }

    /// <summary>The statements, in order, meta-commands included.</summary>
    public IReadOnlyList<PgScriptStatement> Statements { get; private set; }

    public DatabaseCache? SchemaCache { get; }

    public ResolvedAnalysisSettings Settings { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>The first statement that did not parse, or null when the whole script parses.</summary>
    public PgScriptStatement? FirstSyntaxError => Statements.FirstOrDefault(s => s.Error != null);

    /// <summary>The tokens that start in [from, to), found by binary search: a statement reads its own tokens, not the whole script's.</summary>
    internal IReadOnlyList<PgLexToken> LexSlice(int from, int to)
    {
        var first = LowerBound(from);
        var last = LowerBound(to);
        return new ArraySegment<PgLexToken>(_lexArray, first, Math.Max(0, last - first));
    }

    // The index of the first token that starts at or after the offset (tokens are in document order).
    private int LowerBound(int offset)
    {
        int lo = 0, hi = _lexArray.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (_lexArray[mid].Start < offset) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>The 1-based line and column of a 0-based document offset.</summary>
    public (int Line, int Column) LineColumn(int offset)
    {
        offset = Math.Max(0, Math.Min(offset, Text.Length));
        var index = Array.BinarySearch(_lineStarts, offset);
        if (index < 0) index = ~index - 1;
        return (index + 1, offset - _lineStarts[index] + 1);
    }

    /// <summary>Splits and parses the script. The result is a snapshot: later edits need a new context.</summary>
    public static PgAnalysisContext Create(string text, DatabaseCache? schemaCache = null, ResolvedAnalysisSettings? settings = null, CancellationToken ct = default)
    {
        text ??= string.Empty;
        var context = new PgAnalysisContext(text, PgLexicalScanner.Scan(text), schemaCache, settings ?? new ResolvedAnalysisSettings(), ct);
        var statements = new List<PgScriptStatement>();

        foreach (var source in PgStatementSplitter.Split(text))
        {
            ct.ThrowIfCancellationRequested();

            // A meta-command ends a line and is never sent to the server, so the SQL before it is parsed on its own.
            var sql = source.Text;
            if (source.IsMetaCommand)
            {
                var meta = context.LexTokens.FirstOrDefault(t => t.Kind == PgTokenKind.MetaCommand && t.Start >= source.Start && t.Start < source.End);
                sql = meta.End > meta.Start ? text.Substring(source.Start, meta.Start - source.Start) : string.Empty;
            }

            RawStmt? raw = null;
            PgParseError? error = null;
            if (HasCode(sql))
            {
                var parsed = PgParser.Parse(sql);
                if (parsed.Success)
                {
                    raw = parsed.Tree!.Stmts.Count > 0 ? parsed.Tree.Stmts[0] : null;
                }
                else
                {
                    var offset = source.Start + parsed.Error!.Offset;
                    var (line, column) = context.LineColumn(offset);
                    error = new PgParseError(parsed.Error.Message, offset, line, column, parsed.Error.SqlState);
                }
            }

            statements.Add(new PgScriptStatement(context, statements.Count, source, sql, raw, error));
        }

        context.Statements = statements;
        return context;
    }

    // True when the text holds something other than white space and comments.
    private static bool HasCode(string sql)
    {
        foreach (var t in PgLexicalScanner.Scan(sql))
        {
            if (t.Kind is not (PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment)) return true;
        }
        return false;
    }

    private static int[] LineStartsOf(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') starts.Add(i + 1);
            else if (text[i] == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n')) starts.Add(i + 1);
        }
        return starts.ToArray();
    }
}
