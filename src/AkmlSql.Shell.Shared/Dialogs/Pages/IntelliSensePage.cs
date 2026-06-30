#nullable enable
using System.Globalization;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class IntelliSensePage : IPageBuilder
    {
        public string Key     => "IntelliSense";
        public string Display => "Suggestions › Behavior";
        public string Title   => "IntelliSense";
        public string Help    => "Controls AKML SQL completion behavior — auto-triggering, fuzzy matching, suggestion count and trigger delay, keyword casing, column/PK/FK detail badges, FK-assisted JOIN and alias generation, special-character handling, commit keys, snippets, and SQL-auth credentials used for IntelliSense.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Core");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable IntelliSense", "Master switch for all IntelliSense features");
            ctx.RegisterSearch("Enable IntelliSense", "Master switch for all IntelliSense features", "Toggle", rowEnabled);

            var (rowAutoTrig, chkAutoTrig) = ctx.Rows.AddToggle(panel,
                "Auto-trigger completions while typing",
                "Show completion list automatically without Ctrl+Space");
            ctx.RegisterSearch("Auto-trigger completions while typing", "Show completion list automatically without Ctrl+Space", "Toggle", rowAutoTrig);

            var (rowAfterDot, chkAfterDot) = ctx.Rows.AddToggle(panel,
                "Trigger after dot",
                "Auto-complete after typing '.' for table.column references");
            ctx.RegisterSearch("Trigger after dot", "Auto-complete after typing '.' for table.column references", "Toggle", rowAfterDot);

            var (rowFuzzy, chkFuzzy) = ctx.Rows.AddToggle(panel,
                "Enable fuzzy matching",
                "Substring and approximate matching in addition to prefix");
            ctx.RegisterSearch("Enable fuzzy matching", "Substring and approximate matching in addition to prefix", "Toggle", rowFuzzy);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Display");

            var (rowMaxSugg, sldMaxSugg, lblMaxSugg) = ctx.Rows.AddSlider(panel,
                "Maximum suggestions", 5, 200, 50,
                "Maximum number of items shown in the completion list");
            ctx.RegisterSearch("Maximum suggestions", "Maximum number of items shown in the completion list", "Slider", rowMaxSugg);

            var (rowTrigDelay, sldTrigDelay, lblTrigDelay) = ctx.Rows.AddSlider(panel,
                "Trigger delay (ms)", 0, 2000, 100,
                "Debounce delay before showing completions");
            ctx.RegisterSearch("Trigger delay (ms)", "Debounce delay before showing completions", "Slider", rowTrigDelay);

            var (rowCase, cboCase) = ctx.Rows.AddDropdown(panel,
                "Keyword casing",
                new[] { "UPPER", "lower", "PascalCase", "As-Is" },
                "Casing applied to SQL keywords inserted by IntelliSense");
            ctx.RegisterSearch("Keyword casing", "Casing applied to SQL keywords inserted by IntelliSense", "Dropdown", rowCase);

            var (rowDataTypes, chkDataTypes) = ctx.Rows.AddToggle(panel,
                "Show column data types",
                "Display data type information in completion details");
            ctx.RegisterSearch("Show column data types", "Display data type information in completion details", "Toggle", rowDataTypes);

            var (rowNullable, chkNullable) = ctx.Rows.AddToggle(panel,
                "Show nullability info",
                "Show NOT NULL / NULL status in completion details");
            ctx.RegisterSearch("Show nullability info", "Show NOT NULL / NULL status in completion details", "Toggle", rowNullable);

            var (rowPkFk, chkPkFk) = ctx.Rows.AddToggle(panel,
                "Show PK/FK indicators",
                "Show primary key and foreign key badges");
            ctx.RegisterSearch("Show PK/FK indicators", "Show primary key and foreign key badges", "Toggle", rowPkFk);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Assistance");

            var (rowJoin, chkJoin) = ctx.Rows.AddToggle(panel,
                "JOIN clause assistance",
                "Master switch for FK-assisted JOIN completion. When on: after typing 'JOIN', FK-related tables are suggested first with a full ON clause inserted; inside 'ON', ready-made FK equality predicates are suggested. Orthogonal to Tables Alias. Default: on.");
            ctx.RegisterSearch("JOIN clause assistance", "Master switch for FK-assisted JOIN completion", "Toggle", rowJoin);

            var (rowAlias, chkAlias) = ctx.Rows.AddToggle(panel,
                "Tables Alias",
                "When on, completion generates new aliases for inserted tables (e.g. 'Orders o ON o.CustomerId = c.Id'). When off, FK JOIN suggestions still fire but the target table is referenced by its bare name ('Orders ON Orders.CustomerId = c.Id'). Default: off.");
            ctx.RegisterSearch("Tables Alias", "Generate new aliases for inserted tables in JOIN completions", "Toggle", rowAlias);

            var (rowDisableNative, chkDisableNative) = ctx.Rows.AddToggle(panel,
                "Disable native SSMS IntelliSense",
                "Recommended to avoid conflicts with AKML SQL IntelliSense");
            ctx.RegisterSearch("Disable native SSMS IntelliSense", "Recommended to avoid conflicts with AKML SQL IntelliSense", "Toggle", rowDisableNative);

            // Spec 030 T080 (FR-043) — special-character handling.
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Special characters");

            var (rowAutoClose, chkAutoClose) = ctx.Rows.AddToggle(panel,
                "Auto-close matching characters",
                "Typing an opening bracket, brace, or quote inserts the matching closing character");
            ctx.RegisterSearch("Auto-close matching characters", "Typing an opening bracket, brace, or quote inserts the matching closing character", "Toggle", rowAutoClose);

            var (rowAddParens, chkAddParens) = ctx.Rows.AddToggle(panel,
                "Add parentheses after functions",
                "Automatically add parentheses when a function is inserted from the completion list");
            ctx.RegisterSearch("Add parentheses after functions", "Automatically add parentheses when a function is inserted from the completion list", "Toggle", rowAddParens);

            // Spec 030 T078 (FR-042) — completion commit keys.
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Commit keys");

            var (rowSpaceCommit, chkSpaceCommit) = ctx.Rows.AddToggle(panel,
                "Commit with Space",
                "Press Space to commit the highlighted completion item (SQL Prompt style). Off by default — most SSMS users expect Space to insert a literal space");
            ctx.RegisterSearch("Commit with Space", "Press Space to commit the highlighted completion item (SQL Prompt style)", "Toggle", rowSpaceCommit);

            var (rowDotCommit, chkDotCommit) = ctx.Rows.AddToggle(panel,
                "Commit with Dot",
                "Press '.' to commit the highlighted completion item and continue with a member access");
            ctx.RegisterSearch("Commit with Dot", "Press '.' to commit the highlighted completion item and continue with a member access", "Toggle", rowDotCommit);

            var (rowSnippets, chkSnippets) = ctx.Rows.AddToggle(panel,
                "Show snippets in the completion list",
                "Include snippet shortcuts (sel, ssf, ins, …) in the completion popup");
            ctx.RegisterSearch("Show snippets in the completion list", "Include snippet shortcuts (sel, ssf, ins, …) in the completion popup", "Toggle", rowSnippets);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "SQL authentication");

            var (rowSqlCreds, chkSqlCreds) = ctx.Rows.AddToggle(panel,
                "Use SQL Server-auth credentials for IntelliSense",
                "When on (default), AKML reuses the SQL password SSMS already holds for the connection — or a stored one — so SQL-auth windows get IntelliSense with no prompt. Off: SQL-auth windows are skipped. Windows / Azure AD connections are unaffected either way.");
            ctx.RegisterSearch("Use SQL Server-auth credentials for IntelliSense", "Reuse the SSMS-held or stored SQL password so SQL-auth windows get IntelliSense", "Toggle", rowSqlCreds);

            var (rowManageCreds, btnManageCreds) = ctx.Rows.AddButton(panel,
                "Saved SQL passwords",
                "Manage…",
                "View and remove the SQL passwords AKML has stored (DPAPI-encrypted, per server + login).");
            ctx.RegisterSearch("Saved SQL passwords", "View and remove stored SQL passwords", "Button", rowManageCreds);
            btnManageCreds.Click += (_, _) =>
            {
                try { new Editor.SqlCredentialManagerDialog().ShowDialog(); }
                catch { /* opening the manager is non-critical */ }
            };

            return new IntelliSenseControls(chkEnabled, chkAutoTrig, chkAfterDot, chkFuzzy,
                sldMaxSugg, lblMaxSugg, sldTrigDelay, lblTrigDelay, cboCase,
                chkDataTypes, chkNullable, chkPkFk,
                chkJoin, chkAlias, chkDisableNative, chkSqlCreds,
                chkAutoClose, chkAddParens, chkSpaceCommit, chkDotCommit, chkSnippets);
        }
    }

    internal sealed class IntelliSenseControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _autoTrigger;
        private readonly CheckBox _afterDot;
        private readonly CheckBox _fuzzyMatch;
        private readonly Slider _maxSuggestions;
        private readonly TextBlock _maxSuggestionsLabel;
        private readonly Slider _triggerDelay;
        private readonly TextBlock _triggerDelayLabel;
        private readonly ComboBox _keywordCase;
        private readonly CheckBox _showDataTypes;
        private readonly CheckBox _showNullability;
        private readonly CheckBox _showPkFk;
        private readonly CheckBox _joinAssist;
        private readonly CheckBox _autoAlias;
        private readonly CheckBox _disableNativeIs;
        private readonly CheckBox _enableSqlAuthCreds;
        private readonly CheckBox _autoCloseChars;
        private readonly CheckBox _addParentheses;
        private readonly CheckBox _spaceCommits;
        private readonly CheckBox _dotCommits;
        private readonly CheckBox _snippetsInCompletion;

        public IntelliSenseControls(CheckBox enabled, CheckBox autoTrig, CheckBox afterDot, CheckBox fuzzy,
            Slider sldMaxSugg, TextBlock lblMaxSugg, Slider sldTrigDelay, TextBlock lblTrigDelay, ComboBox cboCase,
            CheckBox dataTypes, CheckBox nullable, CheckBox pkFk,
            CheckBox join, CheckBox alias, CheckBox disableNative, CheckBox sqlCreds,
            CheckBox autoCloseChars, CheckBox addParentheses, CheckBox spaceCommits, CheckBox dotCommits, CheckBox snippetsInCompletion)
        {
            _enabled = enabled;
            _autoTrigger = autoTrig;
            _afterDot = afterDot;
            _fuzzyMatch = fuzzy;
            _maxSuggestions = sldMaxSugg;
            _maxSuggestionsLabel = lblMaxSugg;
            _triggerDelay = sldTrigDelay;
            _triggerDelayLabel = lblTrigDelay;
            _keywordCase = cboCase;
            _showDataTypes = dataTypes;
            _showNullability = nullable;
            _showPkFk = pkFk;
            _joinAssist = join;
            _autoAlias = alias;
            _disableNativeIs = disableNative;
            _enableSqlAuthCreds = sqlCreds;
            _autoCloseChars = autoCloseChars;
            _addParentheses = addParentheses;
            _spaceCommits = spaceCommits;
            _dotCommits = dotCommits;
            _snippetsInCompletion = snippetsInCompletion;
        }

        public void Load(AppSettings settings)
        {
            var i = settings.IntelliSense;
            _enabled.IsChecked = i.Enabled;
            _autoTrigger.IsChecked = i.AutoTrigger;
            _afterDot.IsChecked = i.AfterDot;
            _fuzzyMatch.IsChecked = i.FuzzyMatch;
            _showDataTypes.IsChecked = i.ShowDataTypes;
            _showNullability.IsChecked = i.ShowNullability;
            _showPkFk.IsChecked = i.ShowPkFk;
            _autoAlias.IsChecked = i.AutoAlias;
            _joinAssist.IsChecked = i.JoinAssist;
            _disableNativeIs.IsChecked = i.DisableNativeIntelliSense;
            _enableSqlAuthCreds.IsChecked = i.EnableSqlAuthCredentials;
            _autoCloseChars.IsChecked = i.SpecialCharOptions.AutoCloseCharacters;
            _addParentheses.IsChecked = i.SpecialCharOptions.AddParentheses;
            _spaceCommits.IsChecked = i.SpaceCommits;
            _dotCommits.IsChecked = i.DotCommits;
            _snippetsInCompletion.IsChecked = i.SnippetsInCompletion;
            _triggerDelay.Value = i.TriggerDelayMs;
            _triggerDelayLabel.Text = i.TriggerDelayMs.ToString(CultureInfo.InvariantCulture);
            _maxSuggestions.Value = i.MaxSuggestions;
            _maxSuggestionsLabel.Text = i.MaxSuggestions.ToString(CultureInfo.InvariantCulture);
            _keywordCase.SelectedIndex = (int)i.KeywordCase;
        }

        public void Save(AppSettings settings)
        {
            settings.IntelliSense.Enabled = _enabled.IsChecked == true;
            settings.IntelliSense.AutoTrigger = _autoTrigger.IsChecked == true;
            settings.IntelliSense.AfterDot = _afterDot.IsChecked == true;
            settings.IntelliSense.FuzzyMatch = _fuzzyMatch.IsChecked == true;
            settings.IntelliSense.ShowDataTypes = _showDataTypes.IsChecked == true;
            settings.IntelliSense.ShowNullability = _showNullability.IsChecked == true;
            settings.IntelliSense.ShowPkFk = _showPkFk.IsChecked == true;
            settings.IntelliSense.AutoAlias = _autoAlias.IsChecked == true;
            settings.IntelliSense.JoinAssist = _joinAssist.IsChecked == true;
            settings.IntelliSense.DisableNativeIntelliSense = _disableNativeIs.IsChecked == true;
            settings.IntelliSense.EnableSqlAuthCredentials = _enableSqlAuthCreds.IsChecked == true;
            settings.IntelliSense.SpecialCharOptions.AutoCloseCharacters = _autoCloseChars.IsChecked == true;
            settings.IntelliSense.SpecialCharOptions.AddParentheses = _addParentheses.IsChecked == true;
            settings.IntelliSense.SpaceCommits = _spaceCommits.IsChecked == true;
            settings.IntelliSense.DotCommits = _dotCommits.IsChecked == true;
            settings.IntelliSense.SnippetsInCompletion = _snippetsInCompletion.IsChecked == true;
            settings.IntelliSense.TriggerDelayMs = (int)_triggerDelay.Value;
            settings.IntelliSense.MaxSuggestions = (int)_maxSuggestions.Value;
            settings.IntelliSense.KeywordCase = (KeywordCaseOption)_keywordCase.SelectedIndex;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
