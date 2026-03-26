#nullable enable
using System.Text.RegularExpressions;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Productivity;

/// <summary>
/// Builds a hierarchical document outline tree from SQL text by combining AST analysis
/// with comment-based region markers (--region / --endregion).
/// </summary>
public partial class DocumentOutlineBuilder
{
    private readonly TsqlParserService _parserService;

    public DocumentOutlineBuilder(TsqlParserService parserService)
    {
        _parserService = parserService ?? throw new ArgumentNullException(nameof(parserService));
    }

    /// <summary>
    /// Builds the document outline from the given SQL text.
    /// Returns an array of top-level outline nodes.
    /// </summary>
    public OutlineNodeDto[] BuildOutline(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        var nodes = new List<OutlineNodeDto>();

        try
        {
            // Phase 1: Parse AST and extract structural nodes
            var script = _parserService.Parse(sql, out _);
            if (script != null)
            {
                foreach (var batch in script.Batches)
                {
                    foreach (var statement in batch.Statements)
                    {
                        var node = BuildNodeFromStatement(statement, sql);
                        if (node != null)
                            nodes.Add(node);
                    }
                }
            }

            // Phase 2: Scan for --region / --endregion comment markers
            var regionNodes = ScanRegions(sql);
            nodes.AddRange(regionNodes);

            // Sort all nodes by start offset
            nodes.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DocumentOutlineBuilder: failed to build outline");
        }

        return nodes.ToArray();
    }

    /// <summary>
    /// Maximum recursion depth for nested statement collection to prevent stack overflow.
    /// </summary>
    private const int MaxRecursionDepth = 50;

