#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class GridPage : IPageBuilder
    {
        public string Key     => "Grid";
        public string Display => "Queries › Query Results";
        public string Title   => "Results Grid";
        public string Help    => "Controls how query results appear in the grid, including aggregate statistics, NULL highlighting, row numbers, and frozen headers. Also sets whether 15+ digit numbers are exported to Excel as text to avoid rounding.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            var (rowAgg, chkAgg) = ctx.Rows.AddToggle(panel,
                "Aggregate statistics",
                "Show Sum, Avg, Count, Min, Max for selected cells");
            ctx.RegisterSearch("Aggregate statistics", "Show Sum, Avg, Count, Min, Max for selected cells", "Toggle", rowAgg);

            var (rowNull, chkNull) = ctx.Rows.AddToggle(panel,
                "Highlight NULL cells", "Highlight NULL cells in results grid");
            ctx.RegisterSearch("Highlight NULL cells", "Highlight NULL cells in results grid", "Toggle", rowNull);

            var (rowRowNums, chkRowNums) = ctx.Rows.AddToggle(panel,
                "Row numbers", "Show row numbers column");
            ctx.RegisterSearch("Row numbers", "Show row numbers column", "Toggle", rowRowNums);

            var (rowFreeze, chkFreeze) = ctx.Rows.AddToggle(panel,
                "Freeze headers", "Freeze column headers while scrolling");
            ctx.RegisterSearch("Freeze headers", "Freeze column headers while scrolling", "Toggle", rowFreeze);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Excel Export");

            var (rowExcel, chkExcel) = ctx.Rows.AddToggle(panel,
                "Save 15+ digit numbers as text",
                "Numbers with 15 or more digits are saved as text to prevent Excel from rounding them");
            ctx.RegisterSearch("Save 15+ digit numbers as text", "Numbers with 15 or more digits are saved as text to prevent Excel from rounding them", "Toggle", rowExcel);

            return new GridControls(chkAgg, chkNull, chkRowNums, chkFreeze, chkExcel);
        }
    }

    internal sealed class GridControls : IPageControls
    {
        private readonly CheckBox _aggregates;
        private readonly CheckBox _nullHighlight;
        private readonly CheckBox _rowNumbers;
        private readonly CheckBox _freezeHeaders;
        private readonly CheckBox _excelLargeAsText;

        public GridControls(CheckBox agg, CheckBox nullHl, CheckBox rowNums, CheckBox freeze, CheckBox excel)
        {
            _aggregates = agg;
            _nullHighlight = nullHl;
            _rowNumbers = rowNums;
            _freezeHeaders = freeze;
            _excelLargeAsText = excel;
        }

        public void Load(AppSettings settings)
        {
            var g = settings.Grid;
            _aggregates.IsChecked = g.Aggregates;
            _nullHighlight.IsChecked = g.NullHighlight;
            _rowNumbers.IsChecked = g.RowNumbers;
            _freezeHeaders.IsChecked = g.FreezeHeaders;
            _excelLargeAsText.IsChecked = g.ExcelLargeNumberAsText;
        }

        public void Save(AppSettings settings)
        {
            settings.Grid.Aggregates = _aggregates.IsChecked == true;
            settings.Grid.NullHighlight = _nullHighlight.IsChecked == true;
            settings.Grid.RowNumbers = _rowNumbers.IsChecked == true;
            settings.Grid.FreezeHeaders = _freezeHeaders.IsChecked == true;
            settings.Grid.ExcelLargeNumberAsText = _excelLargeAsText.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
