using AkmlSql.Web.Services;
using Bunit;
using Xunit;
using static AkmlSql.Web.Tests.Grid.GridTestHost;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T051, research R7/R12, FR-005, FR-017, FR-030, FR-031; contracts/ui.md §5) — the cell and header
/// menus, Copy as, View value and Save results.
/// </summary>
public sealed class ResultsGridContextMenuTests : IDisposable
{
    private readonly GridTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void The_cell_menu_of_a_read_only_result_lists_the_commands_in_order()
    {
        var cut = _host.Render(ReadOnlySet());

        OpenCellMenu(cut, 1, 1);

        Assert.Equal(new[]
        {
            "Copy", "Copy with headers", "Copy as", "Select all", "View value",
            "Save results as CSV…", "Save results as tab-delimited…",
        }, MenuLabels(cut, "results-context-menu"));
        var shortcuts = cut.FindAll("[data-testid=results-context-menu] .akml-menu-shortcut").Select(s => s.TextContent).ToArray();
        Assert.Equal(new[] { "Ctrl+C", "Ctrl+Shift+C", "Ctrl+A" }, shortcuts);
    }

    [Fact]
    public void An_editable_result_adds_Set_to_NULL_and_after_a_separator_Delete_row()
    {
        var cut = _host.Render(EditableSet());

        OpenCellMenu(cut, 0, 1);

        Assert.Equal(new[]
        {
            "Copy", "Copy with headers", "Copy as", "Select all", "View value", "Set to NULL",
            "Save results as CSV…", "Save results as tab-delimited…", "—", "Delete row",
        }, MenuLabels(cut, "results-context-menu"));
        Assert.Equal("Ctrl+0", cut.Find("[data-testid=results-menu-set-null] .akml-menu-shortcut").TextContent);
    }

