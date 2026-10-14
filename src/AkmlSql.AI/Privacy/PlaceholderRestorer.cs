using System.Text;
using System.Text.RegularExpressions;

namespace AkmlSql.Engine.Ai.Privacy;

/// <summary>
/// Spec 042 (FR-081). Puts the literals back into an AI answer. Before a PostgreSQL script goes to a cloud provider the
/// engine replaces each literal with a placeholder (<c>'__STR_0__'</c>, <c>__NUM_1__</c>, <c>$$__STR_2__$$</c>) and returns a
/// map from each placeholder to the literal; the model's answer repeats the placeholders, and this class swaps them back so the
/// user sees, and can accept, SQL with their own values in it.
///
/// <para>
/// Restoring is one pass over the text: a placeholder is looked up exactly as written, and what it is replaced with is never
/// scanned again, so a literal that happens to look like another placeholder stays what it was. A placeholder that is not in the
/// map, or that the model altered, is left as the model wrote it.
/// </para>
/// </summary>
public sealed class PlaceholderRestorer
{
    // The three shapes the engine writes. The map decides what is real; the pattern only finds candidates.
    private static readonly Regex Candidate = new(
        @"'__STR_\d+__'|__NUM_\d+__|\$[A-Za-z0-9_]*\$__STR_\d+__\$[A-Za-z0-9_]*\$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, string> _map;
    private HashSet<string>? _prefixes;
    private int _longestKey;

    public PlaceholderRestorer(IReadOnlyDictionary<string, string> placeholders)
    {
        _map = placeholders ?? throw new ArgumentNullException(nameof(placeholders));
    }

    /// <summary>True when there is nothing to restore, so text can pass through untouched.</summary>
    public bool IsEmpty => _map.Count == 0;

    /// <summary>Restores every known placeholder in <paramref name="text"/>.</summary>
    public string Restore(string text)
    {
        if (IsEmpty || string.IsNullOrEmpty(text)) return text;
        return Candidate.Replace(text, m => _map.TryGetValue(m.Value, out var literal) ? literal : m.Value);
    }

    /// <summary>
    /// Starts restoring a streamed answer. The answer arrives in pieces cut anywhere, so the end of a piece that could be the
    /// beginning of a placeholder is held back until the next piece shows what it is.
    /// </summary>
    public StreamRestorer BeginStream() => new(this);

    /// <summary>The restorer of one streamed answer; not thread-safe, one per answer.</summary>
    public sealed class StreamRestorer
    {
        private readonly PlaceholderRestorer _owner;
        private string _pending = string.Empty;

        internal StreamRestorer(PlaceholderRestorer owner) => _owner = owner;

        /// <summary>Takes the next piece of the answer and returns the text that is now certain, with literals restored.</summary>
        public string Push(string piece)
        {
            if (_owner.IsEmpty) return piece;
            if (string.IsNullOrEmpty(piece)) return string.Empty;

            _pending += piece;
            var hold = _owner.LongestPartialSuffix(_pending, _owner.EndOfLastCandidate(_pending));
            var ready = _pending[..^hold];
            _pending = _pending[^hold..];
            return _owner.Restore(ready);
        }

        /// <summary>The answer has ended: whatever was held back is what it looked like, so it is released.</summary>
        public string Flush()
        {
            var rest = _pending;
            _pending = string.Empty;
            return _owner.Restore(rest);
        }
    }

    /// <summary>
    /// The length of the longest tail of <paramref name="text"/> that is a proper prefix of some placeholder, so more text could
    /// still complete it. Only the last few characters are looked at, and only where a placeholder can begin.
    /// </summary>
    private int LongestPartialSuffix(string text, int notBefore)
    {
        EnsurePrefixes();

        var from = Math.Max(notBefore, text.Length - (_longestKey - 1));
        for (var start = from; start < text.Length; start++)
        {
            var c = text[start];
            if (c != '\'' && c != '_' && c != '$') continue; // no placeholder begins with anything else

            if (_prefixes!.Contains(text.Substring(start))) return text.Length - start;
        }
        return 0;
    }

    /// <summary>
    /// Where the last complete placeholder-shaped text in <paramref name="text"/> ends (0 when there is none). A tail can only
    /// begin a new placeholder after that point: the closing quote of <c>'__STR_0__'</c> is also what an opening quote looks
    /// like, and must not be held back from the placeholder it closes.
    /// </summary>
    private int EndOfLastCandidate(string text)
    {
        var end = 0;
        for (var m = Candidate.Match(text); m.Success; m = m.NextMatch()) end = m.Index + m.Length;
        return end;
    }

    private void EnsurePrefixes()
    {
        if (_prefixes != null) return;

        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        var longest = 0;
        foreach (var key in _map.Keys)
        {
            longest = Math.Max(longest, key.Length);
            for (var length = 1; length < key.Length; length++) prefixes.Add(key.Substring(0, length));
        }
        _longestKey = longest;
        _prefixes = prefixes;
    }
}
