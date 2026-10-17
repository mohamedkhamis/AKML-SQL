using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Review findings WEB-01 and WEB-08 in the grid: the rows' connection stamp goes back with their edits; a frozen grid (a run
/// in flight, or rows read through an earlier connection) takes no change and applies nothing; and while an Apply waits for its
/// reply nothing can change, so what is marked committed is exactly what was sent.
/// </summary>
public sealed class ResultsGridTargetBindingTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private IRenderedComponent<ResultSetGrid> Render(ExecuteResultSet set, string? frozen = null, string? stamp = "stamp-A") =>
        _host.Context.Render<ResultSetGrid>(p => p
            .Add(x => x.Set, set)
            .Add(x => x.SetIndex, 0)
            .Add(x => x.FrozenReason, frozen)
            .Add(x => x.ConnectionStamp, stamp));

    private static void Edit(IRenderedComponent<ResultSetGrid> cut, int r, int c, string value)
    {
        Cell(cut, r, c).DoubleClick();
        var input = cut.Find("[data-testid=results-cell-input]");
        input.Input(value);
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static bool Editing(IRenderedComponent<ResultSetGrid> cut) => cut.FindAll("[data-testid=results-cell-input]").Count == 1;

    [Fact]
    public void Apply_sends_the_stamp_of_the_connection_the_rows_were_read_through()
    {
        var cut = Render(EditableSet(), stamp: "stamp-A");
        Edit(cut, 0, 1, "changed");

        cut.Find("[data-testid=results-apply]").Click();

        Assert.Equal("stamp-A", Assert.Single(_host.Exec.Applied).ConnectionStamp);
    }

    [Fact]
    public void Frozen_rows_take_no_edit_and_say_why()
    {
        const string reason = "These results came from an earlier connection.";
        var cut = Render(EditableSet(), frozen: reason);

        ClickCell(cut, 0, 1);
        ClickCell(cut, 0, 1);                               // a second click edits — but not here
        ClickCell(cut, 1, 1);
        Press(cut, "x");                                    // typing replaces — but not here
        Press(cut, "0", ctrl: true);                        // Set to NULL

        Assert.False(Editing(cut));
        Assert.Empty(cut.FindAll("[data-testid=results-applybar]"));
        Assert.Contains(reason, cut.Find("[data-testid=results-frozen-reason]").TextContent);
        Assert.True(cut.Find("[data-testid=results-add-row]").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid=results-delete-0]").HasAttribute("disabled"));
        Assert.Equal("true", Root(cut).GetAttribute("aria-readonly"));
        Assert.Contains(_host.Notes.Sent, n => n.Text == reason);
    }

    [Fact]
    public void Edits_made_before_the_freeze_stay_visible_can_be_discarded_and_are_never_applied()
    {
        var cut = Render(EditableSet());
        Edit(cut, 0, 1, "pending");

        cut.Render(p => p.Add(x => x.FrozenReason, "These results came from an earlier connection."));

        var apply = cut.Find("[data-testid=results-apply]");
        Assert.True(apply.HasAttribute("disabled"));
        apply.Click();
        Assert.Empty(_host.Exec.Applied);
        Assert.Equal("pending", Cell(cut, 0, 1).TextContent.Trim());

        cut.Find("[data-testid=results-discard]").Click();
        Assert.Empty(cut.FindAll("[data-testid=results-applybar]"));
        Assert.Equal("alpha", Cell(cut, 0, 1).TextContent.Trim());
    }

    [Fact]
    public async Task While_an_Apply_waits_nothing_changes_and_only_what_was_sent_is_marked_committed()
    {
        var cut = Render(EditableSet());
        Edit(cut, 0, 1, "sent");
        _host.Exec.ApplyGate = new TaskCompletionSource();

        cut.Find("[data-testid=results-apply]").Click();            // the reply is on its way
        Assert.True(cut.Instance.IsApplying);

        // The same cell, another cell, a new row and a delete are all refused while it waits.
        ClickCell(cut, 0, 1);
        ClickCell(cut, 0, 1);
        Assert.False(Editing(cut));
        cut.Find("[data-testid=results-add-row]").Click();
        cut.Find("[data-testid=results-delete-1]").Click();
        Assert.Equal(1, cut.Instance.PendingEditCount);

        _host.Exec.ApplyGate.SetResult();
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.WaitForAssertion(() => Assert.False(cut.Instance.IsApplying));
        Assert.Equal(0, cut.Instance.PendingEditCount);
        Assert.Equal("sent", Cell(cut, 0, 1).TextContent.Trim());
        var edit = Assert.Single(Assert.Single(_host.Exec.Applied).Edits);
        Assert.Equal("sent", Assert.Single(edit.SetCells).Value);
    }

    [Fact]
    public async Task A_reply_that_arrives_after_a_new_result_touches_nothing_of_it()
    {
        var cut = Render(EditableSet());
        Edit(cut, 0, 1, "sent");
        _host.Exec.ApplyGate = new TaskCompletionSource();
        cut.Find("[data-testid=results-apply]").Click();

        var next = EditableSet();
        cut.Render(p => p.Add(x => x.Set, next));                   // a new run replaced the result
        _host.Exec.ApplyGate.SetResult();
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.WaitForAssertion(() => Assert.False(cut.Instance.IsApplying));
        Assert.Equal("alpha", next.Rows[0][1]);                     // nothing baked into the new rows
        Assert.Equal("alpha", Cell(cut, 0, 1).TextContent.Trim());
    }
}
