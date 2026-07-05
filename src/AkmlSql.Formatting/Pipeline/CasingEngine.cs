using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Stage 4: Applies casing rules to layout nodes based on token type and profile settings.
/// Supports UPPERCASE, lowercase, PascalCase, camelCase, and AsIs modes.
/// </summary>
public class CasingEngine
{
    /// <summary>
    /// Optional delegate for looking up identifier casing from a database schema cache.
    /// Parameters: (schemaName, objectName) → cached casing or null if not found.
    /// </summary>
    public Func<string, string, string?>? IdentifierLookup { get; set; }

    /// <summary>
    /// When true, uses CamelCaseDictionary to improve PascalCase/camelCase
    /// splitting for compound identifiers (e.g., "customerorderid" → "CustomerOrderId").
    /// </summary>
    public bool UseCamelCaseDictionary { get; set; }

    // Common SQL keywords for PascalCase/camelCase lookup
    private static readonly Dictionary<string, string> PascalCaseKeywords = BuildPascalCaseKeywords();

    // Built-in function names for PascalCase
    private static readonly HashSet<string> BuiltInFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX", "COALESCE", "ISNULL", "NULLIF",
        "CAST", "CONVERT", "TRY_CAST", "TRY_CONVERT", "PARSE", "TRY_PARSE",
        "LEN", "DATALENGTH", "LEFT", "RIGHT", "SUBSTRING", "CHARINDEX", "PATINDEX",
        "LTRIM", "RTRIM", "TRIM", "UPPER", "LOWER", "REPLACE", "STUFF", "REVERSE",
        "CONCAT", "CONCAT_WS", "STRING_AGG", "STRING_SPLIT", "FORMAT", "REPLICATE", "SPACE",
        "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME", "SYSDATETIMEOFFSET",
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATENAME", "DATEPART", "YEAR", "MONTH", "DAY",
        "EOMONTH", "DATEFROMPARTS", "DATETIME2FROMPARTS", "DATETIMEFROMPARTS",
        "ABS", "CEILING", "FLOOR", "ROUND", "POWER", "SQRT", "SIGN", "RAND", "LOG", "LOG10", "EXP",
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD",
        "FIRST_VALUE", "LAST_VALUE", "PERCENT_RANK", "CUME_DIST",
        "NEWID", "NEWSEQUENTIALID", "CHECKSUM", "BINARY_CHECKSUM",
        "ISNUMERIC", "ISDATE", "IIF", "CHOOSE",
        "OBJECT_ID", "OBJECT_NAME", "DB_ID", "DB_NAME", "SCHEMA_ID", "SCHEMA_NAME",
        "COL_NAME", "TYPE_ID", "TYPE_NAME", "COLUMNPROPERTY", "OBJECTPROPERTY",
        "SCOPE_IDENTITY", "IDENT_CURRENT", "ERROR_MESSAGE", "ERROR_NUMBER",
        "ERROR_SEVERITY", "ERROR_STATE", "ERROR_LINE", "ERROR_PROCEDURE",
        "JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "ISJSON", "OPENJSON",
        "HASHBYTES", "COMPRESS", "DECOMPRESS", "UNICODE", "NCHAR", "CHAR", "ASCII",
        "QUOTENAME", "PARSENAME", "TRANSLATE",
    };

    // Built-in data type names
    private static readonly HashSet<string> BuiltInDataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT",
        "BIT", "FLOAT", "REAL", "DECIMAL", "NUMERIC", "MONEY", "SMALLMONEY",
        "CHAR", "VARCHAR", "NCHAR", "NVARCHAR", "TEXT", "NTEXT",
        "BINARY", "VARBINARY", "IMAGE",
        "DATE", "TIME", "DATETIME", "DATETIME2", "DATETIMEOFFSET", "SMALLDATETIME", "TIMESTAMP",
        "UNIQUEIDENTIFIER", "XML", "SQL_VARIANT", "HIERARCHYID", "GEOMETRY", "GEOGRAPHY",
        "CURSOR", "TABLE", "ROWVERSION", "SYSNAME",
    };

    public void ApplyCasing(List<LayoutNode> nodes, FormattingProfile profile)
    {
        // Wire up CamelCaseDictionary setting from profile
        UseCamelCaseDictionary = profile.Casing.CamelCaseDictionary;

        foreach (var node in nodes)
        {
            if (node.IsInNoformatRegion)
                continue;

            // Skip comments
            if (node.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
                continue;

            // Skip string literals and quoted identifiers
            if (node.TokenType is TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral or TSqlTokenType.QuotedIdentifier)
                continue;

            // Skip numeric literals
            if (node.TokenType is TSqlTokenType.Integer or TSqlTokenType.Numeric or TSqlTokenType.Real or TSqlTokenType.Money or TSqlTokenType.HexLiteral)
                continue;

            // For identifiers, try database cache lookup first
            if (node.TokenType == TSqlTokenType.Identifier &&
                profile.Casing.SyncWithDatabase &&
                IdentifierLookup != null)
            {
                // Try common schemas — dbo first, then empty string for schema-agnostic lookup
                var cached = IdentifierLookup("dbo", node.FormattedText)
                          ?? IdentifierLookup("", node.FormattedText);
                if (cached != null)
                {
                    node.FormattedText = cached;
                    continue;
                }
            }

            // Determine what casing mode to apply
            var casingMode = DetermineCasingMode(node, profile.Casing);
            if (casingMode == "AsIs")
                continue;

            // For identifiers with PascalCase/camelCase, optionally use CamelCaseDictionary
            if (node.TokenType == TSqlTokenType.Identifier &&
                UseCamelCaseDictionary &&
                casingMode is "PascalCase" or "camelCase")
            {
                node.FormattedText = CamelCase.CamelCaseDictionary.Apply(node.FormattedText, casingMode);
            }
            else
            {
                node.FormattedText = ApplyCasingMode(node.FormattedText, casingMode);
            }
        }
    }

    private static string DetermineCasingMode(LayoutNode node, CasingOptions casing)
    {
        var text = node.FormattedText;

        // Check if it's a data type keyword
        if (IsDataTypeToken(node.TokenType, text))
            return casing.BuiltInDataTypes;

        // Check if it's a built-in function
        if (IsFunctionToken(node.TokenType, text))
            return casing.BuiltInFunctions;

        // Check if it's a keyword
        if (IsKeywordToken(node.TokenType))
            return casing.ReservedKeywords;

        // Identifiers
        if (node.TokenType == TSqlTokenType.Identifier)
            return casing.Identifiers;

        // Variables — @@-prefixed tokens are system globals (you cannot DECLARE a
        // @@-named local), so they follow GlobalVariables; single-@ locals follow
        // LocalVariables. Before this split every Variable token used LocalVariables,
        // so @@ROWCOUNT etc. were never cased per the GlobalVariables option.
        if (node.TokenType == TSqlTokenType.Variable)
            return text.StartsWith("@@", StringComparison.Ordinal)
                ? casing.GlobalVariables
                : casing.LocalVariables;

        return "AsIs";
    }

    private static bool IsKeywordToken(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Select or TSqlTokenType.From or TSqlTokenType.Where or
            TSqlTokenType.And or TSqlTokenType.Or or TSqlTokenType.Not or
            TSqlTokenType.In or TSqlTokenType.Between or TSqlTokenType.Like or
            TSqlTokenType.Is or TSqlTokenType.Null or
            TSqlTokenType.Insert or TSqlTokenType.Into or TSqlTokenType.Values or
            TSqlTokenType.Update or TSqlTokenType.Set or TSqlTokenType.Delete or
            TSqlTokenType.Merge or
            TSqlTokenType.Create or TSqlTokenType.Alter or TSqlTokenType.Drop or
            TSqlTokenType.Table or TSqlTokenType.Index or TSqlTokenType.View or
            TSqlTokenType.Procedure or TSqlTokenType.Function or TSqlTokenType.Trigger or
            TSqlTokenType.Begin or TSqlTokenType.End or TSqlTokenType.If or TSqlTokenType.Else or
            TSqlTokenType.While or TSqlTokenType.Return or TSqlTokenType.Break or TSqlTokenType.Continue or
            TSqlTokenType.Declare or TSqlTokenType.As or TSqlTokenType.With or
            TSqlTokenType.Case or TSqlTokenType.When or TSqlTokenType.Then or
            TSqlTokenType.Exists or TSqlTokenType.All or TSqlTokenType.Any or
            TSqlTokenType.Join or TSqlTokenType.Inner or TSqlTokenType.Outer or
            TSqlTokenType.Left or TSqlTokenType.Right or TSqlTokenType.Cross or TSqlTokenType.Full or
            TSqlTokenType.On or TSqlTokenType.By or TSqlTokenType.Having or
            TSqlTokenType.Order or TSqlTokenType.Group or
            TSqlTokenType.Union or TSqlTokenType.Except or TSqlTokenType.Intersect or
            TSqlTokenType.Top or TSqlTokenType.Distinct or
            TSqlTokenType.Asc or TSqlTokenType.Desc or
            TSqlTokenType.Over or
            TSqlTokenType.RowCount or TSqlTokenType.Rollback or TSqlTokenType.Commit or
            TSqlTokenType.Transaction or TSqlTokenType.Execute or TSqlTokenType.Exec or
            TSqlTokenType.Grant or TSqlTokenType.Deny or TSqlTokenType.Revoke or
            TSqlTokenType.Go or TSqlTokenType.Use or TSqlTokenType.Print => true,
            _ => false,
        };
    }

    private static bool IsFunctionToken(TSqlTokenType tokenType, string text)
    {
        // ScriptDom may parse some functions as Identifier type
        if (tokenType == TSqlTokenType.Identifier && BuiltInFunctions.Contains(text))
            return true;

        // Some functions are parsed as specific token types
        return tokenType switch
        {
            TSqlTokenType.Coalesce or TSqlTokenType.NullIf or
            TSqlTokenType.Convert => true,
            _ => false,
        };
    }

    private static bool IsDataTypeToken(TSqlTokenType tokenType, string text)
    {
        // Data types are often parsed as Identifier tokens
        if (tokenType == TSqlTokenType.Identifier && BuiltInDataTypes.Contains(text))
            return true;

        // Some are specific token types
        return tokenType switch
        {
            TSqlTokenType.Integer when BuiltInDataTypes.Contains(text) => true,
            _ => false,
        };
    }

    internal static string ApplyCasingMode(string text, string mode)
    {
        return mode switch
        {
            "UPPERCASE" => text.ToUpperInvariant(),
            "lowercase" => text.ToLowerInvariant(),
            "PascalCase" => ToPascalCase(text),
            "camelCase" => ToCamelCase(text),
            _ => text, // AsIs or unknown
        };
    }

    private static string ToPascalCase(string text)
    {
        // For multi-word keywords like "GROUP BY" or "ORDER BY"
        // These arrive as individual tokens, so we just need single-word handling
        var lower = text.ToLowerInvariant();

        // Check our lookup table first
        if (PascalCaseKeywords.TryGetValue(lower, out var pascal))
            return pascal;

        // Handle underscore-separated identifiers: FIRST_VALUE -> FirstValue
        if (text.Contains('_'))
        {
            var parts = lower.Split('_');
            return string.Concat(parts.Select(p =>
                p.Length == 0 ? "_" : char.ToUpperInvariant(p[0]) + p[1..]));
        }

        // Default: capitalize first letter
        if (lower.Length == 0) return lower;
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static string ToCamelCase(string text)
    {
        var pascal = ToPascalCase(text);
        if (pascal.Length == 0) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    private static Dictionary<string, string> BuildPascalCaseKeywords()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["select"] = "Select", ["from"] = "From", ["where"] = "Where",
            ["and"] = "And", ["or"] = "Or", ["not"] = "Not",
            ["in"] = "In", ["between"] = "Between", ["like"] = "Like",
            ["is"] = "Is", ["null"] = "Null", ["as"] = "As",
            ["on"] = "On", ["by"] = "By", ["having"] = "Having",
            ["insert"] = "Insert", ["into"] = "Into", ["values"] = "Values",
            ["update"] = "Update", ["set"] = "Set", ["delete"] = "Delete",
            ["create"] = "Create", ["alter"] = "Alter", ["drop"] = "Drop",
            ["table"] = "Table", ["index"] = "Index", ["view"] = "View",
            ["procedure"] = "Procedure", ["function"] = "Function", ["trigger"] = "Trigger",
            ["begin"] = "Begin", ["end"] = "End", ["if"] = "If", ["else"] = "Else",
            ["while"] = "While", ["return"] = "Return",
            ["declare"] = "Declare", ["with"] = "With",
            ["case"] = "Case", ["when"] = "When", ["then"] = "Then",
            ["exists"] = "Exists", ["all"] = "All", ["any"] = "Any",
            ["join"] = "Join", ["inner"] = "Inner", ["outer"] = "Outer",
            ["left"] = "Left", ["right"] = "Right", ["cross"] = "Cross", ["full"] = "Full",
            ["union"] = "Union", ["except"] = "Except", ["intersect"] = "Intersect",
            ["top"] = "Top", ["distinct"] = "Distinct",
            ["asc"] = "Asc", ["desc"] = "Desc",
            ["over"] = "Over", ["partition"] = "Partition",
            ["group"] = "Group", ["order"] = "Order",
            ["go"] = "Go", ["use"] = "Use", ["exec"] = "Exec", ["execute"] = "Execute",
            ["print"] = "Print", ["try"] = "Try", ["catch"] = "Catch", ["throw"] = "Throw",
            ["grant"] = "Grant", ["deny"] = "Deny", ["revoke"] = "Revoke",
            ["rollback"] = "Rollback", ["commit"] = "Commit", ["transaction"] = "Transaction",
            ["apply"] = "Apply",
        };
        return dict;
    }
}
