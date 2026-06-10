using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Implements DDL formatting rules as a post-processing pass:
/// createTableColumnsOnNewLine, alignDataTypes, alignConstraints, constraintsOnNewLine,
/// inlineConstraintStyle, tableConstraintsSeparate, firstParameterOnNewLine,
/// parameterAlignment, alignParameterDataTypes, alignParameterDefaults,
/// asOnNewLine, beginOnNewLine, collapseShortDDL, collapseThreshold.
/// </summary>
// ReSharper disable once UnusedMember.Global
public class DdlRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var ddl = profile.Ddl;

        ApplyCreateTableColumnsOnNewLine(nodes, ddl);
        ApplyAlignDataTypes(nodes, ddl);
        ApplyAlignConstraints(nodes, ddl);
        ApplyConstraintsOnNewLine(nodes, ddl);
        ApplyInlineConstraintStyle(nodes, ddl);
        ApplyTableConstraintsSeparate(nodes, ddl);
        ApplyParameterFormatting(nodes, ddl);
        ApplyAsOnNewLine(nodes, ddl);
        ApplyBeginOnNewLine(nodes, ddl);
        ApplyCollapseShortDdl(nodes, ddl);
    }

    /// <summary>
    /// createTableColumnsOnNewLine: each column definition in CREATE TABLE goes on its own line.
    /// </summary>
    private static void ApplyCreateTableColumnsOnNewLine(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (!ddl.CreateTableColumnsOnNewLine)
            return;

        var tableRegions = FindCreateTableRegions(nodes);

        foreach (var (openParen, closeParen) in tableRegions)
        {
            // First column after open paren on new line
            if (openParen + 1 < closeParen)
            {
                var firstCol = nodes[openParen + 1];
                if (firstCol.PrecedingBreak == BreakType.None)
                {
                    firstCol.PrecedingBreak = BreakType.NewLine;
                    firstCol.IndentLevel = 1;
                    firstCol.PrecedingSpaces = 0;
                }
            }

            // Each column after comma on new line
            for (int j = openParen + 1; j < closeParen; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.Comma && j + 1 < closeParen)
                {
                    var next = nodes[j + 1];
                    if (next.PrecedingBreak == BreakType.None)
                    {
                        next.PrecedingBreak = BreakType.NewLine;
                        next.IndentLevel = 1;
                        next.PrecedingSpaces = 0;
                    }
                }
            }

            // Close paren on new line
            if (nodes[closeParen].PrecedingBreak == BreakType.None)
            {
                nodes[closeParen].PrecedingBreak = BreakType.NewLine;
                nodes[closeParen].IndentLevel = 0;
                nodes[closeParen].PrecedingSpaces = 0;
            }
        }
    }

    /// <summary>
    /// alignDataTypes: vertically aligns data type declarations in CREATE TABLE column lists.
    /// </summary>
    private static void ApplyAlignDataTypes(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (!ddl.AlignDataTypes)
            return;

        var tableRegions = FindCreateTableRegions(nodes);

        foreach (var (openParen, closeParen) in tableRegions)
        {
            // Parse column definitions: each starts after a comma or at openParen+1
            var columnDefs = ParseColumnDefinitions(nodes, openParen, closeParen);

            if (columnDefs.Count < 2)
                continue;

            // Find the max column name width
            int maxNameWidth = 0;
            foreach (var col in columnDefs)
            {
                if (col.DataTypeIndex >= 0)
                {
                    int nameWidth = MeasureWidth(nodes, col.StartIndex, col.DataTypeIndex);
                    maxNameWidth = Math.Max(maxNameWidth, nameWidth);
                }
            }

            // Pad the data type token to align
            foreach (var col in columnDefs)
            {
                if (col.DataTypeIndex >= 0)
                {
                    int nameWidth = MeasureWidth(nodes, col.StartIndex, col.DataTypeIndex);
                    int padding = maxNameWidth - nameWidth + 1;
                    if (nodes[col.DataTypeIndex].PrecedingBreak == BreakType.None)
                    {
                        nodes[col.DataTypeIndex].PrecedingSpaces = Math.Max(padding, 1);
                    }
                }
            }
        }
    }

    /// <summary>
    /// alignConstraints: vertically aligns inline constraints (NULL, NOT NULL, DEFAULT, etc.)
    /// in CREATE TABLE column lists.
    /// </summary>
    private static void ApplyAlignConstraints(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (!ddl.AlignConstraints)
            return;

        var tableRegions = FindCreateTableRegions(nodes);

        foreach (var (openParen, closeParen) in tableRegions)
        {
            var columnDefs = ParseColumnDefinitions(nodes, openParen, closeParen);

            if (columnDefs.Count < 2)
                continue;

            // Find the max width before the constraint region for alignment
            int maxPreConstraintWidth = 0;
            foreach (var col in columnDefs)
            {
                if (col.ConstraintIndex >= 0)
                {
                    int width = MeasureWidth(nodes, col.StartIndex, col.ConstraintIndex);
                    maxPreConstraintWidth = Math.Max(maxPreConstraintWidth, width);
                }
            }

            // Pad constraints to align
            foreach (var col in columnDefs)
            {
                if (col.ConstraintIndex >= 0)
                {
                    int width = MeasureWidth(nodes, col.StartIndex, col.ConstraintIndex);
                    int padding = maxPreConstraintWidth - width + 1;
                    if (nodes[col.ConstraintIndex].PrecedingBreak == BreakType.None)
                    {
                        nodes[col.ConstraintIndex].PrecedingSpaces = Math.Max(padding, 1);
                    }
                }
            }
        }
    }

    /// <summary>
    /// constraintsOnNewLine: puts inline column constraints on their own line.
    /// </summary>
    private static void ApplyConstraintsOnNewLine(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (!ddl.ConstraintsOnNewLine)
            return;

        var tableRegions = FindCreateTableRegions(nodes);

        foreach (var (openParen, closeParen) in tableRegions)
        {
            var columnDefs = ParseColumnDefinitions(nodes, openParen, closeParen);

            foreach (var col in columnDefs)
            {
                if (col.ConstraintIndex >= 0)
                {
                    nodes[col.ConstraintIndex].PrecedingBreak = BreakType.NewLine;
                    nodes[col.ConstraintIndex].IndentLevel = 2;
                    nodes[col.ConstraintIndex].PrecedingSpaces = 0;
                }
            }
        }
    }

    /// <summary>
    /// inlineConstraintStyle: "sameLine" keeps constraints on the same line as the column,
    /// "nextLine" puts them on the next line.
    /// </summary>
    private static void ApplyInlineConstraintStyle(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (ddl.InlineConstraintStyle != "nextLine")
            return;

        // Same as constraintsOnNewLine but triggered by style setting
        var tableRegions = FindCreateTableRegions(nodes);

        foreach (var (openParen, closeParen) in tableRegions)
        {
            var columnDefs = ParseColumnDefinitions(nodes, openParen, closeParen);

            foreach (var col in columnDefs)
            {
                if (col.ConstraintIndex >= 0 && nodes[col.ConstraintIndex].PrecedingBreak == BreakType.None)
                {
                    nodes[col.ConstraintIndex].PrecedingBreak = BreakType.NewLine;
                    nodes[col.ConstraintIndex].IndentLevel = 2;
                    nodes[col.ConstraintIndex].PrecedingSpaces = 0;
                }
            }
        }
    }

    /// <summary>
    /// tableConstraintsSeparate: puts an empty line before table-level constraints
    /// (PRIMARY KEY, UNIQUE, FOREIGN KEY, CHECK at the table level).
    /// </summary>
    private static void ApplyTableConstraintsSeparate(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (!ddl.TableConstraintsSeparate)
            return;

        var tableRegions = FindCreateTableRegions(nodes);

        foreach (var (openParen, closeParen) in tableRegions)
        {
            // Find table-level constraint keywords (CONSTRAINT, PRIMARY, UNIQUE, FOREIGN, CHECK)
            // that appear at the comma-separated level (not within a column definition)
            for (int j = openParen + 1; j < closeParen; j++)
            {
                if (nodes[j].IsInNoformatRegion)
                    continue;

                if (IsTableLevelConstraintKeyword(nodes, j, openParen))
                {
                    if (nodes[j].PrecedingBreak == BreakType.NewLine)
                    {
                        nodes[j].PrecedingBreak = BreakType.EmptyLine;
                    }
                }
            }
        }
    }

    /// <summary>
    /// firstParameterOnNewLine and parameterAlignment: controls procedure/function parameter layout.
    /// </summary>
    private static void ApplyParameterFormatting(List<LayoutNode> nodes, DdlOptions ddl)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion)
                continue;

            if (nodes[i].TokenType != TSqlTokenType.Procedure &&
                nodes[i].TokenType != TSqlTokenType.Function)
                continue;

            // Find parameter region (between procedure name and AS keyword or open paren)
            int paramStart = -1;
            int paramEnd = -1;
            var parameterNodes = new List<(int startIdx, int dataTypeIdx)>();
            int parenDepth = 0;
            bool inParams = false;

            for (int j = i + 1; j < nodes.Count; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis)
                {
                    parenDepth++;
                    if (parenDepth == 1)
                    {
                        paramStart = j;
                        inParams = true;
                    }
                    continue;
                }
                if (nodes[j].TokenType == TSqlTokenType.RightParenthesis)
                {
                    parenDepth--;
                    if (parenDepth == 0)
                    {
                        paramEnd = j;
                        break;
                    }
                    continue;
                }

                // For procedures without parentheses, parameters start at the first @variable
                if (!inParams && nodes[j].TokenType == TSqlTokenType.Variable)
                {
                    paramStart = j;
                    inParams = true;
                }

                if (inParams && nodes[j].TokenType == TSqlTokenType.As &&
                    parenDepth == 0)
                {
                    paramEnd = j;
                    break;
                }

                // Track parameter starts (variables)
                if (inParams && nodes[j].TokenType == TSqlTokenType.Variable && parenDepth <= 1)
                {
                    // Find the data type that follows
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    int dtIdx = FindParameterDataType(nodes, j + 1, paramEnd >= 0 ? paramEnd : nodes.Count);
                    parameterNodes.Add((j, dtIdx));
                }
            }

            if (paramStart < 0 || parameterNodes.Count == 0)
                continue;

            // firstParameterOnNewLine
            if (ddl.FirstParameterOnNewLine == "always" ||
                (ddl.FirstParameterOnNewLine == "auto" && parameterNodes.Count > 1))
            {
                var firstParam = nodes[parameterNodes[0].startIdx];
                if (firstParam.PrecedingBreak == BreakType.None)
                {
                    firstParam.PrecedingBreak = BreakType.NewLine;
                    firstParam.IndentLevel = 1;
                    firstParam.PrecedingSpaces = 0;
                }
            }

            // alignParameterDataTypes
            if (ddl.AlignParameterDataTypes && parameterNodes.Count >= 2)
            {
                int maxNameWidth = 0;
                var nameWidths = new List<int>();

                foreach (var (startIdx, dataTypeIdx) in parameterNodes)
                {
                    int width = dataTypeIdx >= 0
                        ? MeasureWidth(nodes, startIdx, dataTypeIdx)
                        : 0;
                    nameWidths.Add(width);
                    maxNameWidth = Math.Max(maxNameWidth, width);
                }

                for (int p = 0; p < parameterNodes.Count; p++)
                {
                    var (_, dataTypeIdx) = parameterNodes[p];
                    if (dataTypeIdx >= 0 && nameWidths[p] > 0 &&
                        nodes[dataTypeIdx].PrecedingBreak == BreakType.None)
                    {
                        int padding = maxNameWidth - nameWidths[p] + 1;
                        nodes[dataTypeIdx].PrecedingSpaces = Math.Max(padding, 1);
                    }
                }
            }

            // alignParameterDefaults
            if (ddl.AlignParameterDefaults && parameterNodes.Count >= 2)
            {
                AlignParameterDefaults(nodes, parameterNodes, paramEnd >= 0 ? paramEnd : nodes.Count);
            }

            i = paramEnd >= 0 ? paramEnd : i;
        }
    }

    /// <summary>
    /// asOnNewLine: puts the AS keyword on a new line in CREATE/ALTER PROCEDURE/FUNCTION/VIEW.
    /// </summary>
    private static void ApplyAsOnNewLine(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (!ddl.AsOnNewLine)
            return;

        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.As)
                continue;

            // Check if this AS is in a DDL context (after CREATE/ALTER ... PROCEDURE/FUNCTION/VIEW)
            if (IsAsInDdlContext(nodes, i))
            {
                if (nodes[i].PrecedingBreak == BreakType.None)
                {
                    nodes[i].PrecedingBreak = BreakType.NewLine;
                    nodes[i].IndentLevel = 0;
                    nodes[i].PrecedingSpaces = 0;
                }
            }
        }
    }

    /// <summary>
    /// beginOnNewLine: puts BEGIN on a new line after AS in DDL definitions.
    /// </summary>
    private static void ApplyBeginOnNewLine(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (!ddl.BeginOnNewLine)
            return;

        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.Begin)
                continue;

            // Check if preceded by AS in DDL context
            if (i > 0 && nodes[i - 1].TokenType == TSqlTokenType.As)
            {
                if (nodes[i].PrecedingBreak == BreakType.None)
                {
                    nodes[i].PrecedingBreak = BreakType.NewLine;
                    nodes[i].IndentLevel = 0;
                    nodes[i].PrecedingSpaces = 0;
                }
            }
        }
    }

    /// <summary>
    /// collapseShortDDL: collapses short DDL statements onto a single line
    /// when they fit within collapseThreshold characters.
    /// </summary>
    private static void ApplyCollapseShortDdl(List<LayoutNode> nodes, DdlOptions ddl)
    {
        if (!ddl.CollapseShortDdl)
            return;

        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i].IsInNoformatRegion)
            {
                i++;
                continue;
            }

            if (IsDdlStart(nodes[i].TokenType))
            {
                int stmtEnd = FindDdlEnd(nodes, i + 1);
                int totalLength = MeasureLength(nodes, i, stmtEnd);

                // Never collapse across a block boundary: a short proc/function/trigger body's
                // BEGIN…END layout (the nested-statement breaks) is structural — inlining
                // "AS BEGIN SET …" undoes it and nothing downstream restores the body breaks.
                bool containsBlock = false;
                for (int j = i + 1; j < stmtEnd && j < nodes.Count; j++)
                {
                    if (nodes[j].TokenType == TSqlTokenType.Begin)
                    {
                        containsBlock = true;
                        break;
                    }
                }

                if (!containsBlock && totalLength <= ddl.CollapseThreshold)
                {
                    for (int j = i + 1; j < stmtEnd && j < nodes.Count; j++)
                    {
                        if (nodes[j].PrecedingBreak == BreakType.NewLine ||
                            nodes[j].PrecedingBreak == BreakType.EmptyLine)
                        {
                            nodes[j].PrecedingBreak = BreakType.None;
                            nodes[j].PrecedingSpaces = nodes[j].TokenType == TSqlTokenType.Comma ? 0 : 1;
                            nodes[j].IndentLevel = 0;
                        }
                    }
                }

                i = stmtEnd;
            }
            else
            {
                i++;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Finds CREATE TABLE regions: returns list of (openParen, closeParen) index pairs.
    /// </summary>
    private static List<(int openParen, int closeParen)> FindCreateTableRegions(List<LayoutNode> nodes)
    {
        var regions = new List<(int, int)>();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType != TSqlTokenType.Create)
                continue;

            // Find TABLE keyword
            int tableIdx = -1;
            for (int j = i + 1; j < nodes.Count && j <= i + 3; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.Table)
                {
                    tableIdx = j;
                    break;
                }
            }
            if (tableIdx < 0)
                continue;

            // Find opening parenthesis
            int openParen = -1;
            for (int j = tableIdx + 1; j < nodes.Count; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis)
                {
                    openParen = j;
                    break;
                }
                if (nodes[j].TokenType == TSqlTokenType.Semicolon || nodes[j].TokenType == TSqlTokenType.Go)
                    break;
            }
            if (openParen < 0)
                continue;

            // Find matching close paren
            int depth = 1;
            int closeParen = -1;
            for (int j = openParen + 1; j < nodes.Count; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis) depth++;
                else if (nodes[j].TokenType == TSqlTokenType.RightParenthesis)
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeParen = j;
                        break;
                    }
                }
            }

            if (closeParen > openParen)
            {
                regions.Add((openParen, closeParen));
                i = closeParen;
            }
        }

        return regions;
    }

    /// <summary>
    /// Represents a column definition within a CREATE TABLE body.
    /// </summary>
    private struct ColumnDef
    {
        public int StartIndex;
        // ReSharper disable once NotAccessedField.Local
        public int EndIndex;
        public int DataTypeIndex;   // Index of the data type token, or -1
        public int ConstraintIndex; // Index of the first constraint token, or -1
    }

    /// <summary>
    /// Parses column definitions from a CREATE TABLE parenthesized body.
    /// </summary>
    private static List<ColumnDef> ParseColumnDefinitions(List<LayoutNode> nodes, int openParen, int closeParen)
    {
        var columns = new List<ColumnDef>();
        int colStart = openParen + 1;
        int parenDepth = 0;

        for (int j = openParen + 1; j <= closeParen; j++)
        {
            if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis)
                parenDepth++;
            else if (nodes[j].TokenType == TSqlTokenType.RightParenthesis)
            {
                parenDepth--;
                if (parenDepth < 0) // This is the closing paren of the CREATE TABLE
                {
                    if (colStart < j)
                    {
                        columns.Add(AnalyzeColumnDef(nodes, colStart, j));
                    }
                    break;
                }
            }

            if (parenDepth == 0 && nodes[j].TokenType == TSqlTokenType.Comma)
            {
                columns.Add(AnalyzeColumnDef(nodes, colStart, j));
                colStart = j + 1;
            }
        }

        return columns;
    }

    /// <summary>
    /// Analyzes a single column definition to find the data type and constraint positions.
    /// </summary>
    private static ColumnDef AnalyzeColumnDef(List<LayoutNode> nodes, int start, int end)
    {
        var col = new ColumnDef
        {
            StartIndex = start,
            EndIndex = end,
            DataTypeIndex = -1,
            ConstraintIndex = -1
        };

        // The data type is typically the second significant token (after column name)
        // or the first token if it's a table-level constraint
        int tokenCount = 0;
        for (int i = start; i < end; i++)
        {
            if (nodes[i].FormattedText.Length == 0)
                continue;

            tokenCount++;

            // Second token is likely the data type (first is column name)
            if (tokenCount == 2 && IsDataTypeIdentifier(nodes[i]))
            {
                col.DataTypeIndex = i;
            }

            // Constraint keywords
            if (col.ConstraintIndex < 0 && IsConstraintKeyword(nodes[i]))
            {
                col.ConstraintIndex = i;
            }
        }

        return col;
    }

    private static bool IsDataTypeIdentifier(LayoutNode node)
    {
        if (node.TokenType == TSqlTokenType.Identifier)
        {
            var upper = node.FormattedText.ToUpperInvariant();
            return upper is "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT" or
                "BIT" or "FLOAT" or "REAL" or "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY" or
                "CHAR" or "VARCHAR" or "NCHAR" or "NVARCHAR" or "TEXT" or "NTEXT" or
                "BINARY" or "VARBINARY" or "IMAGE" or
                "DATE" or "TIME" or "DATETIME" or "DATETIME2" or "DATETIMEOFFSET" or "SMALLDATETIME" or
                "UNIQUEIDENTIFIER" or "XML" or "SQL_VARIANT" or "HIERARCHYID" or
                "GEOMETRY" or "GEOGRAPHY" or "SYSNAME" or "ROWVERSION" or "TIMESTAMP";
        }
        return false;
    }

    private static bool IsConstraintKeyword(LayoutNode node)
    {
        var upper = node.FormattedText.ToUpperInvariant();
        return upper is "NULL" or "NOT" or "DEFAULT" or "PRIMARY" or "UNIQUE" or
            "REFERENCES" or "CHECK" or "IDENTITY" or "CONSTRAINT" or "FOREIGN";
    }

    private static bool IsTableLevelConstraintKeyword(List<LayoutNode> nodes, int index, int openParen)
    {
        var upper = nodes[index].FormattedText.ToUpperInvariant();
        if (upper is not ("CONSTRAINT" or "PRIMARY" or "UNIQUE" or "FOREIGN" or "CHECK"))
            return false;

        // Verify this is at the top level (preceded by comma at the same depth)
        // Walk backward to find the preceding comma
        int parenDepth = 0;
        for (int i = index - 1; i > openParen; i--)
        {
            if (nodes[i].TokenType == TSqlTokenType.RightParenthesis) parenDepth++;
            else if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis) parenDepth--;

            if (parenDepth == 0 && nodes[i].TokenType == TSqlTokenType.Comma)
                return true;
            if (parenDepth < 0)
                return false;
        }

        return false;
    }

    private static int FindParameterDataType(List<LayoutNode> nodes, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Comma ||
                nodes[i].TokenType == TSqlTokenType.RightParenthesis ||
                nodes[i].TokenType == TSqlTokenType.As)
                return -1;

            if (IsDataTypeIdentifier(nodes[i]))
                return i;
        }
        return -1;
    }

    private static void AlignParameterDefaults(List<LayoutNode> nodes,
        List<(int startIdx, int dataTypeIdx)> parameterNodes, int paramEnd)
    {
        // Find default value assignments (= token) for each parameter
        var defaultIndices = new List<int>();
        int maxPreDefaultWidth = 0;

        foreach (var (startIdx, _) in parameterNodes)
        {
            int eqIdx = -1;
            for (int j = startIdx + 1; j < paramEnd; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.Comma ||
                    nodes[j].TokenType == TSqlTokenType.RightParenthesis)
                    break;
                if (nodes[j].TokenType == TSqlTokenType.EqualsSign)
                {
                    eqIdx = j;
                    break;
                }
            }

            defaultIndices.Add(eqIdx);
            if (eqIdx >= 0)
            {
                int width = MeasureWidth(nodes, startIdx, eqIdx);
                maxPreDefaultWidth = Math.Max(maxPreDefaultWidth, width);
            }
        }

        // Pad default values to align
        for (int p = 0; p < parameterNodes.Count; p++)
        {
            int eqIdx = defaultIndices[p];
            if (eqIdx < 0)
                continue;

            int width = MeasureWidth(nodes, parameterNodes[p].startIdx, eqIdx);
            int padding = maxPreDefaultWidth - width + 1;
            if (nodes[eqIdx].PrecedingBreak == BreakType.None)
            {
                nodes[eqIdx].PrecedingSpaces = Math.Max(padding, 1);
            }
        }
    }

    private static bool IsAsInDdlContext(List<LayoutNode> nodes, int asIndex)
    {
        for (int i = asIndex - 1; i >= 0; i--)
        {
            var tt = nodes[i].TokenType;
            if (tt is TSqlTokenType.Procedure or TSqlTokenType.Function or TSqlTokenType.View or TSqlTokenType.Trigger)
                return true;
            if (tt is TSqlTokenType.Create or TSqlTokenType.Alter)
                return true;
            if (tt is TSqlTokenType.Semicolon or TSqlTokenType.Go)
                return false;
        }
        return false;
    }

    private static bool IsDdlStart(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Create or TSqlTokenType.Alter or TSqlTokenType.Drop => true,
            _ => false,
        };
    }

    private static int FindDdlEnd(List<LayoutNode> nodes, int start)
    {
        for (int i = start; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Semicolon || nodes[i].TokenType == TSqlTokenType.Go)
                return i;
        }
        return nodes.Count;
    }

    private static int MeasureWidth(List<LayoutNode> nodes, int start, int end)
    {
        int width = 0;
        for (int i = start; i < end; i++)
        {
            width += nodes[i].FormattedText.Length;
            if (i > start && nodes[i].PrecedingBreak == BreakType.None)
                width += nodes[i].PrecedingSpaces;
        }
        return width;
    }

    private static int MeasureLength(List<LayoutNode> nodes, int start, int end)
    {
        int length = 0;
        for (int i = start; i < end && i < nodes.Count; i++)
        {
            length += nodes[i].FormattedText.Length;
            if (nodes[i].PrecedingBreak == BreakType.None)
                length += nodes[i].PrecedingSpaces;
            else
                length += 1;
        }
        return length;
    }
}
