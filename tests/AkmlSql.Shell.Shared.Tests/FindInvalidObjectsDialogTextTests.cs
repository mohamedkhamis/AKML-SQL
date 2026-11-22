using AkmlSql.Shell.Shared.Refactoring;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests;

/// <summary>
/// Spec 047 (SSMS-003, data-model §9) — the Find Invalid Objects window says what the scan checked: a clean database reads
/// "All 63 references resolve.", a database with nothing to check says so, and the empty grid is not shown. Before, a clean
/// Northwind read "No invalid objects found (0 dependency rows scanned)." above an empty grey grid.
/// </summary>
public sealed class FindInvalidObjectsDialogTextTests
{
    [Theory]
    [InlineData(0, 63, "No invalid objects found. All 63 references resolve.")]
    [InlineData(0, 1, "No invalid objects found. The 1 reference resolves.")]
    [InlineData(0, 0, "This database has no object references to check.")]
    [InlineData(2, 63, "2 invalid object(s) — references that can no longer be resolved.")]
    [InlineData(1, 0, "1 invalid object(s) — references that can no longer be resolved.")]
    public void The_summary_says_what_was_checked(int records, int totalScanned, string expected)
    {
        Assert.Equal(expected, FindInvalidObjectsDialog.SummaryText(records, totalScanned));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    public void The_grid_is_shown_only_when_there_is_something_in_it(int records, bool shown)
    {
        Assert.Equal(shown, FindInvalidObjectsDialog.ShowsGrid(records));
    }
}
