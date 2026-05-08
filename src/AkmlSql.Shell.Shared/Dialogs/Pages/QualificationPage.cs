#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Inserted Code › Qualification &amp; Brackets (Phase 2 C.2). Surfaces
    /// <see cref="QualificationSettings"/>. <c>SchemaMode</c> is read by
    /// <c>CompletionEngine</c> (A.2). <c>BracketMode</c> is recorded but full
    /// bracket policy is deferred — current engine matches the
    /// <c>WhenRequired</c> default.
    /// </summary>
    internal sealed class QualificationPage : IPageBuilder
    {
        public string Key     => "Qualification";
        public string Display => "Inserted Code › Qualification & Brackets";
        public string Title   => "Qualification & Brackets";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Schema qualification");

            var (rowSchema, cboSchema) = ctx.Rows.AddDropdown(panel,
                "Qualify object names with schema",
                new[] { "Always", "Non-default schemas only", "Never" },
                "When inserted from completion: never strip the schema, strip it only if it matches the default schema, or never qualify.");
            ctx.RegisterSearch("Qualify object names with schema", "Schema qualification policy for inserted object names", "Dropdown", rowSchema);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Brackets");

            var (rowBracket, cboBracket) = ctx.Rows.AddDropdown(panel,
                "Bracket identifiers",
                new[] { "Always", "When required", "Never" },
                "When to wrap inserted identifiers in [square brackets]: always, only when needed (reserved words / spaces), or never.");
            ctx.RegisterSearch("Bracket identifiers", "Bracket policy for inserted identifiers", "Dropdown", rowBracket);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Columns");

            var (rowQualifyCols, chkQualifyCols) = ctx.Rows.AddToggle(panel,
                "Qualify columns with table name or alias",
                "Insert column references as 'alias.Column' or 'Table.Column' instead of bare 'Column'.");
            ctx.RegisterSearch("Qualify columns with table name or alias", "Insert column references with their table or alias prefix", "Toggle", rowQualifyCols);

            return new QualificationControls(cboSchema, cboBracket, chkQualifyCols);
        }
    }

    internal sealed class QualificationControls : IPageControls
    {
        private readonly ComboBox _schemaMode;
        private readonly ComboBox _bracketMode;
        private readonly CheckBox _qualifyColumns;

        public QualificationControls(ComboBox schemaMode, ComboBox bracketMode, CheckBox qualifyColumns)
        {
            _schemaMode = schemaMode;
            _bracketMode = bracketMode;
            _qualifyColumns = qualifyColumns;
        }

        public void Load(AppSettings settings)
        {
            var q = settings.IntelliSense.Qualification;
            _schemaMode.SelectedIndex = q.SchemaMode switch
            {
                SchemaQualifyMode.Always          => 0,
                SchemaQualifyMode.NonDefaultOnly  => 1,
                SchemaQualifyMode.Never           => 2,
                _ => 1,
            };
            _bracketMode.SelectedIndex = q.BracketMode switch
            {
                BracketMode.Always       => 0,
                BracketMode.WhenRequired => 1,
                BracketMode.Never        => 2,
                _ => 1,
            };
            _qualifyColumns.IsChecked = q.QualifyColumnsWithTableOrAlias;
        }

        public void Save(AppSettings settings)
        {
            var q = settings.IntelliSense.Qualification;
            q.SchemaMode = _schemaMode.SelectedIndex switch
            {
                0 => SchemaQualifyMode.Always,
                2 => SchemaQualifyMode.Never,
                _ => SchemaQualifyMode.NonDefaultOnly,
            };
            q.BracketMode = _bracketMode.SelectedIndex switch
            {
                0 => BracketMode.Always,
                2 => BracketMode.Never,
                _ => BracketMode.WhenRequired,
            };
            q.QualifyColumnsWithTableOrAlias = _qualifyColumns.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
