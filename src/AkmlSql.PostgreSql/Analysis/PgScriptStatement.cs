using AkmlSql.PostgreSql.Completion;
using AkmlSql.PostgreSql.Language;
using Google.Protobuf;
using PgSqlParser;
using PgSqlParser.Utils;

namespace AkmlSql.PostgreSql.Analysis;

/// <summary>
/// Spec 042 (task T162) - one statement of the script as a rule sees it: where it is in the document, its tokens, and the parse
/// tree the PostgreSQL parser built for it. Scripts are split into statements first and each one is parsed alone, as it is
/// executed (research R7), so a psql meta-command or an error in one statement never hides the others from the parser.
/// </summary>
public sealed class PgScriptStatement
{
    private readonly PgAnalysisContext _ctx;
    private readonly string _sql;
    private PgStatementTokens? _tokens;
    private Dictionary<int, int>? _tokenAt;
    private Utf8OffsetMapper? _mapper;
    private bool? _ascii;
    private IReadOnlyList<SqlToken>? _keywords;
    private Dictionary<Type, List<IMessage>>? _byType;

    internal PgScriptStatement(PgAnalysisContext ctx, int index, PgStatement source, string sql, RawStmt? raw, PgParseError? error)
    {
        _ctx = ctx;
        _sql = sql;
        Index = index;
        Source = source;
        Raw = raw;
        Error = error;
    }

    public int Index { get; }
    public PgStatement Source { get; }

    /// <summary>Document offset of the statement's first character.</summary>
    public int Start => Source.Start;

    /// <summary>Document offset just past the statement's last character (the terminating semicolon is not part of it).</summary>
    public int End => Source.End;

    public string Text => Source.Text;

    /// <summary>True when the statement is, or ends with, a psql meta-command (a backslash command such as \d or \gexec); the server never sees that part.</summary>
    public bool IsMetaCommand => Source.IsMetaCommand;

    /// <summary>The tree of the statement; null for a meta-command alone and for a statement that did not parse.</summary>
    public RawStmt? Raw { get; }

    /// <summary>The statement node (a SelectStmt, CreateStmt ... wrapped in a <see cref="Node"/>).</summary>
    public Node? Stmt => Raw?.Stmt;

    /// <summary>The syntax error that stopped this statement parsing, in document coordinates; null when it parsed.</summary>
    public PgParseError? Error { get; }

    /// <summary>The statement's kind as the parser classifies it; <see cref="StatementKind.Other"/> when there is no tree.</summary>
    public StatementKind Kind => Raw == null ? StatementKind.Other : StatementClassifier.Classify(Raw).Kind;

    /// <summary>
    /// All nodes of type <typeparamref name="T"/> in the tree, the statement itself and nested queries included, in document order.
    /// The tree is walked once, for the first rule that asks; the fifty rules that follow read the index.
    /// </summary>
    public IEnumerable<T> All<T>() where T : class, IMessage
    {
        if (Raw == null) return Enumerable.Empty<T>();

        if (_byType == null)
        {
            _byType = new Dictionary<Type, List<IMessage>>();
            foreach (var node in Raw.Descendants())
            {
                var type = node.GetType();
                if (!_byType.TryGetValue(type, out var list)) _byType[type] = list = new List<IMessage>();
                list.Add(node);
            }
        }

        return _byType.TryGetValue(typeof(T), out var found) ? found.Cast<T>() : Enumerable.Empty<T>();
    }

    internal PgStatementTokens Tokens => _tokens ??= PgStatementTokens.Build(_ctx.Text, _ctx.LexSlice(Start, End), Start, End);

    /// <summary>The upper-case first word of the statement ("SELECT", "CREATE"); empty for a meta-command.</summary>
    public string FirstWord
    {
        get
        {
            var sig = Tokens.Sig;
            return sig.Count > 0 && sig[0].IsWord ? sig[0].Upper : string.Empty;
        }
    }

