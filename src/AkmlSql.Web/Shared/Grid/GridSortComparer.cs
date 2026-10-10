using System.Globalization;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;

namespace AkmlSql.Web.Shared.Grid;

/// <summary>
/// Spec 041 (research R6, FR-012) — orders the returned rows by one column the way SQL Server would:
/// numbers as numbers, dates as dates, uniqueidentifiers in SQL Server's byte order, NULL first. It
/// orders the wire values (the engine's round-trip encodings, <c>SqlScalarEncoder</c>), never the text
/// the grid shows, and returns a PERMUTATION of fetched row indexes: rows and their pending edits never
/// move, so Apply still writes to the row each edit was made on (FR-016). Keys are parsed once; the sort
/// is stable, with the original position breaking ties.
/// </summary>
public static class GridSortComparer
{
    // Rank orders the classes of value: NULL first, parsed values, then values that would not parse,
    // then engine-cut indicators (their content is unknown).
    private const byte RankNull = 0;
    private const byte RankValue = 1;
    private const byte RankUnparsed = 2;
    private const byte RankCut = 3;

    private readonly struct Key
    {
        public Key(byte rank, string? text) { Rank = rank; Text = text; Number = 0; Big = 0; Bytes = null; Guid = default; }

        public byte Rank { get; init; }
        public string? Text { get; init; }
        public double Number { get; init; }
        public decimal Big { get; init; }
        public byte[]? Bytes { get; init; }
        public System.Data.SqlTypes.SqlGuid Guid { get; init; }
    }

    /// <summary>
    /// Sort <paramref name="fetchedIndexes"/> (the rows on screen, as fetched indexes) by the wire value
    /// <paramref name="valueOf"/> returns for each, typed by <paramref name="clrHint"/>
    /// (<see cref="ClrTypeHint"/>). Returns the new display order.
    /// </summary>
    public static int[] Sort(IReadOnlyList<int> fetchedIndexes, Func<int, string?> valueOf, int clrHint, bool descending)
    {
        int n = fetchedIndexes.Count;
        var keys = new Key[n];
        for (int i = 0; i < n; i++) keys[i] = Parse(valueOf(fetchedIndexes[i]), clrHint);

        var positions = Enumerable.Range(0, n).ToArray();
        Comparison<int> compare = (a, b) =>
        {
            int c = CompareKeys(keys[a], keys[b], clrHint);
            if (descending) c = -c;
            return c != 0 ? c : a.CompareTo(b);   // stable: original order breaks ties
        };
        Array.Sort(positions, compare);
        return positions.Select(p => fetchedIndexes[p]).ToArray();
    }

    private static Key Parse(string? value, int hint)
    {
        if (value == null) return new Key(RankNull, null);
        if (EngineCutIndicator.IsIndicator(value)) return new Key(RankCut, value);
        var inv = CultureInfo.InvariantCulture;
        switch (hint)
        {
            case ClrTypeHint.Int64:
                if (long.TryParse(value, NumberStyles.Integer, inv, out var l)) return new Key(RankValue, value) { Big = l };
                if (decimal.TryParse(value, NumberStyles.Float, inv, out var bigInt)) return new Key(RankValue, value) { Big = bigInt };
                break;
            case ClrTypeHint.Decimal:
                if (decimal.TryParse(value, NumberStyles.Float, inv, out var dec)) return new Key(RankValue, value) { Big = dec };
                break;
            case ClrTypeHint.Double:
                if (double.TryParse(value, NumberStyles.Float, inv, out var d)) return new Key(RankValue, value) { Number = d };
                break;
            case ClrTypeHint.Bool:
                if (value == "0" || value == "1") return new Key(RankValue, value) { Big = value == "1" ? 1 : 0 };
                break;
            case ClrTypeHint.DateTime:
                if (DateTime.TryParse(value, inv, DateTimeStyles.RoundtripKind, out var dt)) return new Key(RankValue, value) { Big = dt.Ticks };
                break;
            case ClrTypeHint.DateTimeOffset:
                if (DateTimeOffset.TryParse(value, inv, DateTimeStyles.None, out var dto)) return new Key(RankValue, value) { Big = dto.UtcTicks };
                break;
            case ClrTypeHint.Guid:
                if (System.Guid.TryParse(value, out var g)) return new Key(RankValue, value) { Guid = new System.Data.SqlTypes.SqlGuid(g) };
                break;
            case ClrTypeHint.Binary:
                try { return new Key(RankValue, value) { Bytes = Convert.FromBase64String(value) }; }
                catch (FormatException) { }
                break;
            default:
                return new Key(RankValue, value);
        }
        return new Key(RankUnparsed, value);
    }

    private static int CompareKeys(in Key a, in Key b, int hint)
    {
        int rank = a.Rank.CompareTo(b.Rank);
        if (rank != 0) return rank;
        if (a.Rank == RankNull) return 0;
        if (a.Rank != RankValue) return string.CompareOrdinal(a.Text, b.Text);

        switch (hint)
        {
            case ClrTypeHint.Int64:
            case ClrTypeHint.Decimal:
            case ClrTypeHint.Bool:
            case ClrTypeHint.DateTime:
            case ClrTypeHint.DateTimeOffset:
                return a.Big.CompareTo(b.Big);
            case ClrTypeHint.Double:
                return a.Number.CompareTo(b.Number);
            case ClrTypeHint.Guid:
                return a.Guid.CompareTo(b.Guid);
            case ClrTypeHint.Binary:
                return CompareBytes(a.Bytes!, b.Bytes!);
            default:
                int ci = string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
                return ci;
        }
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }
}
