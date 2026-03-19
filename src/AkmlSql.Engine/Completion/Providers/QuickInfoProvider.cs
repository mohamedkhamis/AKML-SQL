using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// T065-T066: Provides quick info tooltips for identifiers under the cursor.
/// Resolves identifiers to tables, columns, variables, or keywords and returns
/// descriptive information.
/// </summary>
public class QuickInfoProvider
{
    /// <summary>
    /// T065: Get quick info for the identifier at the cursor position.
    /// </summary>
    public QuickInfoResponse GetQuickInfo(
        string documentText,
        int cursorOffset,
        DatabaseCache? cache,
        TsqlParserService parser)
    {
        var tokens = parser.GetTokenStream(documentText);

        // T066: Resolve the identifier at the cursor
        var (token, tokenIndex) = FindTokenAtOffset(tokens, cursorOffset);
        if (token == null)
            return EmptyResponse();

        // Skip whitespace, comments, strings
        if (token.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment
            or TSqlTokenType.MultilineComment or TSqlTokenType.AsciiStringLiteral
            or TSqlTokenType.UnicodeStringLiteral)
            return EmptyResponse();

        var identifierText = token.Text.Trim('[', ']', '"');

        // Check if preceded by dot (qualified reference like alias.column)
        bool hasDotPrefix = tokenIndex >= 2 &&
            tokens[tokenIndex - 1].TokenType == TSqlTokenType.Dot &&
            (tokens[tokenIndex - 2].TokenType == TSqlTokenType.Identifier ||
             tokens[tokenIndex - 2].TokenType == TSqlTokenType.QuotedIdentifier);
        string? dotPrefix = hasDotPrefix
            ? tokens[tokenIndex - 2].Text.Trim('[', ']', '"')
            : null;

        // Resolve aliases from AST for context
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
        var script = parser.ParseWithSuffix(documentText, out _);
        if (script != null)
        {
            var aliasResolver = new AliasResolver();
            var resolved = aliasResolver.ResolveAliases(script, cursorOffset);
            foreach (var (alias, tableRef) in resolved)
                aliases[alias] = tableRef.FullName;
        }

        // T066: Try resolution in order: alias.column → alias → table → variable → keyword
        if (hasDotPrefix && dotPrefix != null && cache != null)
        {
            var columnInfo = TryResolveColumn(dotPrefix, identifierText, aliases, cache);
            if (columnInfo != null)
                return columnInfo;
        }

        if (cache != null)
        {
            // Check if it's a known alias
            if (aliases.TryGetValue(identifierText, out var aliasedTable))
            {
                var tableInfo = TryResolveTable(aliasedTable, cache);
                if (tableInfo != null)
                {
                    tableInfo.Header = $"{identifierText} (alias for {aliasedTable})";
                    return tableInfo;
                }
            }

            // Check if it's a table name directly
            var directTable = TryResolveTableByName(identifierText, cache);
            if (directTable != null)
                return directTable;
        }

        // Check if it's a variable
        if (identifierText.StartsWith("@"))
            return MakeVariableInfo(identifierText);

        // Check if it's a keyword
        if (IsKeyword(token))
            return MakeKeywordInfo(identifierText);

        // Check if it's a known function
        if (BuiltinFunctionDictionary.IsBuiltinFunction(identifierText))
            return MakeFunctionInfo(identifierText);

        return EmptyResponse();
    }

