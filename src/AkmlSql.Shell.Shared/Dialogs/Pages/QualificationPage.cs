#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Inserted Code › Qualification (Phase 2 C.2). Surfaces the schema-qualification and
    /// column-qualification parts of <see cref="QualificationSettings"/>. <c>SchemaMode</c>
    /// is read by <c>CompletionEngine</c> (A.2). Bracket policy (<c>BracketMode</c>) moved
    /// to the Inserted Code › Special characters page (report §4 rec #1) so SQL Prompt's
    /// single special-characters pane is mirrored.
    /// </summary>
    internal sealed class QualificationPage : IPageBuilder
    {
        public string Key     => "Qualification";
        public string Display => "Inserted Code › Qualification";
        public string Title   => "Qualification";
        public string Help    => "Controls how completion-inserted code is qualified: whether object names carry their schema prefix, and whether column references are prefixed with their table name or alias. Bracket-identifier policy lives on Inserted Code › Special characters.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Schema qualification");

            var (rowSchema, cboSchema) = ctx.Rows.AddDropdown(panel,
                "Qualify object names with schema",
                new[] { "Always", "Non-default schemas only", "Never" },
                "When inserted from completion: never strip the schema, strip it only if it matches the default schema, or never qualify.");
            ctx.RegisterSearch("Qualify object names with schema", "Schema qualification policy for inserted object names", "Dropdown", rowSchema);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Columns");

            var (rowQualifyCols, chkQualifyCols) = ctx.Rows.AddToggle(panel,
                "Qualify columns with table name or alias",
                "Insert column references as 'alias.Column' or 'Table.Column' instead of bare 'Column'.");
            ctx.RegisterSearch("Qualify columns with table name or alias", "Insert column references with their table or alias prefix", "Toggle", rowQualifyCols);

            return new QualificationControls(cboSchema, chkQualifyCols);
        }
    }

    internal sealed class QualificationControls : IPageControls
    {
        private readonly ComboBox _schemaMode;
        private readonly CheckBox _qualifyColumns;

        public QualificationControls(ComboBox schemaMode, CheckBox qualifyColumns)
        {
            _schemaMode = schemaMode;
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
            q.QualifyColumnsWithTableOrAlias = _qualifyColumns.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
