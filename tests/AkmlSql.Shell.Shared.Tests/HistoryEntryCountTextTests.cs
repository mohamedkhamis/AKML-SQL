using System.Globalization;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests;

/// <summary>
/// Installed run 2026-11-23: the SQL History window's count line read "1 entries found" (a binding's StringFormat cannot choose
/// the word). It reads "1 entry found" and "12 entries found" now.
/// </summary>
public sealed class HistoryEntryCountTextTests
{
    [Theory]
    [InlineData(0, "0 entries found")]
    [InlineData(1, "1 entry found")]
    [InlineData(2, "2 entries found")]
    public void The_count_line_chooses_entry_or_entries(int count, string expected)
    {
        Assert.Equal(expected, EntryCountText.For(count));
    }

    [Fact]
    public void The_binding_converter_gives_the_same_words()
    {
        Assert.Equal("1 entry found", EntryCountText.Converter.Convert(1, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