    /// <summary>
    /// T066: Find the token at or immediately before the given offset.
    /// </summary>
    private static (TSqlParserToken? Token, int Index) FindTokenAtOffset(
        IList<TSqlParserToken> tokens, int cursorOffset)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (cursorOffset >= t.Offset && cursorOffset <= t.Offset + t.Text.Length)
                return (t, i);
        }

        return (null, -1);
    }

    /// <summary>
    /// Try to resolve alias.column to column info.
    /// </summary>
    private static QuickInfoResponse? TryResolveColumn(
        string prefix, string columnName,
        Dictionary<string, string> aliases, DatabaseCache cache)
    {
        if (!aliases.TryGetValue(prefix, out var fullTableName))
            return null;

        var parts = fullTableName.Split('.');
        var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
        var tableName = parts.Length >= 2 ? parts[1] : parts[0];

        var dbObject = cache.FindObject(schemaName, tableName);
        if (dbObject == null || !dbObject.ColumnsLoaded)
            return null;

        var column = dbObject.Columns.FirstOrDefault(c =>
            c.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (column == null)
            return null;

        var details = new List<QuickInfoDetail>
        {
            new QuickInfoDetail("Type", column.TypeDisplay),
            new QuickInfoDetail("Nullable", column.IsNullable ? "YES" : "NO")
        };

        if (column.IsPrimaryKey)
            details.Add(new QuickInfoDetail("Primary Key", "YES"));
        if (column.IsIdentity)
            details.Add(new QuickInfoDetail("Identity", "YES"));
        if (column.IsComputed)
            details.Add(new QuickInfoDetail("Computed", column.ComputedDefinition ?? "YES"));
        if (!string.IsNullOrEmpty(column.DefaultValue))
            details.Add(new QuickInfoDetail("Default", column.DefaultValue));

        // Check FK relationships
        var fks = cache.GetForeignKeysForTable(schemaName, tableName);
        foreach (var fk in fks)
        {
            if (fk.ParentSchema.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
                fk.ParentTable.Equals(tableName, StringComparison.OrdinalIgnoreCase) &&
                fk.ParentColumns.Any(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase)))
            {
                details.Add(new QuickInfoDetail("FK", $"→ {fk.ReferencedFullName} ({fk.FkName})"));
            }
        }

        return new QuickInfoResponse
        {
            ObjectType = "Column",
            Header = $"{prefix}.{column.ColumnName} ({dbObject.FullName})",
            Details = details.ToArray(),
            Description = column.Description
        };
    }

    /// <summary>
    /// Try to resolve a fully qualified table name (schema.table) to table info.
    /// </summary>
    private static QuickInfoResponse? TryResolveTable(string fullTableName, DatabaseCache cache)
    {
        var parts = fullTableName.Split('.');
        var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
        var tableName = parts.Length >= 2 ? parts[1] : parts[0];

        return TryResolveTableDirect(schemaName, tableName, cache);
    }

    /// <summary>
    /// Try to find a table by unqualified name, searching across schemas.
    /// </summary>
    private static QuickInfoResponse? TryResolveTableByName(string tableName, DatabaseCache cache)
    {
        // Try dbo first
        var result = TryResolveTableDirect("dbo", tableName, cache);
        if (result != null) return result;

        // Search other schemas
        foreach (var schemaName in cache.GetSchemaNames())
        {
            if (schemaName.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                continue;

            result = TryResolveTableDirect(schemaName, tableName, cache);
            if (result != null) return result;
        }

        return null;
    }

    private static QuickInfoResponse? TryResolveTableDirect(
        string schemaName, string tableName, DatabaseCache cache)
    {
        var dbObject = cache.FindObject(schemaName, tableName);
        if (dbObject == null)
            return null;

        var details = new List<QuickInfoDetail>
        {
            new QuickInfoDetail("Schema", dbObject.SchemaName),
            new QuickInfoDetail("Type", dbObject.ObjectType.ToString())
        };

        if (dbObject.ColumnsLoaded)
            details.Add(new QuickInfoDetail("Columns", dbObject.Columns.Count.ToString()));

        if (dbObject.ApproxRowCount > 0)
            details.Add(new QuickInfoDetail("Row Count", $"~{dbObject.ApproxRowCount:N0}"));

        details.Add(new QuickInfoDetail("Modified", dbObject.ModifyDate.ToString("yyyy-MM-dd")));

        return new QuickInfoResponse
        {
            ObjectType = dbObject.ObjectType.ToString(),
            Header = dbObject.FullName,
            Details = details.ToArray(),
            Description = dbObject.Description
        };
    }

    private static QuickInfoResponse MakeVariableInfo(string variableName)
    {
        return new QuickInfoResponse
        {
            ObjectType = "Variable",
            Header = variableName,
            Details = [new QuickInfoDetail("Kind", variableName.StartsWith("@@") ? "System Variable" : "Local Variable")],
            Description = null
        };
    }

    private static bool IsKeyword(TSqlParserToken token)
    {
        // TSqlTokenType values for keywords are generally not Identifier
        return token.TokenType != TSqlTokenType.Identifier &&
               token.TokenType != TSqlTokenType.QuotedIdentifier &&
               token.TokenType != TSqlTokenType.Variable &&
               token.TokenType != TSqlTokenType.Integer &&
               token.TokenType != TSqlTokenType.Numeric &&
               token.TokenType != TSqlTokenType.AsciiStringLiteral &&
               token.TokenType != TSqlTokenType.UnicodeStringLiteral &&
               token.TokenType != TSqlTokenType.WhiteSpace &&
               token.TokenType != TSqlTokenType.Dot &&
               token.TokenType != TSqlTokenType.Comma &&
               token.TokenType != TSqlTokenType.LeftParenthesis &&
               token.TokenType != TSqlTokenType.RightParenthesis &&
               token.TokenType != TSqlTokenType.Semicolon &&
               token.TokenType != TSqlTokenType.EndOfFile;
    }

    private static QuickInfoResponse MakeKeywordInfo(string keyword)
    {
        return new QuickInfoResponse
        {
            ObjectType = "Keyword",
            Header = keyword.ToUpperInvariant(),
            Details = [new QuickInfoDetail("Kind", "T-SQL Keyword")],
            Description = null
        };
    }

    private static QuickInfoResponse MakeFunctionInfo(string functionName)
    {
        if (BuiltinFunctionDictionary.TryGetSignature(functionName, out var overloads) && overloads.Length > 0)
        {
            var details = new List<QuickInfoDetail>
            {
                new QuickInfoDetail("Kind", "Built-in Function"),
                new QuickInfoDetail("Overloads", overloads.Length.ToString())
            };

            return new QuickInfoResponse
            {
                ObjectType = "Function",
                Header = overloads[0].Label,
                Details = details.ToArray(),
                Description = overloads[0].Documentation
            };
        }

        return new QuickInfoResponse
        {
            ObjectType = "Function",
            Header = functionName,
            Details = [new QuickInfoDetail("Kind", "Built-in Function")]
        };
    }

    private static QuickInfoResponse EmptyResponse() => new()
    {
        ObjectType = string.Empty,
        Header = string.Empty,
        Details = []
    };
}
