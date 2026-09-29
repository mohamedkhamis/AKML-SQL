using System.Linq;
using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 040 (T113, HIS-10) — the line diff behind History's "Compare with current": lines that stay,
/// lines added or removed, and a line edited in place reported as changed rather than as a
/// removal plus an addition.
/// </summary>
public class LineDiffTests
{
    private static string Kinds(string left, string right) =>
        string.Join(",", LineDiff.Diff(left, right).Select(e => e.Kind.ToString()));

    [Fact]
    public void Identical_texts_are_all_same()
    {
        var diff = LineDiff.Diff("SELECT 1\nFROM t", "SELECT 1\nFROM t");
        Assert.All(diff, e => Assert.Equal(LineDiffKind.Same, e.Kind));
        Assert.Equal(new int?[] { 1, 2 }, diff.Select(e => e.LeftLine));
        Assert.Equal(new int?[] { 1, 2 }, diff.Select(e => e.RightLine));
    }

    [Fact]
    public void A_pure_insert()
    {
        var diff = LineDiff.Diff("SELECT a\nFROM t", "SELECT a\nWHERE 1 = 1\nFROM t");
        Assert.Equal("Same,Added,Same", string.Join(",", diff.Select(e => e.Kind)));
        var added = diff[1];
        Assert.Null(added.LeftLine);
        Assert.Equal(2, added.RightLine);
        Assert.Equal("WHERE 1 = 1", added.RightText);
    }

    [Fact]
    public void A_pure_delete()
    {
        var diff = LineDiff.Diff("SELECT a\n-- note\nFROM t", "SELECT a\nFROM t");
        Assert.Equal("Same,Removed,Same", string.Join(",", diff.Select(e => e.Kind)));
        Assert.Equal(2, diff[1].LeftLine);
        Assert.Null(diff[1].RightLine);
        Assert.Equal("-- note", diff[1].LeftText);
    }

    [Fact]
    public void An_edited_line_is_changed_not_removed_and_added()
    {
        var diff = LineDiff.Diff("SELECT a\nFROM Customers\nWHERE x = 1", "SELECT a\nFROM Orders\nWHERE x = 1");
        Assert.Equal("Same,Changed,Same", string.Join(",", diff.Select(e => e.Kind)));
        Assert.Equal("FROM Customers", diff[1].LeftText);
        Assert.Equal("FROM Orders", diff[1].RightText);
        Assert.Equal(2, diff[1].LeftLine);
        Assert.Equal(2, diff[1].RightLine);
    }

    [Fact]
    public void Uneven_edit_runs_pair_what_they_can()
        => Assert.Equal("Same,Changed,Added,Same", Kinds("a\nb\nz", "a\nB1\nB2\nz"));

    [Fact]
    public void Empty_left_is_all_added_and_empty_right_all_removed()
    {
        Assert.Equal("Added,Added", Kinds("", "SELECT 1\nFROM t"));
        Assert.Equal("Removed,Removed", Kinds("SELECT 1\nFROM t", ""));
        Assert.Empty(LineDiff.Diff("", ""));
    }

    [Fact]
    public void Crlf_and_lf_are_the_same_text()
    {
        Assert.All(LineDiff.Diff("SELECT 1\r\nFROM t\r\n", "SELECT 1\nFROM t\n"), e => Assert.Equal(LineDiffKind.Same, e.Kind));
        Assert.All(LineDiff.Diff("a\rb", "a\nb"), e => Assert.Equal(LineDiffKind.Same, e.Kind));
    }

    [Fact]
    public void A_long_text_with_one_change_stays_fast_and_exact()
    {
        var left = string.Join("\n", Enumerable.Range(0, 20000).Select(i => "line " + i));
        var right = left.Replace("line 12345", "line twelve thousand");
        var diff = LineDiff.Diff(left, right);
        var changed = Assert.Single(diff, e => e.Kind != LineDiffKind.Same);
        Assert.Equal(LineDiffKind.Changed, changed.Kind);
        Assert.Equal(12346, changed.LeftLine);
    }
}
