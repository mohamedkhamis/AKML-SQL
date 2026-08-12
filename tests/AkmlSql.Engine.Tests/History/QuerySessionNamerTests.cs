using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class QuerySessionNamerTests
{
    [Theory]
    [InlineData(1, "query-01")]
    [InlineData(9, "query-09")]
    [InlineData(10, "query-10")]
    [InlineData(99, "query-99")]
    // Past 99 the name widens rather than truncating — a 100th session in one day
    // must still get a unique, sortable name.
    [InlineData(100, "query-100")]
    public void FormatName_pads_to_two_digits_then_widens(int ordinal, string expected)
        => Assert.Equal(expected, QuerySessionNamer.FormatName(ordinal));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SQLQuery1.sql")]
    [InlineData("SQLQuery17.sql")]
    [InlineData("dwnhdxfq.sql")]   // SSMS random 8-char scratch name — the reported case
    [InlineData("DWNHDXFQ.SQL")]   // matching is case-insensitive
    public void IsScratchTabTitle_true_for_unsaved_scratch_documents(string? title)
        => Assert.True(QuerySessionNamer.IsScratchTabTitle(title));

    [Theory]
    [InlineData("MonthlyReport.sql")]
    [InlineData("customer-cleanup.sql")]
    [InlineData("a.sql")]
    public void IsScratchTabTitle_false_for_real_file_names(string title)
        => Assert.False(QuerySessionNamer.IsScratchTabTitle(title));

    /// <summary>
    /// Known false positive, asserted so the limitation stays visible instead of being
    /// rediscovered as a bug. Applies to the backfill of pre-migration rows ONLY: new rows
    /// carry TabTitle only for genuinely saved documents (Task 8), so this never fires on them.
    /// </summary>
    [Fact]
    public void IsScratchTabTitle_known_false_positive_is_documented()
        => Assert.True(QuerySessionNamer.IsScratchTabTitle("report01.sql"));

    [Fact]
    public void LocalDateKey_converts_utc_to_local_day()
    {
        // Pick an instant and assert against the machine's own local conversion, so the test
        // is correct in every timezone rather than only in the author's.
        var utc = new DateTime(2026, 8, 12, 21, 30, 0, DateTimeKind.Utc);
        var expected = utc.ToLocalTime().ToString("yyyy-MM-dd");
        Assert.Equal(expected, QuerySessionNamer.LocalDateKey(utc));
    }
}
