#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Inserted code › Qualification &amp; brackets (the 2026-10 "rebalanced tree", design B):
    /// how an inserted name is written — its schema prefix (<see cref="QualificationSettings.SchemaMode"/>,
    /// read by <c>CompletionEngine</c>), the [bracket] policy (<see cref="QualificationSettings.BracketMode"/>)
    /// and the parentheses added after a function or data type
    /// (<see cref="SpecialCharacterSettings.AddParentheses"/>). The last two came from the Special
    /// characters section, which now holds the closing characters only; they save to the same
    /// settings. Spec 040 (OPT-01): "Qualify columns with table name or alias" changed nothing
    /// and is hidden; its saved value is kept.
    /// </summary>
    internal sealed class QualificationPage : IPageBuilder
    {
        public string Key     => "Qualification";
        public string Display => "Inserted code › Qualification & brackets";
        public string Title   => "Qualification & brackets";
        public string HelpTopic => "topics/options#qualification";
        public string Help    => "How names inserted from the suggestions box are written: with or without their schema prefix, with or without [square brackets], and whether a function or data type gets its parentheses.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Qualification & brackets");

            var (left, right) = ctx.Rows.AddColumns(panel);

            var (rowSchema, cboSchema) = ctx.Rows.AddDropdown(left,
                "Qualify object names with schema",
                new[] { "Always", "Non-default schemas only", "Never" }, "");
            ctx.Rows.Tip(rowSchema, "When inserted from completion: never strip the schema, strip it only if it matches the default schema, or never qualify.");
            ctx.RegisterSearch("Qualify object names with schema", "Schema qualification policy for inserted object names", "Dropdown", rowSchema);

            var (rowBracket, cboBracket) = ctx.Rows.AddDropdown(right,
                "Bracket identifiers",
                new[] { "Always", "When required", "Never" }, "");
            ctx.Rows.Tip(rowBracket, "When to wrap inserted identifiers in [square brackets]: always, only when needed (reserved words / spaces), or never.");
            ctx.RegisterSearch("Bracket identifiers", "Bracket policy for inserted identifiers", "Dropdown", rowBracket);

            var (rowAddParens, chkAddParens) = ctx.Rows.AddToggle(panel,
                "Add parentheses ( ) when inserting a function or data type",
                "After a function or parameterized data type from the list");
            ctx.RegisterSearch("Add parentheses ( ) when inserting a function or data type", "Automatically add parentheses when a function or parameterized data type is inserted from the completion list", "Toggle", rowAddParens);

            return new QualificationControls(cboSchema, cboBracket, chkAddParens);
        }
    }

    internal sealed class QualificationControls : IPageControls
    {
        private readonly ComboBox _schemaMode;

        public QualificationControls(ComboBox schemaMode, ComboBox bracketMode, CheckBox addParentheses)
        {
            _schemaMode = schemaMode;
            BracketMode = bracketMode;
            AddParentheses = addParentheses;
        }

        /// <summary>The [bracket] policy dropdown: Always / When required / Never (test seam).</summary>
        internal ComboBox BracketMode { get; }

        /// <summary>The function-parentheses toggle (test seam).</summary>
        internal CheckBox AddParentheses { get; }

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
            BracketMode.SelectedIndex = q.BracketMode switch
            {
                Core.Config.BracketMode.Always       => 0,
                Core.Config.BracketMode.WhenRequired => 1,
                Core.Config.BracketMode.Never        => 2,
                _ => 1,
            };
            AddParentheses.IsChecked = settings.IntelliSense.SpecialCharOptions.AddParentheses;
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
            q.BracketMode = BracketMode.SelectedIndex switch
            {
                0 => Core.Config.BracketMode.Always,
                2 => Core.Config.BracketMode.Never,
                _ => Core.Config.BracketMode.WhenRequired,
            };
            settings.IntelliSense.SpecialCharOptions.AddParentheses = AddParentheses.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