    [Fact]
    public void Delete_row_from_the_menu_deletes_and_then_restores()
    {
        var cut = _host.Render(EditableSet());

        OpenCellMenu(cut, 1, 1);
        cut.Find("[data-testid=results-menu-delete-row]").Click();
        cut.WaitForAssertion(() => Assert.Contains("akml-row-deleted", cut.Find("[data-testid=results-row-1]").ClassName));

        OpenCellMenu(cut, 1, 1);
        Assert.Equal("Restore row", cut.Find("[data-testid=results-menu-delete-row] .akml-menu-label").TextContent);
        cut.Find("[data-testid=results-menu-delete-row]").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("akml-row-deleted", cut.Find("[data-testid=results-row-1]").ClassName));
    }

    [Fact]
    public void A_right_click_outside_the_selection_selects_the_cell_and_inside_keeps_it()
    {
        var cut = _host.Render(ReadOnlySet());
        ClickCell(cut, 0, 0);
        ClickCell(cut, 2, 1, shift: true);

        OpenCellMenu(cut, 1, 1);
        Assert.Equal("true", Cell(cut, 0, 0).GetAttribute("aria-selected"));
        cut.Find("[data-testid=results-menu-select-all]");   // menu open

        cut.InvokeAsync(() => cut.Instance.OnContextMenuFromJs(10, 10, 4, 2, false));
        Assert.Equal("false", Cell(cut, 0, 0).GetAttribute("aria-selected"));
        Assert.Equal("true", Cell(cut, 4, 2).GetAttribute("aria-selected"));
    }

    [Fact]
    public void The_keyboard_route_opens_the_menu_at_the_focused_cell()
    {
        // The Menu key and Shift+F10 fire contextmenu on the grid root; the module routes it to the focused
        // cell and passes that cell's position (akml-results-grid.js, isKeyboardMenu).
        var cut = _host.Render(ReadOnlySet());
        ClickCell(cut, 0, 0);
        Press(cut, "ArrowDown");
        Press(cut, "ArrowDown");

        cut.InvokeAsync(() => cut.Instance.OnContextMenuFromJs(120, 90, 2, 0, false));

        var menu = cut.Find("[data-testid=results-context-menu]");
        Assert.Contains("left:120px;top:90px", menu.GetAttribute("style"));
        Assert.Equal("akml-grid-0-2-0", Root(cut).GetAttribute("aria-activedescendant"));
    }

    [Fact]
    public void An_Escape_that_reaches_the_grid_before_the_menu_has_focus_still_closes_it()
    {
        var cut = _host.Render(ReadOnlySet());
        OpenCellMenu(cut, 0, 0);

        Press(cut, "Escape");

        Assert.Empty(cut.FindAll("[data-testid=results-context-menu]"));
    }

    [Fact]
    public void The_header_menu_lists_the_column_commands_in_order()
    {
        var cut = _host.Render(ReadOnlySet());

        OpenHeaderMenu(cut, 1);

        Assert.Equal(new[]
        {
            "Auto-fit this column", "Auto-fit all columns", "Reset column widths", "Copy column name", "—",
            "Sort ascending", "Sort descending", "Clear sort",
        }, MenuLabels(cut, "results-header-menu"));
        Assert.Equal("true", cut.Find("[data-testid=results-menu-sort-clear]").GetAttribute("aria-disabled"));
    }

    [Fact]
    public void Copy_column_name_copies_the_name()
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Mode = JSRuntimeMode.Loose;
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var cut = _host.Render(ReadOnlySet());

        OpenHeaderMenu(cut, 2);
        cut.Find("[data-testid=results-menu-copy-name]").Click();

        cut.WaitForAssertion(() => Assert.Equal("created", (string)ui.Invocations["copyText"].Single().Arguments[0]!));
    }

    [Theory]
    [InlineData("results-menu-copy-csv", "id,name\r\n1,alpha\r\n")]
    [InlineData("results-menu-copy-markdown", "| id | name |\r\n| --- | --- |\r\n| 1 | alpha |\r\n")]
    public void Copy_as_formats_the_selection(string item, string expected)
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Mode = JSRuntimeMode.Loose;
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var cut = _host.Render(ReadOnlySet());
        ClickCell(cut, 0, 0);
        ClickCell(cut, 0, 1, shift: true);

        OpenCellMenu(cut, 0, 0);
        cut.Find("[data-testid=results-menu-copy-as]").Click();
        cut.Find($"[data-testid={item}]").Click();

        cut.WaitForAssertion(() => Assert.Equal(expected, (string)ui.Invocations["copyText"].Single().Arguments[0]!));
    }

    [Fact]
    public void Copy_as_INSERT_uses_the_SSMS_placeholder_table()
    {
        var ui = _host.Context.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Mode = JSRuntimeMode.Loose;
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var cut = _host.Render(ReadOnlySet());
        ClickCell(cut, 0, 1);

        OpenCellMenu(cut, 0, 1);
        cut.Find("[data-testid=results-menu-copy-as]").Click();
        cut.Find("[data-testid=results-menu-copy-insert]").Click();

        cut.WaitForAssertion(() => Assert.StartsWith("INSERT INTO [TableName]", (string)ui.Invocations["copyText"].Single().Arguments[0]!));
    }

    [Fact]
    public void View_value_is_disabled_for_NULL()
    {
        var cut = _host.Render(EditableSet());

        OpenCellMenu(cut, 2, 1);   // name is NULL

        var item = cut.Find("[data-testid=results-menu-view-value]");
        Assert.Equal("true", item.GetAttribute("aria-disabled"));
        Assert.Equal("The value is NULL.", item.GetAttribute("title"));
    }

    [Fact]
    public void View_value_opens_the_value_dialog()
    {
        var cut = _host.Render(ReadOnlySet());

        OpenCellMenu(cut, 2, 1);
        cut.Find("[data-testid=results-menu-view-value]").Click();

        var dialog = cut.Find("[data-testid=results-view-value]");
        Assert.Equal("name", cut.Find("[data-testid=results-view-value-title]").TextContent);
        Assert.Equal("nvarchar(50)", cut.Find("[data-testid=results-view-value-type]").TextContent);
        Assert.Equal("charlie", cut.Find("[data-testid=results-view-value-text]").TextContent);
        Assert.Equal("dialog", dialog.GetAttribute("role"));

        cut.Find("[data-testid=results-view-value-close]").Click();
        Assert.Empty(cut.FindAll("[data-testid=results-view-value]"));
    }

    [Fact]
    public void Save_results_as_CSV_downloads_the_whole_result_with_a_header_row_and_BOM()
    {
        var download = _host.Context.JSInterop.SetupModule("./js/akml-download.js");
        download.SetupVoid("downloadText", _ => true).SetVoidResult();
        var cut = _host.Render(ReadOnlySet(
            new string?[] { "1", "a, b", null },
            new string?[] { "2", "say \"hi\"", "2025-10-01T08:00:00.0000000" }), documentName: "Orders.sql");

        OpenCellMenu(cut, 0, 0);
        cut.Find("[data-testid=results-save-csv]").Click();

        var call = Assert.Single(download.Invocations["downloadText"]);
        Assert.Equal("Orders.csv", call.Arguments[0]);
        Assert.Equal("text/csv;charset=utf-8", call.Arguments[1]);
        Assert.Equal("id,name,created\r\n1,\"a, b\",NULL\r\n2,\"say \"\"hi\"\"\",2025-10-01 08:00:00.000\r\n", call.Arguments[2]);
        Assert.Equal(true, call.Arguments[3]);
        Assert.Empty(_host.Dialogs.Confirms);
    }

    [Fact]
    public void Save_results_as_tab_delimited_falls_back_to_the_default_document_name()
    {
        var download = _host.Context.JSInterop.SetupModule("./js/akml-download.js");
        download.SetupVoid("downloadText", _ => true).SetVoidResult();
        var cut = _host.Render(ReadOnlySet());

        OpenCellMenu(cut, 0, 0);
        cut.Find("[data-testid=results-save-tsv]").Click();

        var call = Assert.Single(download.Invocations["downloadText"]);
        Assert.Equal("SQLQuery1.tsv", call.Arguments[0]);
        Assert.StartsWith("id\tname\tcreated\r\n1\talpha\t", (string)call.Arguments[2]!);
        Assert.Contains(_host.Notes.Sent, n => n.Kind == NotificationKind.Success && n.Text.Contains("SQLQuery1.tsv"));
    }

    [Fact]
    public void Saving_a_truncated_result_or_one_holding_engine_cut_values_asks_first()
    {
        var download = _host.Context.JSInterop.SetupModule("./js/akml-download.js");
        download.SetupVoid("downloadText", _ => true).SetVoidResult();
        var set = ReadOnlySet(new string?[] { "1", "[text 70000 chars]", null });
        set.Truncated = true;
        var cut = _host.Render(set);
        _host.Dialogs.ConfirmAnswer = _ => false;

        OpenCellMenu(cut, 0, 0);
        cut.Find("[data-testid=results-save-csv]").Click();

        var ask = Assert.Single(_host.Dialogs.Confirms);
        Assert.Equal(2, ask.Details!.Count);
        Assert.Empty(download.Invocations["downloadText"]);
    }

    [Theory]
    [InlineData("Orders.sql", ".csv", "Orders.csv")]
    [InlineData("  ", ".tsv", "SQLQuery1.tsv")]
    [InlineData(null, ".csv", "SQLQuery1.csv")]
    [InlineData("a/b:c?.sql", ".csv", "a_b_c_.csv")]
    public void The_file_is_named_after_the_document(string? document, string extension, string expected)
    {
        Assert.Equal(expected, Shared.ResultSetGrid.SaveFileName(document, extension));
    }
}
