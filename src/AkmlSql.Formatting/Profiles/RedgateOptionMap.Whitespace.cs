namespace AkmlSql.Formatting.Profiles;

internal static partial class RedgateOptionMap
{
    private static bool B(string v) => v.Equals("true", StringComparison.OrdinalIgnoreCase);
    private static int I(string v, int fallback) => int.TryParse(v, out var n) ? n : fallback;

    private static string Casing5(string v) => v.Trim().ToLowerInvariant() switch
    {
        "uppercase" => "UPPERCASE",
        "lowercase" => "lowercase",
        "uppercamelcase" => "PascalCase",
        "lowercamelcase" => "camelCase",
        _ => "AsIs", // leaveAsIs
    };

    private static void Add(string path, string defaultValue, Action<FormattingProfile, string> apply)
        => Entries[path] = new RedgateMappingEntry { DefaultValue = defaultValue, Apply = apply };

    private static void AddUnsupported(string path, string defaultValue, string reason)
        => Entries[path] = new RedgateMappingEntry { DefaultValue = defaultValue, UnsupportedReason = reason };

    static partial void RegisterWhitespaceListsParensCasing()
    {
        // ----- whitespace -----
        Add("whitespace.spacesOrTabs", "spaces", (p, v) => p.Whitespace.TabStyle = v.Trim().ToLowerInvariant() switch
        {
            "tabs" => "tabs",
            "tabsifpossible" => "tabsWhenPossible",
            _ => "spaces",
        });
        Add("whitespace.numberOfSpacesInTabs", "4", (p, v) => p.Whitespace.TabSize = I(v, 4));
        Add("whitespace.wrapLongLines", "true", (p, v) => p.Whitespace.WrapLongLines = B(v));
        Add("whitespace.wrapLinesLongerThan", "120", (p, v) => p.Whitespace.MaxLineWidth = I(v, 120));
        Add("whitespace.whiteSpaceBeforeSemiColon", "none", (p, v) => p.Whitespace.SemicolonPlacement = v.Trim().ToLowerInvariant() switch
        {
            "spacebefore" => "spaceBefore",
            "newlinebefore" => "newLineBefore",
            _ => "none",
        });
        Add("whitespace.newLines.preserveExistingEmptyLinesBetweenStatements", "true", (p, v) => p.Whitespace.PreserveEmptyLines = B(v));
        Add("whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator", "true", (p, v) => p.Whitespace.PreserveEmptyLinesAfterBatch = B(v));
        Add("whitespace.newLines.emptyLinesBetweenStatements", "1", (p, v) => p.Whitespace.EmptyLineBetweenStatements = I(v, 1));
        Add("whitespace.newLines.emptyLinesAfterBatchSeparator", "1", (p, v) => p.Whitespace.EmptyLinesAfterBatchSeparator = I(v, 1));
        // Post-schema documented addition (SP 10.14 release notes) — FR-001/FR-036:
        Add("whitespace.newLines.alignMultilineCommentsMatchingPatterns", "false", (p, v) =>
        {
            if (!B(v)) return;
            p.Comments.MultilineFormatting = "normaliseIndent";
            p.Comments.RecognizeCommonPatterns = true;
        });

        // ----- lists -----
        AddUnsupported("lists.placeFirstItemOnNewLine", "never",
            "AKML's list layout controls whether SUBSEQUENT items break (placeSubsequentItemsOnNewLines / oneItemPerLine); there is no independent control for whether the FIRST item in a list breaks off the clause keyword. Revisit if phase-3 list layout adds a distinct first-item control.");
        Add("lists.placeSubsequentItemsOnNewLines", "always", (p, v) => p.List.PlaceSubsequentItemsOnNewLines = NormalizePlacement(v));
        AddUnsupported("lists.alignSubsequentItemsWithFirstItem", "true",
            "AKML has no column-alignment control for continuation list items relative to the first item's column; IndentListItems only controls indent LEVEL, not alignment to a specific column.");
        Add("lists.alignItemsAcrossClauses", "true", (p, v) => p.List.AlignItemsAcrossClauses = B(v));
        Add("lists.indentListItems", "true", (p, v) => p.List.IndentListItems = B(v));
        Add("lists.alignItemsToTabStops", "false", (p, v) => p.List.AlignItemsToTabStops = B(v));
        Add("lists.alignAliases", "false", (p, v) => p.List.AlignAliases = B(v));
        AddUnsupported("lists.alignComments", "false",
            "AKML has no column-alignment model for trailing comments across list items; comments are preserved in place but never padded to a shared column.");
        Add("lists.placeCommasBeforeItems", "false", (p, v) => p.List.CommaPosition = B(v) ? "leading" : "trailing");
        Add("lists.addSpaceBeforeComma", "false", (p, v) => p.List.SpaceBeforeComma = B(v));
        Add("lists.addSpaceAfterComma", "true", (p, v) => { p.Whitespace.SpaceAfterComma = B(v); p.List.SpaceAfterListComma = B(v); });
        Add("lists.commaAlignment", "toList", (p, v) => p.List.CommaAlignment = v.Trim().ToLowerInvariant() switch
        {
            "beforeitem" => "beforeItem",
            "tostatement" => "toStatement",
            _ => "toList",
        });

        // ----- parentheses (global) -----
        Add("parentheses.parenthesisStyle", "compactSimple", (p, v) => p.Parenthesis.Style = NormalizeParenStyle(v));
        Add("parentheses.indentParenthesesContents", "false", (p, v) => p.Parenthesis.IndentContents = B(v));
        Add("parentheses.collapseShortParenthesisContents", "false", (p, v) => p.Parenthesis.CollapseShort = B(v));
        Add("parentheses.collapseParenthesesShorterThan", "80", (p, v) => p.Parenthesis.CollapseThreshold = I(v, 80));
        Add("parentheses.addSpacesInsideParentheses", "false", (p, v) => p.Parenthesis.SpaceInside = B(v));
        Add("parentheses.addSpacesAroundParentheses", "true", (p, v) => p.Whitespace.SpaceBeforeParentheses = B(v));

        // ----- casing -----
        Add("casing.reservedKeywords", "leaveAsIs", (p, v) => p.Casing.ReservedKeywords = Casing5(v));
        Add("casing.builtInFunctions", "leaveAsIs", (p, v) => p.Casing.BuiltInFunctions = Casing5(v));
        Add("casing.builtInDataTypes", "leaveAsIs", (p, v) => p.Casing.BuiltInDataTypes = Casing5(v));
        Add("casing.globalVariables", "leaveAsIs", (p, v) => p.Casing.GlobalVariables = Casing5(v));
        Add("casing.useObjectDefinitionCase", "false", (p, v) => p.Casing.SyncWithDatabase = B(v));
    }

    internal static string NormalizeParenStyle(string v) => v.Trim().ToLowerInvariant() switch
    {
        "compactsimple" => "compactSimple",
        "compacttostatement" => "compactToStatement",
        "compactindented" => "compactIndented",
        "compactrightaligned" => "compactRightAligned",
        "expandedsimple" => "expandedSimple",
        "expandedsplit" => "expandedSplit",
        "expandedtostatement" => "expandedToStatement",
        "expandedindented" => "expandedIndented",
        "expandedrightaligned" => "expandedRightAligned",
        _ => "compactSimple",
    };
}
