#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Spec 030 T078 (FR-042 / SC-007) — surfaces the previously config-only
    /// <see cref="CompletionPolishSettings"/> family in Options: object/parameter tooltips,
    /// decrypt-encrypted-objects, temp-table IntelliSense, and the Column Picker default sort.
    /// </summary>
    internal sealed class CompletionPolishPage : IPageBuilder
    {
        public string Key     => "CompletionPolish";
        public string Display => "Suggestions › Tooltips";
        public string Title   => "Tooltips & Object Definition";
        public string Help    => "Polish for the completion experience: richer tooltips (object descriptions and active-parameter highlighting), decrypting encrypted object definitions when you have permission, temp-table column completion, and the Column Picker's default sort order.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Tooltips");

            var (rowMsDesc, chkMsDesc) = ctx.Rows.AddToggle(panel,
                "Show object descriptions in tooltips",
                "Surface the MS_Description extended property in object tooltips, with cross-references to related objects");
            ctx.RegisterSearch("Show object descriptions in tooltips", "Surface the MS_Description extended property in object tooltips", "Toggle", rowMsDesc);

            var (rowParam, chkParam) = ctx.Rows.AddToggle(panel,
                "Highlight the active parameter in signature help",
                "Bold the next-expected parameter in function-signature popups");
            ctx.RegisterSearch("Highlight the active parameter in signature help", "Bold the next-expected parameter in function-signature popups", "Toggle", rowParam);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Object scripting");

            var (rowDecrypt, chkDecrypt) = ctx.Rows.AddToggle(panel,
                "Decrypt encrypted procedures and functions",
                "When you have DAC permission, render the plaintext definition of WITH ENCRYPTION objects (with a “decrypted” badge)");
            ctx.RegisterSearch("Decrypt encrypted procedures and functions", "Render the plaintext definition of encrypted objects when permitted", "Toggle", rowDecrypt);

            var (rowTempTable, chkTempTable) = ctx.Rows.AddToggle(panel,
                "Temp-table IntelliSense (#temp columns)",
                "Parse CREATE TABLE #x / SELECT … INTO #x in the active script and offer column completions for those temp tables");
            ctx.RegisterSearch("Temp-table IntelliSense", "Offer column completions for temp tables declared in the active script", "Toggle", rowTempTable);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Column Picker");

            var (rowSort, cboSort) = ctx.Rows.AddDropdown(panel,
                "Default sort",
                new[] { "Table order", "Alphabetical" },
                "Initial ordering of columns in the Column Picker popup");
            ctx.RegisterSearch("Column Picker default sort", "Initial ordering of columns in the Column Picker popup", "Dropdown", rowSort);

            return new CompletionPolishControls(chkMsDesc, chkParam, chkDecrypt, chkTempTable, cboSort);
        }
    }

    internal sealed class CompletionPolishControls : IPageControls
    {
        private readonly CheckBox _msDescription;
        private readonly CheckBox _parameterHighlight;
        private readonly CheckBox _encryptedDecryption;
        private readonly CheckBox _tempTableIntellisense;
        private readonly ComboBox _columnPickerSort;

        public CompletionPolishControls(CheckBox msDescription, CheckBox parameterHighlight,
            CheckBox encryptedDecryption, CheckBox tempTableIntellisense, ComboBox columnPickerSort)
        {
            _msDescription = msDescription;
            _parameterHighlight = parameterHighlight;
            _encryptedDecryption = encryptedDecryption;
            _tempTableIntellisense = tempTableIntellisense;
            _columnPickerSort = columnPickerSort;
        }

        public void Load(AppSettings settings)
        {
            var c = settings.CompletionPolish;
            _msDescription.IsChecked = c.EnableMsDescription;
            _parameterHighlight.IsChecked = c.EnableParameterHighlight;
            _encryptedDecryption.IsChecked = c.EnableEncryptedDecryption;
            _tempTableIntellisense.IsChecked = c.EnableTempTableIntellisense;
            _columnPickerSort.SelectedIndex = (int)c.ColumnPickerDefaultSort;
        }

        public void Save(AppSettings settings)
        {
            settings.CompletionPolish.EnableMsDescription = _msDescription.IsChecked == true;
            settings.CompletionPolish.EnableParameterHighlight = _parameterHighlight.IsChecked == true;
            settings.CompletionPolish.EnableEncryptedDecryption = _encryptedDecryption.IsChecked == true;
            settings.CompletionPolish.EnableTempTableIntellisense = _tempTableIntellisense.IsChecked == true;
            settings.CompletionPolish.ColumnPickerDefaultSort =
                _columnPickerSort.SelectedIndex >= 0 ? (ColumnPickerSortMode)_columnPickerSort.SelectedIndex : ColumnPickerSortMode.TableOrder;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
