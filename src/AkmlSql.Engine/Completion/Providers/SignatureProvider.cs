using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// T061-T062: Provides function signature help (not an ICompletionProvider).
/// Resolves function signatures from BuiltinFunctionDictionary and schema cache,
/// and tracks active parameter index via comma counting.
/// </summary>
public class SignatureProvider
{
    /// <summary>
    /// T061: Get signature help for a function at the cursor position.
    /// Checks BuiltinFunctionDictionary first, then falls back to schema cache for user procs/functions.
    /// </summary>
    public SignatureResponse GetSignature(string functionName, int parameterIndex, DatabaseCache? cache)
    {
        var response = new SignatureResponse
        {
            FunctionName = functionName,
            ActiveParameter = parameterIndex
        };

        // Check built-in functions first
        if (BuiltinFunctionDictionary.TryGetSignature(functionName, out var builtinOverloads))
        {
            response.Overloads = builtinOverloads;
            response.ActiveOverload = FindBestOverload(builtinOverloads, parameterIndex);
            return response;
        }

        // Fall back to schema cache for user-defined procedures/functions
        if (cache != null)
        {
            var overloads = GetSchemaOverloads(functionName, cache);
            if (overloads.Length > 0)
            {
                response.Overloads = overloads;
                response.ActiveOverload = 0;
                return response;
            }
        }

        Log.Debug("SignatureProvider: no signature found for {Function}", functionName);
        return response;
    }

    /// <summary>
    /// T062: Count commas before the cursor position to determine which parameter is active.
    /// Scans from the open parenthesis forward, counting commas at the same nesting level.
    /// </summary>
    public static int CountCommasBeforeCursor(IList<TSqlParserToken> tokens, int cursorOffset, int openParenOffset)
    {
        int commaCount = 0;
        int depth = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            // Skip tokens before the opening paren
            if (token.Offset < openParenOffset)
            {
                continue;
            }

            // Stop at or after cursor
            if (token.Offset >= cursorOffset)
            {
                break;
            }

            switch (token.TokenType)
            {
                case TSqlTokenType.LeftParenthesis:
                    depth++;
                    break;

                case TSqlTokenType.RightParenthesis:
                    depth--;
                    if (depth <= 0)
                    {
                        return commaCount; // Closed the function call
                    }

                    break;

                case TSqlTokenType.Comma:
                    if (depth == 1) // Only count commas at the immediate function level
                    {
                        commaCount++;
                    }

                    break;
            }
        }

        return commaCount;
    }

    /// <summary>
    /// Find the best-matching overload based on the current parameter index.
    /// Prefers overloads where the parameter index is within the non-optional range.
    /// </summary>
    private static int FindBestOverload(SignatureOverload[] overloads, int parameterIndex)
    {
        // Find the first overload that has enough parameters
        for (int i = 0; i < overloads.Length; i++)
        {
            var overload = overloads[i];
            if (parameterIndex < overload.Parameters.Length)
            {
                return i;
            }
        }

        // If no overload fits, return the last one (most parameters)
        return overloads.Length > 0 ? overloads.Length - 1 : 0;
    }

    /// <summary>
    /// Build signature overloads from schema cache parameters for user procs/functions.
    /// </summary>
    private static SignatureOverload[] GetSchemaOverloads(string functionName, DatabaseCache cache)
    {
        // Try to find a matching object across all schemas
        foreach (var schema in cache.Schemas.Values)
        {
            foreach (var obj in schema.Objects)
            {
                if (!obj.ObjectName.Equals(functionName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (obj.ObjectType is not (DbObjectType.Procedure or DbObjectType.ScalarFunction
                    or DbObjectType.TableFunction or DbObjectType.InlineFunction))
                {
                    continue;
                }

                if (obj.Parameters.Count == 0)
                {
                    continue;
                }

                var label = $"{obj.ObjectName}({string.Join(", ", obj.Parameters.Select(p => $"{p.ParameterName} {p.TypeName}"))})";
                var parameters = obj.Parameters.Select(p => new ParameterInfo
                {
                    Name = p.ParameterName,
                    Type = p.TypeName,
                    Documentation = p.IsOutput ? "OUTPUT parameter" : string.Empty,
                    IsOptional = p.HasDefault
                }).ToArray();

                return
                [
                    new SignatureOverload
                    {
                        Label = label,
                        Documentation = obj.Description ?? $"{obj.ObjectType}: {obj.FullName}",
                        Parameters = parameters
                    }
                ];
            }
        }

        return [];
    }
}
