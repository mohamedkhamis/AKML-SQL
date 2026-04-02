using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// Computes required padding for vertical alignment of columns in formatted SQL output.
/// Supports alignment of aliases, data types, constraints, parameter defaults, and
/// other column-aligned elements in column lists.
/// </summary>
// ReSharper disable once UnusedMember.Global
public class AlignmentCalculator(FormattingProfile profile)
{
    /// <summary>
    /// Represents a column alignment region: a set of lines where elements should be vertically aligned.
    /// </summary>
    public class AlignmentRegion
    {
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public List<AlignmentEntry> Entries { get; } = [];
    }

    /// <summary>
    /// Represents a single line's contribution to an alignment region.
    /// </summary>
    public class AlignmentEntry
    {
        /// <summary>Index of the node to align (the target that should be padded).</summary>
        public int TargetNodeIndex { get; set; }

        /// <summary>Index of the first node on this line (for measuring width).</summary>
        public int LineStartIndex { get; set; }

        /// <summary>Measured width from line start to the target node.</summary>
        public int WidthBeforeTarget { get; set; }
    }

    /// <summary>
    /// Aligns AS aliases in a SELECT list so all AS keywords start at the same column.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public void AlignSelectAliases(List<LayoutNode> nodes)
    {
        if (!profile.List.AlignAliases)
            return;

        var regions = FindAliasRegions(nodes);

        foreach (var region in regions)
        {
            if (region.Entries.Count < 2)
                continue;

            int maxWidth = 0;
            foreach (var entry in region.Entries)
            {
                entry.WidthBeforeTarget = MeasureLineWidth(nodes, entry.LineStartIndex, entry.TargetNodeIndex);
                maxWidth = Math.Max(maxWidth, entry.WidthBeforeTarget);
            }

            foreach (var entry in region.Entries)
            {
                int padding = maxWidth - entry.WidthBeforeTarget + 1;
                nodes[entry.TargetNodeIndex].PrecedingSpaces = Math.Max(padding, 1);
            }
        }
    }

    /// <summary>
    /// Aligns data types in CREATE TABLE column definitions so all data types start at the same column.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public void AlignDataTypes(List<LayoutNode> nodes)
    {
        if (!profile.Ddl.AlignDataTypes)
            return;

        var regions = FindDataTypeRegions(nodes);

        foreach (var region in regions)
        {
            if (region.Entries.Count < 2)
                continue;

            int maxWidth = 0;
            foreach (var entry in region.Entries)
            {
                entry.WidthBeforeTarget = MeasureLineWidth(nodes, entry.LineStartIndex, entry.TargetNodeIndex);
                maxWidth = Math.Max(maxWidth, entry.WidthBeforeTarget);
            }

            foreach (var entry in region.Entries)
            {
                int padding = maxWidth - entry.WidthBeforeTarget + 1;
                if (nodes[entry.TargetNodeIndex].PrecedingBreak == BreakType.None)
                {
                    nodes[entry.TargetNodeIndex].PrecedingSpaces = Math.Max(padding, 1);
                }
            }
        }
    }

    /// <summary>
    /// Aligns inline constraints (NULL, NOT NULL, DEFAULT, etc.) in CREATE TABLE columns
    /// so constraints start at the same column.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public void AlignConstraints(List<LayoutNode> nodes)
    {
        if (!profile.Ddl.AlignConstraints)
            return;

        var regions = FindConstraintRegions(nodes);

        foreach (var region in regions)
        {
            if (region.Entries.Count < 2)
                continue;

            int maxWidth = 0;
            foreach (var entry in region.Entries)
            {
                entry.WidthBeforeTarget = MeasureLineWidth(nodes, entry.LineStartIndex, entry.TargetNodeIndex);
                maxWidth = Math.Max(maxWidth, entry.WidthBeforeTarget);
            }

            foreach (var entry in region.Entries)
            {
                int padding = maxWidth - entry.WidthBeforeTarget + 1;
                if (nodes[entry.TargetNodeIndex].PrecedingBreak == BreakType.None)
                {
                    nodes[entry.TargetNodeIndex].PrecedingSpaces = Math.Max(padding, 1);
                }
            }
        }
    }

