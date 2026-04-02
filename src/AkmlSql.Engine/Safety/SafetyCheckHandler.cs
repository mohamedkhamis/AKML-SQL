using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Safety;
using AkmlSql.Engine.Parser;
using MessagePack;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Safety;

/// <summary>
/// Handles MessageType 55 (SafetyCheck) requests from the shell.
/// Parses the SQL text and walks the AST to detect potentially dangerous statements
/// such as DELETE/UPDATE without WHERE, DROP TABLE/DATABASE, and TRUNCATE TABLE.
/// When the target server is flagged as production, additional production-level
/// warnings are emitted for any DML/DDL statements.
/// </summary>
public class SafetyCheckHandler(TsqlParserService parserService)
{
    private readonly TsqlParserService _parserService = parserService ?? throw new ArgumentNullException(nameof(parserService));

    /// <summary>
    /// Analyzes SQL text for execution safety concerns and returns warnings.
    /// </summary>
    public async Task<RpcMessage?> HandleAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateResponse(request.RequestId, new SafetyCheckResponse
                {
                    RequiresConfirmation = false,
                    Warnings = []
                });
            }

            var checkRequest = MessagePackSerializer.Deserialize<SafetyCheckRequest>(request.Payload);

            if (string.IsNullOrWhiteSpace(checkRequest.SqlText))
            {
                return CreateResponse(request.RequestId, new SafetyCheckResponse
                {
                    RequiresConfirmation = false,
                    Warnings = []
                });
            }

            var warnings = await Task.Run(() => AnalyzeSql(checkRequest));

            var response = new SafetyCheckResponse
            {
                RequiresConfirmation = warnings.Length > 0,
                Warnings = warnings
            };

            Log.Debug("SafetyCheck: {WarningCount} warning(s) for server={Server}, isProduction={IsProd}",
                warnings.Length, checkRequest.Server, checkRequest.IsProductionServer);

            return CreateResponse(request.RequestId, response);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SafetyCheckHandler: failed to analyze SQL");

            // On failure, allow execution to proceed (fail-open) rather than blocking the user
            return CreateResponse(request.RequestId, new SafetyCheckResponse
            {
                RequiresConfirmation = false,
                Warnings = []
            });
        }
    }

    /// <summary>
    /// Parses the SQL and walks the AST to detect safety concerns.
    /// </summary>
    private SafetyWarningDto[] AnalyzeSql(SafetyCheckRequest request)
    {
        var script = _parserService.Parse(request.SqlText, out var errors);

        // If the SQL cannot be parsed, we cannot analyze it — allow execution
        if (script == null || script.Batches.Count == 0)
        {
            Log.Debug("SafetyCheckHandler: SQL could not be parsed ({ErrorCount} error(s)), skipping analysis",
                errors?.Count ?? 0);
            return [];
        }

        var warnings = new List<SafetyWarningDto>();
        bool hasDml = false;
        bool hasDdl = false;

        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                AnalyzeStatement(statement, warnings, ref hasDml, ref hasDdl);
            }
        }

        // Production server warnings — add after statement-specific warnings
        if (request.IsProductionServer)
        {
            if (hasDml)
            {
                warnings.Add(new SafetyWarningDto
                {
                    WarningType = (int)SafetyWarningType.ProductionDml,
                    Message = $"You are about to execute DML statements on production server '{request.Server}'. Proceed with caution.",
                    Severity = 1 // Warning
                });
            }

            if (hasDdl)
            {
                warnings.Add(new SafetyWarningDto
                {
                    WarningType = (int)SafetyWarningType.ProductionDdl,
                    Message = $"You are about to execute DDL statements on production server '{request.Server}'. This may affect the database schema.",
                    Severity = 2 // Error
                });
            }
        }

        return warnings.ToArray();
    }

    /// <summary>
    /// Recursively analyzes a single statement for safety concerns.
    /// Sets <paramref name="hasDml"/> or <paramref name="hasDdl"/> flags as appropriate.
    /// </summary>
    private static void AnalyzeStatement(TSqlStatement statement, List<SafetyWarningDto> warnings,
        ref bool hasDml, ref bool hasDdl)
    {
        switch (statement)
        {
            case DeleteStatement deleteStmt:
                hasDml = true;
                if (deleteStmt.DeleteSpecification?.WhereClause == null)
                {
                    var tableName = ExtractTargetTableName(deleteStmt.DeleteSpecification?.Target);
                    warnings.Add(new SafetyWarningDto
                    {
                        WarningType = (int)SafetyWarningType.DeleteWithoutWhere,
                        Message = $"DELETE statement without a WHERE clause will delete ALL rows{(tableName != null ? $" from '{tableName}'" : "")}.",
                        ObjectName = tableName,
                        Severity = 2 // Error
                    });
                }
                break;

            case UpdateStatement updateStmt:
                hasDml = true;
                if (updateStmt.UpdateSpecification?.WhereClause == null)
                {
                    var tableName = ExtractTargetTableName(updateStmt.UpdateSpecification?.Target);
                    warnings.Add(new SafetyWarningDto
                    {
                        WarningType = (int)SafetyWarningType.UpdateWithoutWhere,
                        Message = $"UPDATE statement without a WHERE clause will update ALL rows{(tableName != null ? $" in '{tableName}'" : "")}.",
                        ObjectName = tableName,
                        Severity = 2 // Error
                    });
                }
                break;

            case DropTableStatement dropTableStmt:
                hasDdl = true;
                foreach (var obj in dropTableStmt.Objects)
                {
                    var objectName = ExtractSchemaObjectName(obj);
                    warnings.Add(new SafetyWarningDto
                    {
                        WarningType = (int)SafetyWarningType.DropTable,
                        Message = $"DROP TABLE will permanently remove table '{objectName}' and all its data.",
                        ObjectName = objectName,
                        Severity = 2 // Error
                    });
                }
                break;

            case DropDatabaseStatement dropDbStmt:
                hasDdl = true;
                foreach (var dbId in dropDbStmt.Databases)
                {
                    var dbName = dbId.Value;
                    warnings.Add(new SafetyWarningDto
                    {
                        WarningType = (int)SafetyWarningType.DropDatabase,
                        Message = $"DROP DATABASE will permanently remove database '{dbName}' and all its data.",
                        ObjectName = dbName,
                        Severity = 2 // Error
                    });
                }
                break;

            case TruncateTableStatement truncateStmt:
                hasDml = true;
                var truncTableName = ExtractSchemaObjectName(truncateStmt.TableName);
                warnings.Add(new SafetyWarningDto
                {
                    WarningType = (int)SafetyWarningType.TruncateTable,
                    Message = $"TRUNCATE TABLE will remove ALL rows from '{truncTableName}'. This operation cannot be rolled back without an explicit transaction.",
                    ObjectName = truncTableName,
                    Severity = 1 // Warning
                });
                break;

            // Track DML/DDL for production warning even when no specific safety issue
            case InsertStatement:
            case MergeStatement:
                hasDml = true;
                break;

            case AlterTableStatement:
            case CreateTableStatement:
            case CreateProcedureStatement:
            case AlterProcedureStatement:
            case CreateViewStatement:
            case AlterViewStatement:
            case CreateFunctionStatement:
            case AlterFunctionStatement:
            case CreateIndexStatement:
            case DropIndexStatement:
            case DropProcedureStatement:
            case DropViewStatement:
            case DropFunctionStatement:
                hasDdl = true;
                break;

            // Recurse into BEGIN...END blocks
            case BeginEndBlockStatement beginEndBlock:
                foreach (var innerStmt in beginEndBlock.StatementList.Statements)
                {
                    AnalyzeStatement(innerStmt, warnings, ref hasDml, ref hasDdl);
                }
                break;

            // Recurse into IF...ELSE
            case IfStatement ifStmt:
                if (ifStmt.ThenStatement != null)
                    AnalyzeStatement(ifStmt.ThenStatement, warnings, ref hasDml, ref hasDdl);
                if (ifStmt.ElseStatement != null)
                    AnalyzeStatement(ifStmt.ElseStatement, warnings, ref hasDml, ref hasDdl);
                break;

            // Recurse into WHILE loops
            case WhileStatement whileStmt:
                if (whileStmt.Statement != null)
                    AnalyzeStatement(whileStmt.Statement, warnings, ref hasDml, ref hasDdl);
                break;

            // Recurse into TRY...CATCH
            case TryCatchStatement tryCatch:
                if (tryCatch.TryStatements?.Statements != null)
                {
                    foreach (var s in tryCatch.TryStatements.Statements)
                        AnalyzeStatement(s, warnings, ref hasDml, ref hasDdl);
                }
                if (tryCatch.CatchStatements?.Statements != null)
                {
                    foreach (var s in tryCatch.CatchStatements.Statements)
                        AnalyzeStatement(s, warnings, ref hasDml, ref hasDdl);
                }
                break;
        }
    }

    /// <summary>
    /// Extracts a fully-qualified name from a <see cref="SchemaObjectName"/> (e.g. "dbo.MyTable").
    /// </summary>
    private static string ExtractSchemaObjectName(SchemaObjectName? objectName)
    {
        if (objectName == null)
            return "<unknown>";

        var parts = new List<string>(4);
        if (objectName.ServerIdentifier?.Value != null)
            parts.Add(objectName.ServerIdentifier.Value);
        if (objectName.DatabaseIdentifier?.Value != null)
            parts.Add(objectName.DatabaseIdentifier.Value);
        if (objectName.SchemaIdentifier?.Value != null)
            parts.Add(objectName.SchemaIdentifier.Value);
        if (objectName.BaseIdentifier?.Value != null)
            parts.Add(objectName.BaseIdentifier.Value);

        return parts.Count > 0 ? string.Join(".", parts) : "<unknown>";
    }

    /// <summary>
    /// Extracts the table name from a <see cref="TableReference"/> used as a DML target.
    /// </summary>
    private static string? ExtractTargetTableName(Microsoft.SqlServer.TransactSql.ScriptDom.TableReference? target)
    {
        return target switch
        {
            NamedTableReference namedTable => ExtractSchemaObjectName(namedTable.SchemaObject),
            _ => null
        };
    }

    private static RpcMessage CreateResponse(int requestId, SafetyCheckResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.SafetyCheckResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }
}