    /// <summary>
    /// Builds an outline node from a TSqlStatement, returning <c>null</c> for
    /// statements that should not appear in the outline (e.g. simple DML).
    /// </summary>
    private static OutlineNodeDto? BuildNodeFromStatement(TSqlStatement statement, string sql, int depth = 0)
    {
        switch (statement)
        {
            case CreateProcedureStatement createProc:
                return BuildProcedureNode(createProc, sql, "Procedure", depth);

            case AlterProcedureStatement alterProc:
                return new OutlineNodeDto
                {
                    Name = ExtractSchemaObjectName(alterProc.ProcedureReference?.Name),
                    NodeType = "Procedure",
                    StartLine = alterProc.StartLine - 1,
                    StartOffset = alterProc.StartOffset,
                    EndOffset = alterProc.StartOffset + alterProc.FragmentLength,
                    Children = CollectChildStatements(alterProc.StatementList, sql, depth)
                };

            case CreateFunctionStatement createFunc:
                return new OutlineNodeDto
                {
                    Name = ExtractSchemaObjectName(createFunc.Name),
                    NodeType = "Function",
                    StartLine = createFunc.StartLine - 1,
                    StartOffset = createFunc.StartOffset,
                    EndOffset = createFunc.StartOffset + createFunc.FragmentLength,
                    Children = CollectChildStatements(
                        (createFunc.StatementList), sql, depth)
                };

            case AlterFunctionStatement alterFunc:
                return new OutlineNodeDto
                {
                    Name = ExtractSchemaObjectName(alterFunc.Name),
                    NodeType = "Function",
                    StartLine = alterFunc.StartLine - 1,
                    StartOffset = alterFunc.StartOffset,
                    EndOffset = alterFunc.StartOffset + alterFunc.FragmentLength,
                    Children = []
                };

            case CreateViewStatement createView:
                return new OutlineNodeDto
                {
                    Name = ExtractSchemaObjectName(createView.SchemaObjectName),
                    NodeType = "View",
                    StartLine = createView.StartLine - 1,
                    StartOffset = createView.StartOffset,
                    EndOffset = createView.StartOffset + createView.FragmentLength,
                    Children = []
                };

            case AlterViewStatement alterView:
                return new OutlineNodeDto
                {
                    Name = ExtractSchemaObjectName(alterView.SchemaObjectName),
                    NodeType = "View",
                    StartLine = alterView.StartLine - 1,
                    StartOffset = alterView.StartOffset,
                    EndOffset = alterView.StartOffset + alterView.FragmentLength,
                    Children = []
                };

            case CreateTriggerStatement createTrigger:
                return new OutlineNodeDto
                {
                    Name = ExtractSchemaObjectName(createTrigger.Name),
                    NodeType = "Trigger",
                    StartLine = createTrigger.StartLine - 1,
                    StartOffset = createTrigger.StartOffset,
                    EndOffset = createTrigger.StartOffset + createTrigger.FragmentLength,
                    Children = CollectChildStatements(createTrigger.StatementList, sql, depth)
                };

            case CreateTableStatement createTable:
                var tableName = ExtractSchemaObjectName(createTable.SchemaObjectName);
                // Detect temp tables (# prefix)
                var isTemp = tableName.Contains('#');
                return new OutlineNodeDto
                {
                    Name = tableName,
                    NodeType = isTemp ? "TempTable" : "Statement",
                    StartLine = createTable.StartLine - 1,
                    StartOffset = createTable.StartOffset,
                    EndOffset = createTable.StartOffset + createTable.FragmentLength,
                    Children = []
                };

            // Detect CTEs inside SELECT statements
            case SelectStatement selectStmt when selectStmt.WithCtesAndXmlNamespaces?.CommonTableExpressions?.Count > 0:
                var cteChildren = new List<OutlineNodeDto>();
                foreach (var cte in selectStmt.WithCtesAndXmlNamespaces.CommonTableExpressions)
                {
                    cteChildren.Add(new OutlineNodeDto
                    {
                        Name = "CTE: " + (cte.ExpressionName?.Value ?? "<unnamed>"),
                        NodeType = "CTE",
                        StartLine = cte.StartLine - 1,
                        StartOffset = cte.StartOffset,
                        EndOffset = cte.StartOffset + cte.FragmentLength,
                        Children = []
                    });
                }
                // Return the CTE children as top-level nodes if there are multiple
                if (cteChildren.Count == 1)
                    return cteChildren[0];
                // For multiple CTEs, wrap them into a block
                return new OutlineNodeDto
                {
                    Name = $"WITH ({cteChildren.Count} CTEs)",
                    NodeType = "Block",
                    StartLine = selectStmt.StartLine - 1,
                    StartOffset = selectStmt.StartOffset,
                    EndOffset = selectStmt.StartOffset + selectStmt.FragmentLength,
                    Children = cteChildren.ToArray()
                };

            case BeginEndBlockStatement beginEnd:
                return new OutlineNodeDto
                {
                    Name = "BEGIN...END",
                    NodeType = "Block",
                    StartLine = beginEnd.StartLine - 1,
                    StartOffset = beginEnd.StartOffset,
                    EndOffset = beginEnd.StartOffset + beginEnd.FragmentLength,
                    Children = CollectChildStatements(beginEnd.StatementList, sql, depth)
                };

            case TryCatchStatement tryCatch:
                var tryEndNode = new OutlineNodeDto
                {
                    Name = "TRY...CATCH",
                    NodeType = "Block",
                    StartLine = tryCatch.StartLine - 1,
                    StartOffset = tryCatch.StartOffset,
                    EndOffset = tryCatch.StartOffset + tryCatch.FragmentLength,
                    Children = []
                };
                var tryChildren = new List<OutlineNodeDto>();
                if (tryCatch.TryStatements?.Statements != null)
                {
                    tryChildren.Add(new OutlineNodeDto
                    {
                        Name = "TRY",
                        NodeType = "Block",
                        StartLine = tryCatch.TryStatements.StartLine - 1,
                        StartOffset = tryCatch.TryStatements.StartOffset,
                        EndOffset = tryCatch.TryStatements.StartOffset + tryCatch.TryStatements.FragmentLength,
                        Children = CollectChildStatements(tryCatch.TryStatements, sql, depth)
                    });
                }
                if (tryCatch.CatchStatements?.Statements != null)
                {
                    tryChildren.Add(new OutlineNodeDto
                    {
                        Name = "CATCH",
                        NodeType = "Block",
                        StartLine = tryCatch.CatchStatements.StartLine - 1,
                        StartOffset = tryCatch.CatchStatements.StartOffset,
                        EndOffset = tryCatch.CatchStatements.StartOffset + tryCatch.CatchStatements.FragmentLength,
                        Children = CollectChildStatements(tryCatch.CatchStatements, sql, depth)
                    });
                }
                tryEndNode.Children = tryChildren.ToArray();
                return tryEndNode;

            default:
                // Skip simple DML/DDL that don't add structural value to the outline
                return null;
        }
    }