    /// <summary>
    /// Aligns parameter data types in procedure/function definitions.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public void AlignParameterDataTypes(List<LayoutNode> nodes)
    {
        if (!profile.Ddl.AlignParameterDataTypes)
            return;

        var regions = FindParameterDataTypeRegions(nodes);

        foreach (var region in regions)
        {
            if (region.Entries.Count < 2)
                continue;

            int maxWidth = 0;
            foreach (var entry in region.Entries)
            {
                entry.WidthBeforeTarget = MeasureLineWidth(nodes, entry.LineStartIndex, entry.TargetNodeIndex);
                maxWidth = Math.Max(maxWidth, entry.WidthBeforeTarget);
            }

            foreach (var entry in region.Entries)
            {
                int padding = maxWidth - entry.WidthBeforeTarget + 1;
                if (nodes[entry.TargetNodeIndex].PrecedingBreak == BreakType.None)
                {
                    nodes[entry.TargetNodeIndex].PrecedingSpaces = Math.Max(padding, 1);
                }
            }
        }
    }

    /// <summary>
    /// Aligns VALUES in INSERT statements across multiple value rows.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public void AlignInsertValues(List<LayoutNode> nodes)
    {
        if (!profile.List.AlignValuesInInsert)
            return;

        // Find INSERT...VALUES regions with multiple value rows
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType != TSqlTokenType.Values)
                continue;

            // Collect value rows (each row is a parenthesized group)
            var rows = FindValueRows(nodes, i);
            if (rows.Count < 2)
                continue;

            // Find the maximum column count
            int maxColumns = 0;
            foreach (var row in rows)
                maxColumns = Math.Max(maxColumns, row.ColumnIndices.Count);

            // For each column position, find the max width and align
            for (int col = 0; col < maxColumns; col++)
            {
                int maxColWidth = 0;
                var columnWidths = new List<(int nodeIndex, int width)>();

                foreach (var row in rows)
                {
                    if (col < row.ColumnIndices.Count)
                    {
                        int colStart = row.ColumnIndices[col];
                        int colEnd = col + 1 < row.ColumnIndices.Count
                            ? row.ColumnIndices[col + 1] - 1 // Before the comma
                            : row.CloseParenIndex;

                        int width = MeasureRange(nodes, colStart, colEnd);
                        columnWidths.Add((colStart, width));
                        maxColWidth = Math.Max(maxColWidth, width);
                    }
                }

                // Pad shorter columns
                foreach (var (nodeIndex, width) in columnWidths)
                {
                    if (width < maxColWidth && nodes[nodeIndex].PrecedingBreak == BreakType.None)
                    {
                        // ReSharper disable once UnusedVariable
                        int extraPadding = maxColWidth - width;
                        // We add padding after the value by adjusting the next token's spacing
                        // For simplicity, pad the token's preceding spaces
                        nodes[nodeIndex].PrecedingSpaces = Math.Max(nodes[nodeIndex].PrecedingSpaces, 1);
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Region finders
    // -----------------------------------------------------------------------

    private static List<AlignmentRegion> FindAliasRegions(List<LayoutNode> nodes)
    {
        var regions = new List<AlignmentRegion>();
        AlignmentRegion? current = null;
        bool inSelect = false;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Select)
            {
                inSelect = true;
                current = new AlignmentRegion { StartIndex = i };
            }
            else if (inSelect && IsClauseEnd(nodes[i].TokenType))
            {
                if (current is { Entries.Count: > 0 })
                {
                    current.EndIndex = i;
                    regions.Add(current);
                }
                current = null;
                inSelect = false;
            }

            if (inSelect && nodes[i].TokenType == TSqlTokenType.As && current != null)
            {
                int lineStart = FindLineStart(nodes, i);
                current.Entries.Add(new AlignmentEntry
                {
                    TargetNodeIndex = i,
                    LineStartIndex = lineStart,
                });
            }
        }

        if (current is { Entries.Count: > 0 })
        {
            current.EndIndex = nodes.Count;
            regions.Add(current);
        }

        return regions;
    }

    private static List<AlignmentRegion> FindDataTypeRegions(List<LayoutNode> nodes)
    {
        var regions = new List<AlignmentRegion>();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType != TSqlTokenType.Create)
                continue;

            // Find TABLE
            int tableIdx = FindNext(nodes, i + 1, TSqlTokenType.Table);
            if (tableIdx < 0) continue;

            // Find open paren
            int openParen = FindNext(nodes, tableIdx + 1, TSqlTokenType.LeftParenthesis);
            if (openParen < 0) continue;

            int closeParen = FindMatchingParen(nodes, openParen);
            if (closeParen < 0) continue;

            var region = new AlignmentRegion { StartIndex = openParen, EndIndex = closeParen };

            // Parse column definitions to find data type positions
            int colStart = openParen + 1;
            int tokenInCol = 0;
            int lineStart = colStart;

            for (int j = openParen + 1; j < closeParen; j++)
            {
                if (nodes[j].PrecedingBreak != BreakType.None)
                {
                    lineStart = j;
                    tokenInCol = 0;
                }

                if (nodes[j].FormattedText.Length == 0)
                    continue;

                tokenInCol++;

                // Data type is typically the second significant token on the line
                if (tokenInCol == 2 && IsBuiltInDataType(nodes[j].FormattedText))
                {
                    region.Entries.Add(new AlignmentEntry
                    {
                        TargetNodeIndex = j,
                        LineStartIndex = lineStart,
                    });
                }

                if (nodes[j].TokenType == TSqlTokenType.Comma)
                {
                    tokenInCol = 0;
                }
            }

            if (region.Entries.Count > 0)
                regions.Add(region);

            i = closeParen;
        }