    /// <summary>Keyword tokens of the statement with the parser's reservation class, in document coordinates.</summary>
    internal IReadOnlyList<(int Start, int End, KeywordKind Kind)> Keywords()
    {
        _keywords ??= PgParser.Tokenize(_sql);
        var list = new List<(int, int, KeywordKind)>();
        foreach (var t in _keywords)
        {
            if (t.Kind == SqlTokenKind.Keyword) list.Add((Start + t.Start, Start + t.End, t.KeywordKind));
        }
        return list;
    }

    /// <summary>The unquoted names (identifiers the parser does not know as key words) of the statement, in document coordinates.</summary>
    internal IReadOnlyList<(int Start, int End)> NameTokens()
    {
        _keywords ??= PgParser.Tokenize(_sql);
        var list = new List<(int, int)>();
        foreach (var t in _keywords)
        {
            // A double-quoted name is an identifier token too, but it is exactly what it spells: only the bare ones are folded.
            if (t.Kind != SqlTokenKind.Identifier || _sql[t.Start] == '"' || IsUnicodeEscapedName(t.Start)) continue;
            list.Add((Start + t.Start, Start + t.End));
        }
        return list;
    }

    // U&"name" - a name with Unicode escapes - starts with U, an ampersand and a double quote.
    private bool IsUnicodeEscapedName(int at) =>
        at + 2 < _sql.Length && (_sql[at] == 'U' || _sql[at] == 'u') && _sql[at + 1] == '&' && _sql[at + 2] == '"';

    /// <summary>The document offset of a parse-tree location (a UTF-8 byte offset into this statement's SQL).</summary>
    public int DocOffset(int location)
    {
        _ascii ??= _sql.All(c => c < 0x80);
        if (_ascii.Value) return Start + Math.Min(location, _sql.Length);

        _mapper ??= new Utf8OffsetMapper(_sql);
        return Start + (_mapper.TryToCharOffset(location, out var chars) ? chars : _sql.Length);
    }

    /// <summary>The start of a node's first token, or -1 when the node carries no position (statement and clause nodes do not).</summary>
    public int LocationOf(IMessage node)
    {
        var location = node.GetLocation();
        return location is null or < 0 ? -1 : DocOffset(location.Value);
    }

    /// <summary>
    /// The range a finding on <paramref name="node"/> underlines: its first token, extended over a dotted name (a.b.c, t.*).
    /// A node without a position falls back to the statement's first token.
    /// </summary>
    public (int Start, int End) SpanOf(IMessage node)
    {
        var at = LocationOf(node);
        if (at < 0)
        {
            if (Tokens.Count == 0) return (Start, End);
            var first = Tokens[0].Token;
            return (first.Start, first.End);
        }
        return NameSpanAt(at);
    }

    /// <summary>The token starting at <paramref name="docOffset"/> and, when it begins a dotted name, the whole name.</summary>
    public (int Start, int End) NameSpanAt(int docOffset)
    {
        var i = TokenIndexAt(docOffset);
        if (i < 0) return (docOffset, Math.Min(docOffset + 1, End));

        var sig = Tokens.Sig;
        var end = sig[i].Token.End;
        if (sig[i].IsName)
        {
            while (i + 2 < sig.Count && sig[i + 1].IsDot && (sig[i + 2].IsName || sig[i + 2].IsStar))
            {
                i += 2;
                end = sig[i].Token.End;
            }
        }
        return (docOffset, end);
    }

    /// <summary>The index in <see cref="Tokens"/> of the token that starts at <paramref name="docOffset"/>, or -1.</summary>
    internal int TokenIndexAt(int docOffset)
    {
        if (_tokenAt == null)
        {
            _tokenAt = new Dictionary<int, int>();
            var sig = Tokens.Sig;
            for (var i = 0; i < sig.Count; i++) _tokenAt[sig[i].Token.Start] = i;
        }
        return _tokenAt.TryGetValue(docOffset, out var index) ? index : -1;
    }
}
