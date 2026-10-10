using AkmlSql.Core.Ipc.Messages;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T048, research R4, FR-013, FR-016) — safe click-to-select editing. One click selects; a second
/// click, a double-click, Enter, F2 or a typed character edits; Escape gives the old value back; Enter and
/// Tab commit. Column 1 (name) of <see cref="GridTestHost.EditableSet"/> is editable; column 0 (the identity
/// key) is not.
/// </summary>
public sealed class ResultsGridEditingTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static bool Editing(IRenderedComponent<Shared.ResultSetGrid> cut) =>
        cut.FindAll("[data-testid=results-cell-input]").Count == 1;

    private static AngleSharp.Dom.IElement Input(IRenderedComponent<Shared.ResultSetGrid> cut) =>
        cut.Find("[data-testid=results-cell-input]");

    private static void EditorKey(IRenderedComponent<Shared.ResultSetGrid> cut, string key, bool shift = false) =>
        Input(cut).KeyDown(new KeyboardEventArgs { Key = key, ShiftKey = shift });

    [Fact]
    public void One_click_selects_and_a_second_click_edits()
    {
        var cut = _host.Render(EditableSet());

        ClickCell(cut, 0, 1);
        Assert.False(Editing(cut));
        Assert.Equal("true", Cell(cut, 0, 1).GetAttribute("aria-selected"));

        ClickCell(cut, 0, 1);
        Assert.True(Editing(cut));
        Assert.Equal("alpha", Input(cut).GetAttribute("value"));
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("F2")]
    public void Enter_or_F2_on_the_focused_cell_edits_it(string key)
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 1, 1);

        Press(cut, key);

        Assert.True(Editing(cut));
        Assert.Equal("bravo", Input(cut).GetAttribute("value"));
    }

    [Fact]
    public void A_double_click_edits()
    {
        var cut = _host.Render(EditableSet());

        Cell(cut, 0, 1).DoubleClick();

        Assert.True(Editing(cut));
    }

    [Fact]
    public void A_typed_character_starts_an_edit_that_replaces_the_value()
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 0, 1);

        Press(cut, "x");

        Assert.Equal("x", Input(cut).GetAttribute("value"));
        EditorKey(cut, "Enter");
        Assert.Equal("x", Cell(cut, 0, 1).TextContent.Trim());
    }

    [Fact]
    public void Keys_typed_before_the_editor_has_focus_are_kept()
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 0, 1);

        Press(cut, "a");
        Press(cut, "b");   // reached the grid root: the input was not focused yet
        Press(cut, "c");

        Assert.Equal("abc", Input(cut).GetAttribute("value"));
        EditorKey(cut, "Enter");
        Assert.Equal("abc", Cell(cut, 0, 1).TextContent.Trim());
    }

    [Fact]
    public void A_cell_that_cannot_be_edited_never_opens_the_editor()
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 0, 0);
        ClickCell(cut, 0, 0);
        Press(cut, "Enter");
        Press(cut, "7");

        Assert.False(Editing(cut));
    }

    [Fact]
    public void Escape_cancels_and_the_value_comes_back()
    {
        var cut = _host.Render(EditableSet());
        Cell(cut, 0, 1).DoubleClick();
        Input(cut).Input("changed");

        EditorKey(cut, "Escape");

        Assert.False(Editing(cut));
        Assert.Equal("alpha", Cell(cut, 0, 1).TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=results-applybar]"));
    }

    [Fact]
    public void Enter_commits_and_hands_the_focus_back_to_the_grid()
    {
        var module = _host.Context.JSInterop.SetupModule("./js/akml-results-grid.js");
        module.Mode = JSRuntimeMode.Loose;
        var cut = _host.Render(EditableSet());
        Cell(cut, 0, 1).DoubleClick();
        Input(cut).Input("new name");

        EditorKey(cut, "Enter");

        Assert.False(Editing(cut));
        Assert.Equal("new name", Cell(cut, 0, 1).TextContent.Trim());
        Assert.Contains("1 pending change", cut.Find("[data-testid=results-applybar]").TextContent);
        cut.WaitForAssertion(() => Assert.NotEmpty(module.Invocations["focusRoot"]));
    }

    [Fact]
    public void An_untouched_editor_changes_nothing()
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 2, 1);   // name is NULL on row 2

        Press(cut, "F2");
        EditorKey(cut, "Enter");

        Assert.Empty(cut.FindAll("[data-testid=results-applybar]"));
        Assert.Equal("NULL", Cell(cut, 2, 1).TextContent.Trim());
    }

    [Fact]
    public void Tab_and_Shift_Tab_commit_and_move_to_the_next_and_previous_editable_cell()
    {
        var cut = _host.Render(EditableSet());
        Cell(cut, 0, 1).DoubleClick();
        Input(cut).Input("one");

        EditorKey(cut, "Tab");
        Assert.Equal("10", Input(cut).GetAttribute("value"));   // qty, the next editable cell of row 0
        Assert.Equal("one", Cell(cut, 0, 1).TextContent.Trim());

        EditorKey(cut, "Tab", shift: true);
        Assert.Equal("one", Input(cut).GetAttribute("value"));
    }

    [Fact]
    public void Tab_past_the_end_of_a_row_continues_on_the_next_row_skipping_the_key()
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 0, 4);
        Press(cut, "F2");

        EditorKey(cut, "Tab");

        Assert.Equal("akml-grid-0-1-1", Root(cut).GetAttribute("aria-activedescendant"));
        Assert.Equal("bravo", Input(cut).GetAttribute("value"));
    }

    [Fact]
    public void Delete_on_a_focused_row_number_toggles_the_rows_delete()
    {
        var cut = _host.Render(EditableSet());
        MouseDown(cut.Find("[data-testid=results-rownum-1]"));

        Press(cut, "Delete");
        Assert.Contains("akml-row-deleted", cut.Find("[data-testid=results-row-1]").ClassName);

        Press(cut, "Delete");
        Assert.DoesNotContain("akml-row-deleted", cut.Find("[data-testid=results-row-1]").ClassName);
    }

    [Fact]
    public void Delete_while_a_cell_is_edited_or_focused_never_deletes_the_row()
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 1, 1);
        Press(cut, "Delete");
        Assert.DoesNotContain("akml-row-deleted", cut.Find("[data-testid=results-row-1]").ClassName);

        Press(cut, "F2");
        EditorKey(cut, "Delete");
        Assert.True(Editing(cut));
        Assert.DoesNotContain("akml-row-deleted", cut.Find("[data-testid=results-row-1]").ClassName);
    }

    [Fact]
    public void Apply_sends_the_same_request_as_before()
    {
        var cut = _host.Render(EditableSet());
        Cell(cut, 0, 1).DoubleClick();
        Input(cut).Change("ALPHA");

        cut.Find("[data-testid=results-apply]").Click();

        var request = Assert.Single(_host.Exec.Applied);
        Assert.Equal("demo", request.BaseTable);
        Assert.Equal("dbo", request.BaseSchema);
        var edit = Assert.Single(request.Edits);
        Assert.Equal(CrudOp.Update, edit.Op);
        var setCell = Assert.Single(edit.SetCells);
        Assert.Equal("name", setCell.BaseColumnName);
        Assert.Equal("ALPHA", setCell.Value);
        var key = Assert.Single(edit.KeyCells);
        Assert.Equal("id", key.BaseColumnName);
        Assert.Equal("1", key.Value);
        cut.WaitForAssertion(() => Assert.Contains("Applied 1 change(s)", cut.Find("[data-testid=results-apply-message]").TextContent));
    }

    [Fact]
    public void A_failed_Apply_names_the_edit_that_failed_not_the_rolled_back_note_and_keeps_the_edits()
    {
        _host.Exec.ApplyReply = new ApplyChangesResult
        {
            Status = ExecuteStatus.Error,
            ErrorMessage = "Edit 1 failed: Conversion failed when converting date.",
            Results = new[]
            {
                new CrudEditResult { Index = 0, Ok = false, Error = "Rolled back — edit 1 failed." },
                new CrudEditResult { Index = 1, Ok = false, Error = "Conversion failed when converting date." },
            },
        };
        var cut = _host.Render(EditableSet());
        Cell(cut, 0, 1).DoubleClick();
        Input(cut).Change("ALPHA");

        cut.Find("[data-testid=results-apply]").Click();

        cut.WaitForAssertion(() => Assert.Equal("Conversion failed when converting date.",
            cut.Find("[data-testid=results-apply-message]").TextContent.Trim()));
        Assert.Equal("ALPHA", Cell(cut, 0, 1).TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=results-applybar]"));
    }

    [Fact]
    public void Discard_asks_first_and_keeps_the_edits_when_the_answer_is_no()
    {
        var cut = _host.Render(EditableSet());
        Cell(cut, 0, 1).DoubleClick();
        Input(cut).Change("ALPHA");

        _host.Dialogs.ConfirmAnswer = _ => false;
        cut.Find("[data-testid=results-discard]").Click();
        Assert.Single(_host.Dialogs.Confirms);
        Assert.Equal("ALPHA", Cell(cut, 0, 1).TextContent.Trim());

        _host.Dialogs.ConfirmAnswer = _ => true;
        cut.Find("[data-testid=results-discard]").Click();
        Assert.Equal("alpha", Cell(cut, 0, 1).TextContent.Trim());
        Assert.Contains("1 pending edit", _host.Dialogs.Confirms[0].Title);
    }
}
