#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Inserted code › Objects &amp; statements (Phase 2 C.3; fitted in the 2026-10 design B).
    /// Surfaces <see cref="InsertOptionsSettings"/>. The flags are read by the lightweight
    /// refactoring operations <c>ExpandInsertColumnsOperation</c> (IncludeColumns,
    /// IncludeDefaultsAsComments) and <c>ExpandExecParametersOperation</c>
    /// (IncludeProcParamInfo) — wired in A.3.
    /// </summary>
    internal sealed class InsertStatementsPage : IPageBuilder
    {
        public string Key     => "InsertOptions";
        public string Display => "Inserted code › Objects & statements";
        public string Title   => "Objects & statements";
        public string HelpTopic => "topics/options#objects-statements";
        public string Help    => "Controls how INSERT INTO statements are expanded — whether to add an explicit column list and annotate defaults as comments — and whether EXEC calls convert positional arguments to named parameters.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Statements");

            var (left, right) = ctx.Rows.AddColumns(panel);

            var (rowColumns, chkColumns) = ctx.Rows.AddToggle(left,
                "Insert column names", "INSERT INTO t (Id, Name, …)");
            ctx.Rows.Tip(rowColumns, "Replace the bare INSERT INTO target with an explicit column list (Id, Name, ...).");
            ctx.RegisterSearch("Insert column names", "Replace the bare INSERT INTO target with an explicit column list", "Toggle", rowColumns);

            var (rowDefaults, chkDefaults) = ctx.Rows.AddToggle(right,
                "Insert default values as comments", "Each column's default as an inline comment");
            ctx.Rows.Tip(rowDefaults, "Annotate each column with its default expression as an inline comment.");
            ctx.RegisterSearch("Insert default values as comments", "Annotate each column with its default expression as an inline comment", "Toggle", rowDefaults);

            var (rowProcParams, chkProcParams) = ctx.Rows.AddToggle(left,
                "Convert positional parameters to named", "EXEC dbo.proc @id = 1, @name = …");
            ctx.Rows.Tip(rowProcParams, "Rewrite EXEC dbo.proc 1, 'x' as EXEC dbo.proc @id = 1, @name = 'x' using procedure parameter names.");
            ctx.RegisterSearch("Convert positional parameters to named", "Rewrite EXEC positional parameters to named form", "Toggle", rowProcParams);

            return new InsertStatementsControls(chkColumns, chkDefaults, chkProcParams);
        }
    }

    internal sealed class InsertStatementsControls : IPageControls
    {
        private readonly CheckBox _includeColumns;
        private readonly CheckBox _includeDefaults;
        private readonly CheckBox _includeProcParamInfo;

        public InsertStatementsControls(CheckBox columns, CheckBox defaults, CheckBox procParams)
        {
            _includeColumns = columns;
            _includeDefaults = defaults;
            _includeProcParamInfo = procParams;
        }

        public void Load(AppSettings settings)
        {
            var io = settings.IntelliSense.InsertOptions;
            _includeColumns.IsChecked = io.IncludeColumns;
            _includeDefaults.IsChecked = io.IncludeDefaultsAsComments;
            _includeProcParamInfo.IsChecked = io.IncludeProcParamInfo;
        }

        public void Save(AppSettings settings)
        {
            var io = settings.IntelliSense.InsertOptions;
            io.IncludeColumns = _includeColumns.IsChecked == true;
            io.IncludeDefaultsAsComments = _includeDefaults.IsChecked == true;
            io.IncludeProcParamInfo = _includeProcParamInfo.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
