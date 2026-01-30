using System.Text;
using System.Text.Json;
using AKML.SQL.Shared.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AKML.SQL.Core.Services;

/// <summary>
/// SQL parsing service using Microsoft.SqlServer.TransactSql.ScriptDom.
/// Handles T-SQL parsing, AST generation, and formatting.
/// </summary>
public class SqlParserService : ISqlParserService
{
    private readonly ILogger<SqlParserService> _logger;
    private readonly TSql160Parser _parser;

    public SqlParserService(ILogger<SqlParserService> logger)
    {
        _logger = logger;
        // Use SQL Server 2022 (160) parser - supports all modern T-SQL features
        _parser = new TSql160Parser(initialQuotedIdentifiers: true);
    }

    /// <summary>
    /// Parse SQL text and return parse result with statements and errors.
    /// </summary>
    public Task<ParseResult> ParseAsync(string? sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Task.FromResult(new ParseResult { Success = true });
        }

        var result = new ParseResult();

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out var errors);

            // Convert errors
            if (errors?.Count > 0)
            {
                foreach (var error in errors)
                {
                    result.Errors.Add(new ParseError
                    {
                        Number = error.Number,
                        Message = error.Message,
                        Line = error.Line - 1, // Convert to 0-based
                        Column = error.Column - 1,
                        Offset = error.Offset
                    });
                }
            }

            // Extract statements
            if (fragment is TSqlScript script)
            {
                foreach (var batch in script.Batches)
                {
                    foreach (var statement in batch.Statements)
                    {
                        var stmtInfo = ExtractStatementInfo(statement, sql);
                        result.Statements.Add(stmtInfo);
                    }
                }
            }

            result.Success = result.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing SQL");
            result.Errors.Add(new ParseError
            {
                Number = -1,
                Message = $"Parse error: {ex.Message}",
                Line = 0,
                Column = 0
            });
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Format SQL using ScriptDom's script generator.
    /// </summary>
    public Task<string> FormatAsync(string? sql, FormatOptions? options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Task.FromResult(string.Empty);
        }

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out var errors);

            if (errors?.Count > 0)
            {
                // Return original if there are parse errors
                _logger.LogWarning("Cannot format SQL with parse errors: {ErrorCount} errors", errors.Count);
                return Task.FromResult(sql);
            }

            // Configure generator options
            var generatorOptions = new SqlScriptGeneratorOptions
            {
                AlignClauseBodies = true,
                AlignColumnDefinitionFields = true,
                AlignSetClauseItem = true,
                AsKeywordOnOwnLine = false,
                IncludeSemicolons = true,
                IndentationSize = options?.IndentSize ?? 4,
                IndentSetClause = true,
                IndentViewBody = true,
                KeywordCasing = ConvertKeywordCasing(options?.KeywordCasing),
                MultilineInsertSourcesList = true,
                MultilineInsertTargetsList = true,
                MultilineSelectElementsList = true,
                MultilineSetClauseItems = true,
                MultilineViewColumnsList = true,
                MultilineWherePredicatesList = true,
                NewLineBeforeCloseParenthesisInMultilineList = true,
                NewLineBeforeFromClause = true,
                NewLineBeforeGroupByClause = true,
                NewLineBeforeHavingClause = true,
                NewLineBeforeJoinClause = true,
                NewLineBeforeOffsetClause = true,
                NewLineBeforeOpenParenthesisInMultilineList = false,
                NewLineBeforeOrderByClause = true,
                NewLineBeforeOutputClause = true,
                NewLineBeforeWhereClause = true,
                SqlVersion = SqlVersion.Sql160
            };

            // Generate formatted SQL
            var generator = new Sql160ScriptGenerator(generatorOptions);
            generator.GenerateScript(fragment, out var formattedSql);

            return Task.FromResult(formattedSql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting SQL");
            return Task.FromResult(sql);
        }
    }

    private SqlStatement ExtractStatementInfo(TSqlStatement statement, string fullSql)
    {
        var info = new SqlStatement
        {
            Type = GetStatementType(statement),
            StartOffset = statement.StartOffset,
            StartLine = statement.StartLine - 1,
            EndLine = statement.StartLine - 1 // Will be calculated
        };

        // Calculate end offset and line
        if (statement.ScriptTokenStream?.Count > 0)
        {
            var lastToken = statement.ScriptTokenStream[statement.LastTokenIndex];
            info.EndOffset = lastToken.Offset + lastToken.Text.Length;
            info.EndLine = lastToken.Line - 1;
        }

        // Extract table references
        var tableVisitor = new TableReferenceVisitor();
        statement.Accept(tableVisitor);
        info.Tables.AddRange(tableVisitor.Tables);

        // Extract column references
        var columnVisitor = new ColumnReferenceVisitor();
        statement.Accept(columnVisitor);
        info.Columns.AddRange(columnVisitor.Columns);

        return info;
    }

    private static SqlStatementType GetStatementType(TSqlStatement statement)
    {
        return statement switch
        {
            SelectStatement => SqlStatementType.Select,
            InsertStatement => SqlStatementType.Insert,
            UpdateStatement => SqlStatementType.Update,
            DeleteStatement => SqlStatementType.Delete,
            CreateTableStatement or CreateViewStatement or CreateProcedureStatement 
                or CreateFunctionStatement or CreateIndexStatement => SqlStatementType.Create,
            AlterTableStatement or AlterViewStatement or AlterProcedureStatement 
                or AlterFunctionStatement => SqlStatementType.Alter,
            DropTableStatement or DropViewStatement or DropProcedureStatement 
                or DropFunctionStatement => SqlStatementType.Drop,
            ExecuteStatement => SqlStatementType.Exec,
            DeclareVariableStatement => SqlStatementType.Declare,
            SetVariableStatement => SqlStatementType.Set,
            IfStatement => SqlStatementType.If,
            WhileStatement => SqlStatementType.While,
            BeginEndBlockStatement => SqlStatementType.Begin,
            MergeStatement => SqlStatementType.Merge,
            _ => SqlStatementType.Other
        };
    }

    private static Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing ConvertKeywordCasing(AKML.SQL.Shared.Contracts.KeywordCasing? casing)
    {
        return casing switch
        {
            AKML.SQL.Shared.Contracts.KeywordCasing.Uppercase => Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.Uppercase,
            AKML.SQL.Shared.Contracts.KeywordCasing.Lowercase => Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.Lowercase,
            AKML.SQL.Shared.Contracts.KeywordCasing.PascalCase => Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.PascalCase,
            _ => Microsoft.SqlServer.TransactSql.ScriptDom.KeywordCasing.Uppercase
        };
    }

    /// <summary>
    /// Visitor to extract table references from SQL AST.
    /// </summary>
    private class TableReferenceVisitor : TSqlFragmentVisitor
    {
        public List<AKML.SQL.Shared.Contracts.TableReference> Tables { get; } = new();

        public override void Visit(NamedTableReference node)
        {
            var table = new AKML.SQL.Shared.Contracts.TableReference
            {
                TableName = node.SchemaObject.BaseIdentifier?.Value ?? string.Empty,
                SchemaName = node.SchemaObject.SchemaIdentifier?.Value ?? "dbo",
                Alias = node.Alias?.Value ?? string.Empty,
                StartOffset = node.StartOffset,
                EndOffset = node.StartOffset + node.FragmentLength
            };
            Tables.Add(table);
            base.Visit(node);
        }
    }

    /// <summary>
    /// Visitor to extract column references from SQL AST.
    /// </summary>
    private class ColumnReferenceVisitor : TSqlFragmentVisitor
    {
        public List<AKML.SQL.Shared.Contracts.ColumnReference> Columns { get; } = new();

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.ColumnType == ColumnType.Regular && node.MultiPartIdentifier?.Identifiers?.Count > 0)
            {
                var identifiers = node.MultiPartIdentifier.Identifiers;
                var column = new AKML.SQL.Shared.Contracts.ColumnReference
                {
                    ColumnName = identifiers[identifiers.Count - 1].Value,
                    TableAlias = identifiers.Count > 1 ? identifiers[identifiers.Count - 2].Value : string.Empty,
                    StartOffset = node.StartOffset,
                    EndOffset = node.StartOffset + node.FragmentLength
                };
                Columns.Add(column);
            }
            base.Visit(node);
        }
    }
}
