using AkmlSql.Core.Ipc.Messages;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T049, research R9/R14, FR-014, FR-016) — Set to NULL, empty strings, invalid cells and the row
/// glyphs. In <see cref="GridTestHost.EditableSet"/>: name nvarchar NULL, qty int NOT NULL, flag bit NULL,
/// blob varbinary NULL.
/// </summary>
public sealed class ResultsGridNullAndInvalidTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static void Edit(IRenderedComponent<Shared.ResultSetGrid> cut, int r, int c, string value)
    {
        Cell(cut, r, c).DoubleClick();
        var input = cut.Find("[data-testid=results-cell-input]");
        input.Input(value);
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    [Fact]
    public void Ctrl_0_stores_a_pending_NULL_that_Apply_sends()
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 0, 1);

        Press(cut, "0", ctrl: true);

        var cell = Cell(cut, 0, 1);
        Assert.Equal("NULL", cell.TextContent.Trim());
        Assert.Contains("akml-cell-null", cell.ClassName);
        cut.Find("[data-testid=results-apply]").Click();
        var setCell = Assert.Single(Assert.Single(_host.Exec.Applied).Edits).SetCells.Single();
        Assert.Equal("name", setCell.BaseColumnName);
        Assert.Null(setCell.Value);
    }

    [Fact]
    public void Set_to_NULL_is_disabled_with_the_reason_on_a_NOT_NULL_column()
    {
        var cut = _host.Render(EditableSet());

        OpenCellMenu(cut, 0, 2);

        var item = cut.Find("[data-testid=results-menu-set-null]");
        Assert.Equal("true", item.GetAttribute("aria-disabled"));
        Assert.Equal("qty does not allow NULL.", item.GetAttribute("title"));
    }

    [Fact]
    public void Ctrl_0_on_a_NOT_NULL_column_changes_nothing_and_says_why()
    {
        var cut = _host.Render(EditableSet());
        ClickCell(cut, 0, 2);

        Press(cut, "0", ctrl: true);

        Assert.Equal("10", Cell(cut, 0, 2).TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=results-applybar]"));
        Assert.Contains(_host.Notes.Sent, n => n.Text == "qty does not allow NULL.");
    }

    [Fact]
    public void Set_to_NULL_from_the_menu_stores_NULL()
    {
        var cut = _host.Render(EditableSet());
        OpenCellMenu(cut, 1, 3);

        cut.Find("[data-testid=results-menu-set-null]").Click();

        cut.WaitForAssertion(() => Assert.Equal("NULL", Cell(cut, 1, 3).TextContent.Trim()));
    }

    [Fact]
    public void An_emptied_nvarchar_cell_stores_an_empty_string()
    {
        var cut = _host.Render(EditableSet());

        Edit(cut, 0, 1, "");

        var cell = Cell(cut, 0, 1);
        Assert.Equal("", cell.TextContent.Trim());
        Assert.Equal("(empty string)", cell.GetAttribute("title"));
        Assert.Empty(cut.FindAll("[data-testid=results-apply-blocked]"));
        cut.Find("[data-testid=results-apply]").Click();
        Assert.Equal("", Assert.Single(Assert.Single(_host.Exec.Applied).Edits).SetCells.Single().Value);
    }

    [Theory]
    [InlineData(2)]   // int
    [InlineData(3)]   // bit
    [InlineData(4)]   // varbinary
    public void An_emptied_non_character_cell_is_invalid_and_blocks_Apply(int col)
    {
        var cut = _host.Render(EditableSet());

        Edit(cut, 0, col, "");

        var glyph = cut.Find($"[data-testid=results-cell-invalid-0-{col}]");
        Assert.Equal("Enter a value or set NULL", glyph.GetAttribute("title"));
        Assert.Contains("akml-cell-invalid", Cell(cut, 0, col).ClassName);
        Assert.True(cut.Find("[data-testid=results-apply]").HasAttribute("disabled"));
        Assert.Contains("1 cell needs a value", cut.Find("[data-testid=results-apply-blocked]").TextContent);

        cut.Find("[data-testid=results-apply]").Click();
        Assert.Empty(_host.Exec.Applied);
    }

    [Fact]
    public void Setting_NULL_on_an_invalid_cell_makes_it_valid_again()
    {
        var cut = _host.Render(EditableSet());
        Edit(cut, 1, 3, "");
        ClickCell(cut, 1, 3);

        Press(cut, "0", ctrl: true);

        Assert.Empty(cut.FindAll("[data-testid=results-cell-invalid-1-3]"));
        Assert.False(cut.Find("[data-testid=results-apply]").HasAttribute("disabled"));
    }

    [Fact]
    public void An_unset_NOT_NULL_column_of_a_new_row_is_invalid()
    {
        var cut = _host.Render(EditableSet());

        cut.Find("[data-testid=results-add-row]").Click();

        Assert.NotNull(cut.Find("[data-testid=results-insert-invalid-0-2]"));            // qty NOT NULL
        Assert.Empty(cut.FindAll("[data-testid=results-insert-invalid-0-1]"));            // name allows NULL
        Assert.Empty(cut.FindAll("[data-testid=results-insert-invalid-0-0]"));            // identity
        Assert.True(cut.Find("[data-testid=results-apply]").HasAttribute("disabled"));

        cut.Find("[data-testid=results-insert-cell-0-2]").Click();
        var input = cut.Find("[data-testid=results-cell-input]");
        input.Input("5");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(cut.FindAll("[data-testid=results-insert-invalid-0-2]"));
        Assert.False(cut.Find("[data-testid=results-apply]").HasAttribute("disabled"));
    }

    [Fact]
    public void NULL_and_the_empty_string_look_different()
    {
        var cut = _host.Render(EditableSet(
            new string?[] { "1", null, "1", null, null },
            new string?[] { "2", "", "1", null, null }));

        var nullCell = Cell(cut, 0, 1);
        var emptyCell = Cell(cut, 1, 1);

        Assert.Equal("NULL", nullCell.TextContent.Trim());
        Assert.Contains("akml-cell-null", nullCell.ClassName);
        Assert.Equal("", emptyCell.TextContent.Trim());
        Assert.DoesNotContain("akml-cell-null", emptyCell.ClassName);
        Assert.Equal("(empty string)", emptyCell.GetAttribute("title"));
    }

    [Fact]
    public void Edited_new_and_deleted_rows_carry_a_glyph_as_well_as_the_edge_colour()
    {
        var cut = _host.Render(EditableSet());

        Edit(cut, 0, 1, "changed");
        cut.Find("[data-testid=results-delete-1]").Click();
        cut.Find("[data-testid=results-add-row]").Click();

        Assert.Contains("akml-row-edited", cut.Find("[data-testid=results-row-0]").ClassName);
        Assert.Contains("#akml-icon-pencil", cut.Find("[data-testid=results-rownum-0]").InnerHtml);
        Assert.Contains("akml-row-deleted", cut.Find("[data-testid=results-row-1]").ClassName);
        Assert.Contains("#akml-icon-x", cut.Find("[data-testid=results-rownum-1]").InnerHtml.Split("akml-rownum")[0]);
        Assert.Contains("akml-row-new", cut.Find("[data-testid=results-insert-0]").ClassName);
        Assert.Contains("#akml-icon-plus", cut.Find("[data-testid=results-insert-0]").InnerHtml);
        Assert.DoesNotContain("akml-icon-pencil", cut.Find("[data-testid=results-rownum-2]").InnerHtml);
    }

    [Fact]
    public void A_deleted_rows_pending_edits_are_dropped_and_its_cells_cannot_be_edited()
    {
        var cut = _host.Render(EditableSet());
        Edit(cut, 0, 1, "changed");

        cut.Find("[data-testid=results-delete-0]").Click();
        ClickCell(cut, 0, 1);
        Press(cut, "F2");

        Assert.Empty(cut.FindAll("[data-testid=results-cell-input]"));
        Assert.DoesNotContain("akml-cell-editable", Cell(cut, 0, 1).ClassName);
        cut.Find("[data-testid=results-apply]").Click();
        Assert.Equal(CrudOp.Delete, Assert.Single(Assert.Single(_host.Exec.Applied).Edits).Op);
    }
}
