using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Parser;

public partial class TsqlParserService
{
    private readonly object _lock = new();
    private TSqlParser? _parser;
    private int _serverVersion;

    public void SetServerVersion(int serverVersion)
    {
        if (_serverVersion == serverVersion && _parser != null)
        {
            return;
        }

        _serverVersion = serverVersion;
        _parser = CreateParser(serverVersion);
    }

    /// Fast tier: tokenize only (~50-70ms for 10K lines)
    public IList<TSqlParserToken> GetTokenStream(string sql)
    {
        var parser = GetParser();
        using var reader = new StringReader(sql);
        return parser.GetTokenStream(reader, out _);
    }

    /// Full tier: parse to AST (~300ms for 10K lines)
    public TSqlScript? Parse(string sql, out IList<ParseError> errors)
    {
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

    private TSqlParser GetParser()
    {
        if (_parser == null)
        {
            _parser = CreateParser(_serverVersion);
        }

        return _parser;
    }

    /// <summary>
    /// T105: Splits SQL text on GO batch separators.
    /// GO must appear on its own line (case-insensitive), optionally with leading/trailing whitespace
    /// and an optional repeat count (e.g., "GO 5").
    /// Returns a list of (startOffset, endOffset, batchText) tuples.
    /// </summary>
    public List<(int startOffset, int endOffset, string text)> SplitBatches(string sql)
    {
        var batches = new List<(int, int, string)>();
        if (string.IsNullOrEmpty(sql))
        {
            return batches;
        }

        var matches = GoPattern().Matches(sql);

        int batchStart = 0;
        foreach (Match match in matches)
        {
            var batchEnd = match.Index;
            if (batchEnd > batchStart)
            {
                var batchText = sql[batchStart..batchEnd];
                if (!string.IsNullOrWhiteSpace(batchText))
                {
                    batches.Add((batchStart, batchEnd, batchText));
                }
            }
            batchStart = match.Index + match.Length;
        }

        // Final batch after the last GO (or the entire text if no GO found)
        if (batchStart < sql.Length)
        {
            var remaining = sql[batchStart..];
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                batches.Add((batchStart, sql.Length, remaining));
            }
        }

        return batches;
    }

    // Matches GO on its own line, optionally followed by a repeat count.
    // ^\s*GO\s*(\d*)\s*$ with multiline flag.
    [GeneratedRegex(@"^\s*GO\s*(\d*)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GoPattern();

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