        return regions;
    }

    private static List<AlignmentRegion> FindConstraintRegions(List<LayoutNode> nodes)
    {
        var regions = new List<AlignmentRegion>();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType != TSqlTokenType.Create)
                continue;

            int tableIdx = FindNext(nodes, i + 1, TSqlTokenType.Table);
            if (tableIdx < 0) continue;

            int openParen = FindNext(nodes, tableIdx + 1, TSqlTokenType.LeftParenthesis);
            if (openParen < 0) continue;

            int closeParen = FindMatchingParen(nodes, openParen);
            if (closeParen < 0) continue;

            var region = new AlignmentRegion { StartIndex = openParen, EndIndex = closeParen };
            int lineStart = openParen + 1;
            bool foundDataType = false;

            for (int j = openParen + 1; j < closeParen; j++)
            {
                if (nodes[j].PrecedingBreak != BreakType.None)
                {
                    lineStart = j;
                    foundDataType = false;
                }

                if (IsBuiltInDataType(nodes[j].FormattedText))
                    foundDataType = true;

                // Constraint keywords after the data type
                if (foundDataType && IsConstraintKeyword(nodes[j].FormattedText))
                {
                    region.Entries.Add(new AlignmentEntry
                    {
                        TargetNodeIndex = j,
                        LineStartIndex = lineStart,
                    });
                    foundDataType = false; // Only first constraint per line
                }

                if (nodes[j].TokenType == TSqlTokenType.Comma)
                    foundDataType = false;
            }

            if (region.Entries.Count > 0)
                regions.Add(region);

            i = closeParen;
        }

        return regions;
    }

    private static List<AlignmentRegion> FindParameterDataTypeRegions(List<LayoutNode> nodes)
    {
        var regions = new List<AlignmentRegion>();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType != TSqlTokenType.Procedure &&
                nodes[i].TokenType != TSqlTokenType.Function)
                continue;

            var region = new AlignmentRegion { StartIndex = i };
            int lineStart = -1;

            for (int j = i + 1; j < nodes.Count; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.As ||
                    nodes[j].TokenType == TSqlTokenType.Semicolon ||
                    nodes[j].TokenType == TSqlTokenType.Go)
                {
                    region.EndIndex = j;
                    break;
                }

                if (nodes[j].PrecedingBreak != BreakType.None)
                    lineStart = j;

                // Find @param data_type patterns
                if (nodes[j].TokenType == TSqlTokenType.Variable && lineStart >= 0)
                {
                    // Next significant token should be the data type
                    for (int k = j + 1; k < nodes.Count; k++)
                    {
                        if (nodes[k].FormattedText.Length == 0) continue;
                        if (IsBuiltInDataType(nodes[k].FormattedText))
                        {
                            region.Entries.Add(new AlignmentEntry
                            {
                                TargetNodeIndex = k,
                                LineStartIndex = lineStart,
                            });
                        }
                        break;
                    }
                }
            }

            if (region.Entries.Count > 0)
                regions.Add(region);
        }

        return regions;
    }

    // -----------------------------------------------------------------------
    // Value row tracking for INSERT alignment
    // -----------------------------------------------------------------------

    private class ValueRow
    {
        public int CloseParenIndex { get; init; }
        public List<int> ColumnIndices { get; } = [];
    }

    private static List<ValueRow> FindValueRows(List<LayoutNode> nodes, int valuesIndex)
    {
        var rows = new List<ValueRow>();

        for (int i = valuesIndex + 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis)
            {
                int closeParen = FindMatchingParen(nodes, i);
                if (closeParen < 0)
                    break;

                var row = new ValueRow
                {
                    CloseParenIndex = closeParen,
                };

                // Find column start positions (after open paren and after each comma)
                if (i + 1 < closeParen)
                    row.ColumnIndices.Add(i + 1);

                for (int j = i + 1; j < closeParen; j++)
                {
                    if (nodes[j].TokenType == TSqlTokenType.Comma && j + 1 < closeParen)
                        row.ColumnIndices.Add(j + 1);
                }

                rows.Add(row);
                i = closeParen;
            }
            else if (nodes[i].TokenType == TSqlTokenType.Comma)
            {
            }
            else if (nodes[i].TokenType == TSqlTokenType.Semicolon ||
                     nodes[i].TokenType == TSqlTokenType.Go)
            {
                break;
            }
        }

        return rows;
    }

    // -----------------------------------------------------------------------
    // Measurement helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Measures the rendered width from start (inclusive) to end (exclusive) on a single line.
    /// Counts each node's text length plus preceding spaces (only for nodes not on a new line).
    /// </summary>
    public static int MeasureLineWidth(List<LayoutNode> nodes, int start, int end)
    {
        int width = 0;
        for (int i = start; i < end; i++)
        {
            if (i > start && nodes[i].PrecedingBreak == BreakType.None)
                width += nodes[i].PrecedingSpaces;
            width += nodes[i].FormattedText.Length;
        }
        return width;
    }

    /// <summary>
    /// Measures the total character width of a range of nodes.
    /// </summary>
    public static int MeasureRange(List<LayoutNode> nodes, int start, int end)
    {
        int width = 0;
        for (int i = start; i < end && i < nodes.Count; i++)
        {
            width += nodes[i].FormattedText.Length;
            if (nodes[i].PrecedingBreak == BreakType.None && i > start)
                width += nodes[i].PrecedingSpaces;
        }
        return width;
    }

    // -----------------------------------------------------------------------
    // Utility helpers
    // -----------------------------------------------------------------------

    private static int FindLineStart(List<LayoutNode> nodes, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
                return i;
        }
        return 0;
    }

    private static int FindNext(List<LayoutNode> nodes, int start, TSqlTokenType type)
    {
        for (int i = start; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == type) return i;
            if (nodes[i].TokenType == TSqlTokenType.Semicolon || nodes[i].TokenType == TSqlTokenType.Go)
                return -1;
        }
        return -1;
    }

    private static int FindMatchingParen(List<LayoutNode> nodes, int openIdx)
    {
        int depth = 1;
        for (int i = openIdx + 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis) depth++;
            else if (nodes[i].TokenType == TSqlTokenType.RightParenthesis)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static bool IsClauseEnd(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.From or TSqlTokenType.Where or TSqlTokenType.Having or
            TSqlTokenType.Join or TSqlTokenType.Semicolon or TSqlTokenType.Go => true,
            _ => false,
        };
    }

    private static bool IsBuiltInDataType(string text)
    {
        var upper = text.ToUpperInvariant();
        return upper is "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT" or
            "BIT" or "FLOAT" or "REAL" or "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY" or
            "CHAR" or "VARCHAR" or "NCHAR" or "NVARCHAR" or "TEXT" or "NTEXT" or
            "BINARY" or "VARBINARY" or "IMAGE" or
            "DATE" or "TIME" or "DATETIME" or "DATETIME2" or "DATETIMEOFFSET" or "SMALLDATETIME" or
            "UNIQUEIDENTIFIER" or "XML" or "SQL_VARIANT" or "HIERARCHYID" or
            "GEOMETRY" or "GEOGRAPHY" or "SYSNAME" or "ROWVERSION" or "TIMESTAMP";
    }

    private static bool IsConstraintKeyword(string text)
    {
        var upper = text.ToUpperInvariant();
        return upper is "NULL" or "NOT" or "DEFAULT" or "PRIMARY" or "UNIQUE" or
            "REFERENCES" or "CHECK" or "IDENTITY" or "CONSTRAINT" or "FOREIGN";
    }
}