    private static OutlineNodeDto BuildProcedureNode(CreateProcedureStatement proc, string sql, string nodeType, int depth)
    {
        return new OutlineNodeDto
        {
            Name = ExtractSchemaObjectName(proc.ProcedureReference?.Name),
            NodeType = nodeType,
            StartLine = proc.StartLine - 1,
            StartOffset = proc.StartOffset,
            EndOffset = proc.StartOffset + proc.FragmentLength,
            Children = CollectChildStatements(proc.StatementList, sql, depth)
        };
    }

    /// <summary>
    /// Collects child outline nodes from a StatementList (the body of a procedure/function/trigger).
    /// Only includes structurally significant statements. Returns an empty array when
    /// <paramref name="depth"/> reaches <see cref="MaxRecursionDepth"/> to prevent stack overflow.
    /// </summary>
    private static OutlineNodeDto[] CollectChildStatements(StatementList? stmtList, string sql, int depth)
    {
        if (depth >= MaxRecursionDepth)
            return [];

        if (stmtList?.Statements == null || stmtList.Statements.Count == 0)
            return [];

        var children = new List<OutlineNodeDto>();
        foreach (var childStmt in stmtList.Statements)
        {
            var childNode = BuildNodeFromStatement(childStmt, sql, depth + 1);
            if (childNode != null)
                children.Add(childNode);
        }
        return children.ToArray();
    }

    /// <summary>
    /// Scans SQL text for --region NAME / --endregion comment pairs and builds outline nodes.
    /// Regions can be nested; unmatched --region lines are ignored.
    /// </summary>
    private static List<OutlineNodeDto> ScanRegions(string sql)
    {
        var result = new List<OutlineNodeDto>();
        var stack = new Stack<(string name, int startLine, int startOffset)>();

        var lines = sql.Split('\n');
        int offset = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            var regionMatch = RegionStartPattern().Match(trimmed);
            if (regionMatch.Success)
            {
                var regionName = regionMatch.Groups[1].Value.Trim();
                // Calculate the offset of --region in the original text
                var lineStartOffset = offset + (line.Length - trimmed.Length);
                stack.Push((regionName, i, lineStartOffset));
            }
            else if (RegionEndPattern().IsMatch(trimmed) && stack.Count > 0)
            {
                var (name, startLine, startOffset) = stack.Pop();
                var endOffset = offset + line.Length;
                result.Add(new OutlineNodeDto
                {
                    Name = "--region " + name,
                    NodeType = "Region",
                    StartLine = startLine,
                    StartOffset = startOffset,
                    EndOffset = endOffset,
                    Children = []
                });
            }

            // +1 for the \n character (which was stripped by Split)
            offset += line.Length + 1;
        }

        return result;
    }

    /// <summary>
    /// Extracts a dotted schema-qualified object name from a <see cref="SchemaObjectName"/>.
    /// </summary>
    private static string ExtractSchemaObjectName(SchemaObjectName? objectName)
    {
        if (objectName == null)
            return "<unknown>";

        var parts = new List<string>(4);
        if (objectName.SchemaIdentifier?.Value != null)
            parts.Add(objectName.SchemaIdentifier.Value);
        if (objectName.BaseIdentifier?.Value != null)
            parts.Add(objectName.BaseIdentifier.Value);

        return parts.Count > 0 ? string.Join(".", parts) : "<unknown>";
    }

    /// <summary>
    /// Extracts a dotted name from a <see cref="ProcedureReference"/>'s Name property.
    /// </summary>
    private static string ExtractSchemaObjectName(ProcedureReferenceName? procName)
    {
        return ExtractSchemaObjectName(procName?.ProcedureReference?.Name);
    }

    [GeneratedRegex(@"^--\s*region\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RegionStartPattern();

    [GeneratedRegex(@"^--\s*endregion", RegexOptions.IgnoreCase)]
    private static partial Regex RegionEndPattern();
}
