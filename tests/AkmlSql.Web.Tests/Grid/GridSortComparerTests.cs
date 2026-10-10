using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Shared.Grid;
using Xunit;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T040, research R6, FR-012) — sorting the returned rows by a column orders them the way SQL
/// Server would: numbers as numbers, dates as dates, NULL first — not by the text shown in the cell. The
/// result is a permutation of fetched row indexes; the rows themselves (and their pending edits) never move.
/// </summary>
public sealed class GridSortComparerTests
{
    /// <summary>Sort the given wire values and return them in display order.</summary>
    private static string?[] Sorted(int hint, bool descending, params string?[] values)
    {
        var order = GridSortComparer.Sort(Enumerable.Range(0, values.Length).ToArray(), i => values[i], hint, descending);
        return order.Select(i => values[i]).ToArray();
    }

    [Fact]
    public void Integers_sort_numerically()
    {
        Assert.Equal(new[] { "2", "10", "100" }, Sorted(ClrTypeHint.Int64, false, "10", "100", "2"));
    }

    [Fact]
    public void Integers_beyond_long_range_fall_back_to_decimal_order()
    {
        Assert.Equal(new[] { "-5", "9223372036854775807", "99999999999999999999" },
            Sorted(ClrTypeHint.Int64, false, "99999999999999999999", "-5", "9223372036854775807"));
    }

    [Fact]
    public void Doubles_and_decimals_sort_numerically()
    {
        Assert.Equal(new[] { "-1.5", "2.25", "10" }, Sorted(ClrTypeHint.Double, false, "10", "-1.5", "2.25"));
        Assert.Equal(new[] { "1.10", "1.9", "12.50" }, Sorted(ClrTypeHint.Decimal, false, "12.50", "1.9", "1.10"));
    }

    [Fact]
    public void Bits_sort_zero_before_one()
    {
        Assert.Equal(new[] { "0", "0", "1" }, Sorted(ClrTypeHint.Bool, false, "1", "0", "0"));
    }

    [Fact]
    public void Dates_sort_chronologically_not_by_text()
    {
        Assert.Equal(new[] { "2025-02-01T00:00:00.0000000", "2025-10-01T09:00:00.0000000", "2026-01-01T00:00:00.0000000" },
            Sorted(ClrTypeHint.DateTime, false, "2025-10-01T09:00:00.0000000", "2026-01-01T00:00:00.0000000", "2025-02-01T00:00:00.0000000"));
    }

    [Fact]
    public void Datetimeoffsets_sort_by_instant()
    {
        // 10:00+02:00 (08:00Z) comes before 09:00+00:00 (09:00Z).
        Assert.Equal(new[] { "2025-10-01T10:00:00.0000000+02:00", "2025-10-01T09:00:00.0000000+00:00" },
            Sorted(ClrTypeHint.DateTimeOffset, false, "2025-10-01T09:00:00.0000000+00:00", "2025-10-01T10:00:00.0000000+02:00"));
    }

    [Fact]
    public void Guids_sort_in_SQL_Server_order()
    {
        // SQL Server compares the last six bytes first: these differ only in byte 10 vs byte 0.
        var lowLast = "ff000000-0000-0000-0000-000000000000";
        var highLast = "00000000-0000-0000-0000-010000000000";
        Assert.Equal(new[] { lowLast, highLast }, Sorted(ClrTypeHint.Guid, false, highLast, lowLast));
    }

    [Fact]
    public void Binary_sorts_by_its_bytes_with_engine_cut_values_last()
    {
        var a = Convert.ToBase64String(new byte[] { 0x01, 0xFF });
        var b = Convert.ToBase64String(new byte[] { 0x02 });
        Assert.Equal(new[] { a, b, "[binary 99999 bytes]" }, Sorted(ClrTypeHint.Binary, false, "[binary 99999 bytes]", b, a));
    }

    [Fact]
    public void Strings_sort_case_insensitively()
    {
        Assert.Equal(new[] { "apple", "Banana", "cherry" }, Sorted(ClrTypeHint.String, false, "cherry", "Banana", "apple"));
    }

    [Fact]
    public void Unparsable_values_follow_the_parsed_ones_in_ordinal_order()
    {
        Assert.Equal(new[] { "1", "5", "abc", "xyz" }, Sorted(ClrTypeHint.Int64, false, "xyz", "5", "abc", "1"));
    }

    [Fact]
    public void Null_sorts_first_ascending_and_last_descending()
    {
        Assert.Equal(new[] { null, "1", "2" }, Sorted(ClrTypeHint.Int64, false, "2", null, "1"));
        Assert.Equal(new[] { "2", "1", null }, Sorted(ClrTypeHint.Int64, true, "2", null, "1"));
    }

    [Fact]
    public void The_sort_is_stable_with_the_original_order_breaking_ties()
    {
        var values = new[] { "b", "a", "B", "a" };
        var order = GridSortComparer.Sort(new[] { 0, 1, 2, 3 }, i => values[i], ClrTypeHint.String, descending: false);
        Assert.Equal(new[] { 1, 3, 0, 2 }, order);

        var desc = GridSortComparer.Sort(new[] { 0, 1, 2, 3 }, i => values[i], ClrTypeHint.String, descending: true);
        Assert.Equal(new[] { 0, 2, 1, 3 }, desc);
    }

    [Fact]
    public void Only_the_given_rows_are_permuted()
    {
        var values = new[] { "5", "4", "3", "2", "1" };
        var order = GridSortComparer.Sort(new[] { 4, 0, 2 }, i => values[i], ClrTypeHint.Int64, descending: false);
        Assert.Equal(new[] { 4, 2, 0 }, order);   // values 1, 3, 5
    }
}
