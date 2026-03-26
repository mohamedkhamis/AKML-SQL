using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// ViewModel for the Profile Editor dialog. Holds the current profile state,
    /// manages undo/redo, and drives live preview via engine IPC.
    /// Targets .NET Framework 4.7.2 (Shell.Shared).
    /// </summary>
    internal sealed class ProfileEditorViewModel : INotifyPropertyChanged
    {
        // -------------------------------------------------------------------
        // Option category definitions
        // -------------------------------------------------------------------
        public static readonly string[] CategoryNames =
        [
            "Whitespace",
            "Casing",
            "Lists",
            "Parentheses",
            "DML (SELECT/INSERT/UPDATE/DELETE)",
            "JOINs",
            "DDL (CREATE/ALTER)",
            "Control Flow (IF/WHILE/TRY)",
            "CASE Expressions",
            "CTEs (WITH)",
            "Expressions",
            "Format Actions"
        ];

        // -------------------------------------------------------------------
        // Profile state — stored as a flat dictionary of path → value
        // -------------------------------------------------------------------
        private readonly Dictionary<string, object> _options = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _defaults = new(StringComparer.OrdinalIgnoreCase);

        // Undo/redo
        private readonly Stack<UndoEntry> _undoStack = new();
        private readonly Stack<UndoEntry> _redoStack = new();

        private string _profileName = "Untitled";
        private string _profileDescription = "";
        private string _profileAuthor = "";
        private string _selectedCategory = "Whitespace";
        private string _searchText = "";
        private string _formattedPreview = "";
        private bool _isDirty;

        public event PropertyChangedEventHandler PropertyChanged;

        public ProfileEditorViewModel()
        {
            InitializeDefaults();
            ResetToDefaults();
        }

        public ProfileEditorViewModel(string profileJson) : this()
        {
            LoadFromJson(profileJson);
        }

        // -------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------

        public string ProfileName
        {
            get => _profileName;
            set { SetField(ref _profileName, value); MarkDirty(); }
        }

        public string ProfileDescription
        {
            get => _profileDescription;
            set { SetField(ref _profileDescription, value); MarkDirty(); }
        }

        public string ProfileAuthor
        {
            get => _profileAuthor;
            set { SetField(ref _profileAuthor, value); MarkDirty(); }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set { SetField(ref _selectedCategory, value); OnPropertyChanged(nameof(VisibleOptions)); }
        }

        public string SearchText
        {
            get => _searchText;
            set { SetField(ref _searchText, value); OnPropertyChanged(nameof(VisibleOptions)); }
        }

        public bool IsDirty
        {
            get => _isDirty;
            private set => SetField(ref _isDirty, value);
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public string FormattedPreview
        {
            get => _formattedPreview;
            private set => SetField(ref _formattedPreview, value);
        }

        public string SampleSql =>
@"SELECT e.EmployeeID, e.FirstName, e.LastName,
d.DepartmentName, e.Salary, e.HireDate
FROM dbo.Employees AS e
INNER JOIN dbo.Departments AS d ON e.DepartmentID = d.DepartmentID
LEFT JOIN dbo.Locations AS l ON d.LocationID = l.LocationID
WHERE e.Salary > 50000
AND e.HireDate >= '2020-01-01'
AND d.DepartmentName IN ('Engineering', 'Sales', 'Marketing')
ORDER BY e.LastName, e.FirstName;

-- CTE example
WITH TopEarners AS (
    SELECT EmployeeID, FirstName, LastName, Salary,
        ROW_NUMBER() OVER (PARTITION BY DepartmentID ORDER BY Salary DESC) AS rn
    FROM dbo.Employees
    WHERE Salary > 75000
)
SELECT * FROM TopEarners WHERE rn <= 3;

-- CASE expression
SELECT ProductName,
    CASE
        WHEN UnitPrice > 100 THEN 'Premium'
        WHEN UnitPrice > 50 THEN 'Standard'
        ELSE 'Budget'
    END AS PriceCategory
FROM dbo.Products
ORDER BY UnitPrice DESC;";

        // -------------------------------------------------------------------
        // Option descriptors
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns the list of option descriptors for the currently selected category,
        /// optionally filtered by <see cref="SearchText"/>.
        /// </summary>
        public List<OptionDescriptor> VisibleOptions
        {
            get
            {
                var allOptions = GetOptionsForCategory(_selectedCategory);
                if (string.IsNullOrWhiteSpace(_searchText))
                    return allOptions;

                var filtered = new List<OptionDescriptor>();
                foreach (var opt in allOptions)
                {
                    if (opt.DisplayName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0
                        || opt.Key.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        filtered.Add(opt);
                    }
                }
                return filtered;
            }
        }

        /// <summary>
        /// Returns all option descriptors across all categories that match the search text.
        /// </summary>
        public List<OptionDescriptor> SearchAllOptions(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return [];

            var results = new List<OptionDescriptor>();
            foreach (var cat in CategoryNames)
            {
                foreach (var opt in GetOptionsForCategory(cat))
                {
                    if (opt.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                        || opt.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(opt);
                    }
                }
            }
            return results;
        }

        // -------------------------------------------------------------------
        // Get / Set option values
        // -------------------------------------------------------------------

        public object GetOption(string key)
        {
            if (_options.TryGetValue(key, out var val))
                return val;
            if (_defaults.TryGetValue(key, out var def))
                return def;
            return null;
        }

        public void SetOption(string key, object value, bool recordUndo = true)
        {
            var oldValue = GetOption(key);
            if (Equals(oldValue, value))
                return;

            if (recordUndo)
            {
                _undoStack.Push(new UndoEntry(key, oldValue, value));
                _redoStack.Clear();
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
            }

            _options[key] = value;
            MarkDirty();
            OnPropertyChanged(nameof(VisibleOptions));
            RefreshPreview();
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;

            var entry = _undoStack.Pop();
            _redoStack.Push(entry);
            _options[entry.Key] = entry.OldValue;
            MarkDirty();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(VisibleOptions));
            RefreshPreview();
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;

            var entry = _redoStack.Pop();
            _undoStack.Push(entry);
            _options[entry.Key] = entry.NewValue;
            MarkDirty();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(VisibleOptions));
            RefreshPreview();
        }

        // -------------------------------------------------------------------
        // Reset
        // -------------------------------------------------------------------

        public void ResetToDefaults()
        {
            _options.Clear();
            foreach (var kvp in _defaults)
            {
                _options[kvp.Key] = kvp.Value;
            }
            _undoStack.Clear();
            _redoStack.Clear();
            MarkDirty();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(VisibleOptions));
            RefreshPreview();
        }

        public void ResetCategory(string category)
        {
            var opts = GetOptionsForCategory(category);
            foreach (var opt in opts)
            {
                if (_defaults.TryGetValue(opt.Key, out var def))
                {
                    SetOption(opt.Key, def, recordUndo: true);
                }
            }
        }

        // -------------------------------------------------------------------
        // Save / Load
        // -------------------------------------------------------------------

        public void Save()
        {
            try
            {
                var json = BuildProfileJson();
                var dir = Path.Combine(Constants.AppDataPath, "profiles");
                Directory.CreateDirectory(dir);

                var fileName = SanitizeFileName(_profileName) + ".akmlstyle";
                var filePath = Path.Combine(dir, fileName);
                var tempPath = filePath + ".tmp";

                File.WriteAllText(tempPath, json, Encoding.UTF8);

                if (File.Exists(filePath))
                    File.Delete(filePath);
                File.Move(tempPath, filePath);

                IsDirty = false;
                Log.Information("Profile saved: {ProfileName} -> {FilePath}", _profileName, filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save profile {ProfileName}", _profileName);
                throw;
            }
        }

        public void SaveAndApply()
        {
            Save();

            // Notify the engine to reload the profile
            try
            {
                var manager = EngineLifecycle.Manager;
                var client = manager?.Client;
                if (client is { IsConnected: true })
                {
                    var json = BuildProfileJson();
                    var jsonBytes = Encoding.UTF8.GetBytes(json);
                    var request = new FormatRequest
                    {
                        SessionId = Guid.NewGuid().ToString("N"),
                        Text = "",
                        ProfileName = _profileName,
                        ProfileOverrides = jsonBytes
                    };
                    // Fire and forget — just push the profile to the engine
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await client.SendRequestAsync<FormatResponse, FormatRequest>(
                                MessageTypes.ProfileSave, request, timeoutMs: 5000);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to push profile to engine via IPC");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply profile to engine");
            }
        }

        public void LoadFromJson(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    // Metadata
                    if (root.TryGetProperty("metadata", out var meta))
                    {
                        if (meta.TryGetProperty("name", out var name))
                            _profileName = name.GetString() ?? "Untitled";
                        if (meta.TryGetProperty("description", out var desc))
                            _profileDescription = desc.GetString() ?? "";
                        if (meta.TryGetProperty("author", out var author))
                            _profileAuthor = author.GetString() ?? "";
                    }

                    // Categories
                    LoadCategoryFromJson(root, "whitespace");
                    LoadCategoryFromJson(root, "casing");
                    LoadCategoryFromJson(root, "list");
                    LoadCategoryFromJson(root, "parenthesis");
                    LoadCategoryFromJson(root, "dml");
                    LoadCategoryFromJson(root, "join");
                    LoadCategoryFromJson(root, "ddl");
                    LoadCategoryFromJson(root, "controlFlow");
                    LoadCategoryFromJson(root, "case");
                    LoadCategoryFromJson(root, "cte");
                    LoadCategoryFromJson(root, "expression");
                    LoadCategoryFromJson(root, "formatActions");
                }

                IsDirty = false;
                _undoStack.Clear();
                _redoStack.Clear();
                OnPropertyChanged(nameof(ProfileName));
                OnPropertyChanged(nameof(ProfileDescription));
                OnPropertyChanged(nameof(ProfileAuthor));
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
                OnPropertyChanged(nameof(VisibleOptions));
                RefreshPreview();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load profile from JSON");
            }
        }

        public string BuildProfileJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            // Metadata
            sb.AppendLine("  \"metadata\": {");
            sb.AppendLine("    \"schemaVersion\": 1,");
            sb.AppendLine("    \"name\": " + JsonEncode(_profileName) + ",");
            sb.AppendLine("    \"description\": " + JsonEncode(_profileDescription) + ",");
            sb.AppendLine("    \"author\": " + JsonEncode(_profileAuthor) + ",");
            sb.AppendLine("    \"version\": \"1.0.0\",");
            sb.AppendLine("    \"modified\": " + JsonEncode(DateTime.UtcNow.ToString("o")));
            sb.AppendLine("  },");

            // Write each category
            WriteCategory(sb, "whitespace", "whitespace.");
            sb.AppendLine(",");
            WriteCategory(sb, "casing", "casing.");
            sb.AppendLine(",");
            WriteCategory(sb, "list", "list.");
            sb.AppendLine(",");
            WriteCategory(sb, "parenthesis", "parenthesis.");
            sb.AppendLine(",");
            WriteCategory(sb, "dml", "dml.");
            sb.AppendLine(",");
            WriteCategory(sb, "join", "join.");
            sb.AppendLine(",");
            WriteCategory(sb, "ddl", "ddl.");
            sb.AppendLine(",");
            WriteCategory(sb, "controlFlow", "controlFlow.");
            sb.AppendLine(",");
            WriteCategory(sb, "case", "case.");
            sb.AppendLine(",");
            WriteCategory(sb, "cte", "cte.");
            sb.AppendLine(",");
            WriteCategory(sb, "expression", "expression.");
            sb.AppendLine(",");
            WriteCategory(sb, "formatActions", "formatActions.");
            sb.AppendLine();

            sb.AppendLine("}");
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        // Live preview
        // -------------------------------------------------------------------

        public void RefreshPreview()
        {
            try
            {
                var manager = EngineLifecycle.Manager;
                var client = manager?.Client;

                if (client is { IsConnected: true })
                {
                    var json = BuildProfileJson();
                    var jsonBytes = Encoding.UTF8.GetBytes(json);
                    var request = new FormatRequest
                    {
                        SessionId = Guid.NewGuid().ToString("N"),
                        Text = SampleSql,
                        ProfileOverrides = jsonBytes
                    };

                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            var response = await client.SendRequestAsync<FormatResponse, FormatRequest>(
                                MessageTypes.FormatPreview, request, timeoutMs: 5000);

                            if (response.Success)
                            {
                                FormattedPreview = response.FormattedText;
                            }
                            else
                            {
                                FormattedPreview = "// Preview unavailable: formatting failed";
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Preview IPC failed");
                            FormattedPreview = "// Preview unavailable (engine not connected)";
                        }
                    });
                }
                else
                {
                    FormattedPreview = "// Preview unavailable (engine not connected).\n// Save the profile and format a document to see results.";
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "RefreshPreview failed");
                FormattedPreview = "// Preview error: " + ex.Message;
            }
        }

        // -------------------------------------------------------------------
        // Default option values
        // -------------------------------------------------------------------

        private void InitializeDefaults()
        {
            // Whitespace
            D("whitespace.tabStyle", "spaces");
            D("whitespace.tabSize", 4);
            D("whitespace.indentStyle", "block");
            D("whitespace.maxLineWidth", 120);
            D("whitespace.lineBreakBeforeClause", true);
            D("whitespace.lineBreakAfterClause", false);
            D("whitespace.lineBreakBeforeComma", false);
            D("whitespace.lineBreakAfterComma", true);
            D("whitespace.emptyLineBetweenStatements", 1);
            D("whitespace.emptyLineBeforeGO", true);
            D("whitespace.emptyLineAfterGO", true);
            D("whitespace.preserveEmptyLines", true);
            D("whitespace.maxConsecutiveEmptyLines", 2);
            D("whitespace.trailingWhitespace", "remove");
            D("whitespace.finalNewline", "ensure");
            D("whitespace.spaceAfterComma", true);
            D("whitespace.spaceAroundOperators", true);
            D("whitespace.spaceAroundBooleanOperators", true);
            D("whitespace.spaceInsideParentheses", false);
            D("whitespace.spaceBeforeParentheses", false);
            D("whitespace.lineBreakAfterSemicolon", true);

            // Casing
            D("casing.reservedKeywords", "UPPERCASE");
            D("casing.builtInFunctions", "UPPERCASE");
            D("casing.builtInDataTypes", "lowercase");
            D("casing.systemObjects", "lowercase");
            D("casing.globalVariables", "lowercase");
            D("casing.localVariables", "AsIs");
            D("casing.identifiers", "AsIs");
            D("casing.syncWithDatabase", false);
            D("casing.camelCaseDictionary", true);
            D("casing.applyOnTyping", true);

            // List
            D("list.commaPosition", "trailing");
            D("list.alignItemsAcrossClauses", true);
            D("list.alignAliases", true);
            D("list.oneItemPerLine", true);
            D("list.collapseShortLists", true);
            D("list.collapseThreshold", 60);
            D("list.indentListItems", true);
            D("list.alignDataTypesInDDL", true);
            D("list.alignValuesInInsert", true);
            D("list.spaceAfterListComma", true);

            // Parenthesis
            D("parenthesis.openOnSameLine", true);
            D("parenthesis.closeOnNewLine", "false");
            D("parenthesis.collapseShort", true);
            D("parenthesis.collapseThreshold", 40);
            D("parenthesis.indentContents", true);
            D("parenthesis.spaceInside", false);
            D("parenthesis.removeRedundant", false);
            D("parenthesis.createTableColumns", "newLine");
            D("parenthesis.procedureParameters", "newLine");
            D("parenthesis.subqueryStyle", "indent");

            // DML
            D("dml.selectItemsOnNewLine", true);
            D("dml.selectStarOnSameLine", true);
            D("dml.fromOnNewLine", true);
            D("dml.whereOnNewLine", true);
            D("dml.andOrNewLine", "before");
            D("dml.andOrIndent", "alignWithWhere");
            D("dml.groupByOnNewLine", true);
            D("dml.havingOnNewLine", true);
            D("dml.orderByOnNewLine", true);
            D("dml.topOnSameLine", true);
            D("dml.distinctOnSameLine", true);
            D("dml.intoOnNewLine", true);
            D("dml.valuesOnNewLine", true);
            D("dml.setOnNewLine", true);
            D("dml.deleteFromOnSameLine", true);
            D("dml.mergeWhenOnNewLine", true);
            D("dml.collapseShortStatements", true);
            D("dml.collapseThreshold", 80);
            D("dml.collapseShortSubqueries", true);
            D("dml.subqueryCollapseThreshold", 60);

            // Join
            D("join.onNewLine", true);
            D("join.indentJoin", false);
            D("join.onConditionNewLine", true);
            D("join.onConditionIndent", "indent");
            D("join.multipleOnConditions", "newLine");
            D("join.emptyLineBeforeJoin", false);
            D("join.alignJoinKeyword", "right");
            D("join.joinTypeStyle", "explicit");
            D("join.crossApplyNewLine", true);

            // DDL
            D("ddl.createTableColumnsOnNewLine", true);
            D("ddl.alignDataTypes", true);
            D("ddl.alignConstraints", true);
            D("ddl.constraintsOnNewLine", false);
            D("ddl.inlineConstraintStyle", "sameLine");
            D("ddl.tableConstraintsSeparate", true);
            D("ddl.firstParameterOnNewLine", "auto");
            D("ddl.parameterAlignment", "aligned");
            D("ddl.alignParameterDataTypes", true);
            D("ddl.alignParameterDefaults", true);
            D("ddl.asOnNewLine", true);
            D("ddl.beginOnNewLine", true);
            D("ddl.collapseShortDDL", true);
            D("ddl.collapseThreshold", 60);

            // ControlFlow
            D("controlFlow.beginOnNewLine", true);
            D("controlFlow.endOnNewLine", true);
            D("controlFlow.indentBetweenBeginEnd", true);
            D("controlFlow.collapseShortIfElse", true);
            D("controlFlow.collapseThreshold", 60);
            D("controlFlow.elseOnNewLine", true);
            D("controlFlow.elseAlignWithIf", true);
            D("controlFlow.tryCatchOnNewLine", true);

            // Case
            D("case.whenOnNewLine", true);
            D("case.thenOnNewLine", false);
            D("case.elseOnNewLine", true);
            D("case.endOnNewLine", true);
            D("case.indentWhen", true);
            D("case.alignThen", true);
            D("case.collapseShortCase", true);
            D("case.collapseThreshold", 60);

            // CTE
            D("cte.withOnNewLine", true);
            D("cte.cteBodyIndent", true);
            D("cte.commaBeforeCte", false);
            D("cte.emptyLineBetweenCtes", true);

            // Expression
            D("expression.booleanOperatorNewLine", "before");
            D("expression.betweenOnOneLine", true);
            D("expression.inListStyle", "multiLine");
            D("expression.inListThreshold", 60);
            D("expression.existsSubqueryIndent", "indent");

            // Format Actions
            D("formatActions.applyLayout", true);
            D("formatActions.applyCasing", true);
            D("formatActions.insertSemicolons", false);
            D("formatActions.removeSemicolons", false);
            D("formatActions.expandWildcards", false);
            D("formatActions.qualifyObjectNames", false);
            D("formatActions.addAsKeyword", true);
            D("formatActions.addSquareBrackets", false);
        }

        private void D(string key, object value)
        {
            _defaults[key] = value;
        }

        // -------------------------------------------------------------------
        // Option descriptors per category
        // -------------------------------------------------------------------

        public List<OptionDescriptor> GetOptionsForCategory(string category)
        {
            switch (category)
            {
                case "Whitespace":
                    return
                    [
                        Combo("whitespace.tabStyle", "Tab Style", ["spaces", "tabs"]),
                        Numeric("whitespace.tabSize", "Tab Size", 1, 16),
                        Combo("whitespace.indentStyle", "Indent Style", ["block", "flat"]),
                        Numeric("whitespace.maxLineWidth", "Max Line Width", 40, 300),
                        Bool("whitespace.lineBreakBeforeClause", "Line Break Before Clause"),
                        Bool("whitespace.lineBreakAfterClause", "Line Break After Clause"),
                        Bool("whitespace.lineBreakBeforeComma", "Line Break Before Comma"),
                        Bool("whitespace.lineBreakAfterComma", "Line Break After Comma"),
                        Numeric("whitespace.emptyLineBetweenStatements", "Empty Lines Between Statements", 0, 5),
                        Bool("whitespace.emptyLineBeforeGO", "Empty Line Before GO"),
                        Bool("whitespace.emptyLineAfterGO", "Empty Line After GO"),
                        Bool("whitespace.preserveEmptyLines", "Preserve Empty Lines"),
                        Numeric("whitespace.maxConsecutiveEmptyLines", "Max Consecutive Empty Lines", 0, 10),
                        Combo("whitespace.trailingWhitespace", "Trailing Whitespace", ["remove", "preserve"]),
                        Combo("whitespace.finalNewline", "Final Newline", ["ensure", "remove", "preserve"]),
                        Bool("whitespace.spaceAfterComma", "Space After Comma"),
                        Bool("whitespace.spaceAroundOperators", "Space Around Operators"),
                        Bool("whitespace.spaceAroundBooleanOperators", "Space Around Boolean Operators"),
                        Bool("whitespace.spaceInsideParentheses", "Space Inside Parentheses"),
                        Bool("whitespace.spaceBeforeParentheses", "Space Before Parentheses"),
                        Bool("whitespace.lineBreakAfterSemicolon", "Line Break After Semicolon")
                    ];
                case "Casing":
                    return
                    [
                        Combo("casing.reservedKeywords", "Reserved Keywords",
                            ["UPPERCASE", "lowercase", "PascalCase", "AsIs"]),
                        Combo("casing.builtInFunctions", "Built-in Functions",
                            ["UPPERCASE", "lowercase", "PascalCase", "AsIs"]),
                        Combo("casing.builtInDataTypes", "Built-in Data Types",
                            ["UPPERCASE", "lowercase", "PascalCase", "AsIs"]),
                        Combo("casing.systemObjects", "System Objects",
                            ["UPPERCASE", "lowercase", "PascalCase", "AsIs"]),
                        Combo("casing.globalVariables", "Global Variables",
                            ["UPPERCASE", "lowercase", "PascalCase", "AsIs"]),
                        Combo("casing.localVariables", "Local Variables",
                            ["UPPERCASE", "lowercase", "PascalCase", "AsIs"]),
                        Combo("casing.identifiers", "Identifiers",
                            ["UPPERCASE", "lowercase", "PascalCase", "AsIs"]),
                        Bool("casing.syncWithDatabase", "Sync With Database"),
                        Bool("casing.camelCaseDictionary", "CamelCase Dictionary"),
                        Bool("casing.applyOnTyping", "Apply On Typing")
                    ];
                case "Lists":
                    return
                    [
                        Combo("list.commaPosition", "Comma Position", ["trailing", "leading"]),
                        Bool("list.alignItemsAcrossClauses", "Align Items Across Clauses"),
                        Bool("list.alignAliases", "Align Aliases"),
                        Bool("list.oneItemPerLine", "One Item Per Line"),
                        Bool("list.collapseShortLists", "Collapse Short Lists"),
                        Numeric("list.collapseThreshold", "Collapse Threshold", 20, 200),
                        Bool("list.indentListItems", "Indent List Items"),
                        Bool("list.alignDataTypesInDDL", "Align Data Types in DDL"),
                        Bool("list.alignValuesInInsert", "Align Values in INSERT"),
                        Bool("list.spaceAfterListComma", "Space After List Comma")
                    ];
                case "Parentheses":
                    return
                    [
                        Bool("parenthesis.openOnSameLine", "Open on Same Line"),
                        Combo("parenthesis.closeOnNewLine", "Close on New Line", ["true", "false", "auto"]),
                        Bool("parenthesis.collapseShort", "Collapse Short"),
                        Numeric("parenthesis.collapseThreshold", "Collapse Threshold", 10, 200),
                        Bool("parenthesis.indentContents", "Indent Contents"),
                        Bool("parenthesis.spaceInside", "Space Inside"),
                        Bool("parenthesis.removeRedundant", "Remove Redundant"),
                        Combo("parenthesis.createTableColumns", "CREATE TABLE Columns",
                            ["newLine", "sameLine"]),
                        Combo("parenthesis.procedureParameters", "Procedure Parameters",
                            ["newLine", "sameLine"]),
                        Combo("parenthesis.subqueryStyle", "Subquery Style", ["indent", "aligned"])
                    ];
                case "DML (SELECT/INSERT/UPDATE/DELETE)":
                    return
                    [
                        Bool("dml.selectItemsOnNewLine", "SELECT Items on New Line"),
                        Bool("dml.selectStarOnSameLine", "SELECT * on Same Line"),
                        Bool("dml.fromOnNewLine", "FROM on New Line"),
                        Bool("dml.whereOnNewLine", "WHERE on New Line"),
                        Combo("dml.andOrNewLine", "AND/OR New Line", ["before", "after", "none"]),
                        Combo("dml.andOrIndent", "AND/OR Indent", ["alignWithWhere", "indent", "noIndent"]),
                        Bool("dml.groupByOnNewLine", "GROUP BY on New Line"),
                        Bool("dml.havingOnNewLine", "HAVING on New Line"),
                        Bool("dml.orderByOnNewLine", "ORDER BY on New Line"),
                        Bool("dml.topOnSameLine", "TOP on Same Line"),
                        Bool("dml.distinctOnSameLine", "DISTINCT on Same Line"),
                        Bool("dml.intoOnNewLine", "INTO on New Line"),
                        Bool("dml.valuesOnNewLine", "VALUES on New Line"),
                        Bool("dml.setOnNewLine", "SET on New Line"),
                        Bool("dml.deleteFromOnSameLine", "DELETE FROM on Same Line"),
                        Bool("dml.mergeWhenOnNewLine", "MERGE WHEN on New Line"),
                        Bool("dml.collapseShortStatements", "Collapse Short Statements"),
                        Numeric("dml.collapseThreshold", "Collapse Threshold", 20, 200),
                        Bool("dml.collapseShortSubqueries", "Collapse Short Subqueries"),
                        Numeric("dml.subqueryCollapseThreshold", "Subquery Collapse Threshold", 20, 200)
                    ];
                case "JOINs":
                    return
                    [
                        Bool("join.onNewLine", "JOIN on New Line"),
                        Bool("join.indentJoin", "Indent JOIN"),
                        Bool("join.onConditionNewLine", "ON Condition on New Line"),
                        Combo("join.onConditionIndent", "ON Condition Indent",
                            ["indent", "aligned", "noIndent"]),
                        Combo("join.multipleOnConditions", "Multiple ON Conditions", ["newLine", "sameLine"]),
                        Bool("join.emptyLineBeforeJoin", "Empty Line Before JOIN"),
                        Combo("join.alignJoinKeyword", "Align JOIN Keyword", ["right", "left", "none"]),
                        Combo("join.joinTypeStyle", "JOIN Type Style", ["explicit", "implicit"]),
                        Bool("join.crossApplyNewLine", "CROSS APPLY on New Line")
                    ];
                case "DDL (CREATE/ALTER)":
                    return
                    [
                        Bool("ddl.createTableColumnsOnNewLine", "CREATE TABLE Columns on New Line"),
                        Bool("ddl.alignDataTypes", "Align Data Types"),
                        Bool("ddl.alignConstraints", "Align Constraints"),
                        Bool("ddl.constraintsOnNewLine", "Constraints on New Line"),
                        Combo("ddl.inlineConstraintStyle", "Inline Constraint Style", ["sameLine", "newLine"]),
                        Bool("ddl.tableConstraintsSeparate", "Table Constraints Separate"),
                        Combo("ddl.firstParameterOnNewLine", "First Parameter on New Line",
                            ["auto", "always", "never"]),
                        Combo("ddl.parameterAlignment", "Parameter Alignment", ["aligned", "indented", "none"]),
                        Bool("ddl.alignParameterDataTypes", "Align Parameter Data Types"),
                        Bool("ddl.alignParameterDefaults", "Align Parameter Defaults"),
                        Bool("ddl.asOnNewLine", "AS on New Line"),
                        Bool("ddl.beginOnNewLine", "BEGIN on New Line"),
                        Bool("ddl.collapseShortDDL", "Collapse Short DDL"),
                        Numeric("ddl.collapseThreshold", "Collapse Threshold", 20, 200)
                    ];
                case "Control Flow (IF/WHILE/TRY)":
                    return
                    [
                        Bool("controlFlow.beginOnNewLine", "BEGIN on New Line"),
                        Bool("controlFlow.endOnNewLine", "END on New Line"),
                        Bool("controlFlow.indentBetweenBeginEnd", "Indent Between BEGIN/END"),
                        Bool("controlFlow.collapseShortIfElse", "Collapse Short IF/ELSE"),
                        Numeric("controlFlow.collapseThreshold", "Collapse Threshold", 20, 200),
                        Bool("controlFlow.elseOnNewLine", "ELSE on New Line"),
                        Bool("controlFlow.elseAlignWithIf", "ELSE Align With IF"),
                        Bool("controlFlow.tryCatchOnNewLine", "TRY/CATCH on New Line")
                    ];
                case "CASE Expressions":
                    return
                    [
                        Bool("case.whenOnNewLine", "WHEN on New Line"),
                        Bool("case.thenOnNewLine", "THEN on New Line"),
                        Bool("case.elseOnNewLine", "ELSE on New Line"),
                        Bool("case.endOnNewLine", "END on New Line"),
                        Bool("case.indentWhen", "Indent WHEN"),
                        Bool("case.alignThen", "Align THEN"),
                        Bool("case.collapseShortCase", "Collapse Short CASE"),
                        Numeric("case.collapseThreshold", "Collapse Threshold", 20, 200)
                    ];
                case "CTEs (WITH)":
                    return
                    [
                        Bool("cte.withOnNewLine", "WITH on New Line"),
                        Bool("cte.cteBodyIndent", "CTE Body Indent"),
                        Bool("cte.commaBeforeCte", "Comma Before CTE"),
                        Bool("cte.emptyLineBetweenCtes", "Empty Line Between CTEs")
                    ];
                case "Expressions":
                    return
                    [
                        Combo("expression.booleanOperatorNewLine", "Boolean Operator New Line",
                            ["before", "after", "none"]),
                        Bool("expression.betweenOnOneLine", "BETWEEN on One Line"),
                        Combo("expression.inListStyle", "IN List Style", ["multiLine", "singleLine", "auto"]),
                        Numeric("expression.inListThreshold", "IN List Threshold", 20, 200),
                        Combo("expression.existsSubqueryIndent", "EXISTS Subquery Indent",
                            ["indent", "aligned"])
                    ];
                case "Format Actions":
                    return
                    [
                        Bool("formatActions.applyLayout", "Apply Layout"),
                        Bool("formatActions.applyCasing", "Apply Casing"),
                        Bool("formatActions.insertSemicolons", "Insert Semicolons"),
                        Bool("formatActions.removeSemicolons", "Remove Semicolons"),
                        Bool("formatActions.expandWildcards", "Expand Wildcards"),
                        Bool("formatActions.qualifyObjectNames", "Qualify Object Names"),
                        Bool("formatActions.addAsKeyword", "Add AS Keyword"),
                        Bool("formatActions.addSquareBrackets", "Add Square Brackets")
                    ];
                default:
                    return [];
            }
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private void MarkDirty()
        {
            IsDirty = true;
        }

        private void LoadCategoryFromJson(JsonElement root, string categoryKey)
        {
            if (!root.TryGetProperty(categoryKey, out var catElement))
                return;

            foreach (var prop in catElement.EnumerateObject())
            {
                var key = categoryKey + "." + prop.Name;
                switch (prop.Value.ValueKind)
                {
                    case JsonValueKind.True:
                        _options[key] = true;
                        break;
                    case JsonValueKind.False:
                        _options[key] = false;
                        break;
                    case JsonValueKind.Number:
                        _options[key] = prop.Value.GetInt32();
                        break;
                    case JsonValueKind.String:
                        _options[key] = prop.Value.GetString();
                        break;
                }
            }
        }

        private void WriteCategory(StringBuilder sb, string jsonKey, string prefix)
        {
            sb.Append("  \"" + jsonKey + "\": {");
            bool first = true;
            foreach (var kvp in _options)
            {
                if (!kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!first) sb.Append(",");
                sb.AppendLine();

                var propName = kvp.Key.Substring(prefix.Length);
                sb.Append("    \"" + propName + "\": ");

                if (kvp.Value is bool bVal)
                    sb.Append(bVal ? "true" : "false");
                else if (kvp.Value is int iVal)
                    sb.Append(iVal.ToString());
                else
                    sb.Append(JsonEncode(kvp.Value?.ToString() ?? ""));

                first = false;
            }
            sb.AppendLine();
            sb.Append("  }");
        }

        private static string JsonEncode(string value)
        {
            if (value == null) return "null";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars).Trim();
        }

        private static OptionDescriptor Bool(string key, string displayName)
        {
            return new OptionDescriptor(key, displayName, OptionKind.Boolean, null, 0, 0);
        }

        private static OptionDescriptor Combo(string key, string displayName, string[] choices)
        {
            return new OptionDescriptor(key, displayName, OptionKind.Choice, choices, 0, 0);
        }

        private static OptionDescriptor Numeric(string key, string displayName, int min, int max)
        {
            return new OptionDescriptor(key, displayName, OptionKind.Numeric, null, min, max);
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // -------------------------------------------------------------------
        // Nested types
        // -------------------------------------------------------------------

        private sealed class UndoEntry
        {
            public string Key { get; }
            public object OldValue { get; }
            public object NewValue { get; }

            public UndoEntry(string key, object oldValue, object newValue)
            {
                Key = key;
                OldValue = oldValue;
                NewValue = newValue;
            }
        }
    }

    // -------------------------------------------------------------------
    // Option descriptor (shared by ViewModel and UI)
    // -------------------------------------------------------------------

    internal enum OptionKind
    {
        Boolean,
        Choice,
        Numeric
    }

    internal sealed class OptionDescriptor
    {
        public string Key { get; }
        public string DisplayName { get; }
        public OptionKind Kind { get; }
        public string[] Choices { get; }
        public int Min { get; }
        public int Max { get; }

        public string Category
        {
            get
            {
                var dot = Key.IndexOf('.');
                return dot >= 0 ? Key.Substring(0, dot) : "";
            }
        }

        public OptionDescriptor(string key, string displayName, OptionKind kind, string[] choices, int min, int max)
        {
            Key = key;
            DisplayName = displayName;
            Kind = kind;
            Choices = choices ?? [];
            Min = min;
            Max = max;
        }
    }
}
