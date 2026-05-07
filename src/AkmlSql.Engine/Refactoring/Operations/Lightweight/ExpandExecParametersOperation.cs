using System.Text;
using AkmlSql.Core.Config;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Converts positional EXEC parameters to named (@param = value) form
/// using procedure parameter names from the schema cache.
/// </summary>
public class ExpandExecParametersOperation : ILightweightOperation
{
    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            var script = context.Script;
            if (script == null)
                return (context.DocumentText, []);

            var collector = new ExecuteStatementCollector();
            script.Accept(collector);

            if (collector.Statements.Count == 0)
                return (context.DocumentText, []);

            // IntelliSense.InsertOptions.IncludeProcParamInfo gates this op.
            // When false, leave EXEC statements untouched (no warning — opted out).
            if (!ResolveInsertOptions(context).IncludeProcParamInfo)
                return (context.DocumentText, []);

            var warnings = new List<string>();

            // Only process statements that have positional (unnamed) parameters
            var candidates = collector.Statements
                .Where(HasPositionalParameters)
                .OrderByDescending(s => s.StartOffset)
                .ToList();

            if (candidates.Count == 0)
                return (context.DocumentText, []);

            var text = context.DocumentText;

            foreach (var exec in candidates)
            {
                if (context.HasSelection)
                {
                    if (exec.StartOffset < context.SelectionStart ||
                        exec.StartOffset >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                var spec = exec.ExecuteSpecification;
                if (spec == null) continue;

                var execEntity = spec.ExecutableEntity as ExecutableProcedureReference;
                if (execEntity == null) continue;

                // API: ExecutableProcedureReference → ProcedureReference (ProcedureReferenceName)
                //      ProcedureReferenceName.ProcedureReference → ProcedureReference
                //      ProcedureReference.Name → SchemaObjectName
                var procRefName = execEntity.ProcedureReference;
                if (procRefName?.ProcedureReference == null) continue;

                var schemaObj = procRefName.ProcedureReference.Name;
                if (schemaObj == null) continue;

                var schemaName = schemaObj.SchemaIdentifier?.Value ?? "dbo";
                var procName   = schemaObj.BaseIdentifier?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(procName)) continue;

                // ExecutableProcedureReference contains the parameter list
                var execParams = execEntity.Parameters;
                if (execParams == null || execParams.Count == 0) continue;

                if (context.SchemaCache == null)
                {
                    warnings.Add($"Could not resolve parameters for: {schemaName}.{procName}");
                    continue;
                }

                var dbObj = context.SchemaCache.FindObject(schemaName, procName);
                if (dbObj == null || dbObj.Parameters.Count == 0)
                {
                    warnings.Add($"Could not resolve parameters for: {schemaName}.{procName}");
                    continue;
                }

                // Build named parameter list
                var sb = new StringBuilder();
                for (int i = 0; i < execParams.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var paramName = i < dbObj.Parameters.Count
                        ? dbObj.Parameters[i].ParameterName
                        : $"@param{i + 1}";

                    // Ensure it starts with @
                    if (!paramName.StartsWith("@"))
                        paramName = "@" + paramName;

                    var param     = execParams[i];
                    var valueText = text.Substring(param.StartOffset, param.FragmentLength);
                    sb.Append($"{paramName} = {valueText}");
                }

                // Replace the parameter region in text
                var firstParam = execParams.OrderBy(p => p.StartOffset).First();
                var lastParam  = execParams.OrderByDescending(p => p.StartOffset).First();
                var regionStart = firstParam.StartOffset;
                var regionEnd   = lastParam.StartOffset + lastParam.FragmentLength;

                text = text[..regionStart] + sb.ToString() + text[regionEnd..];
            }

            return (text, [.. warnings]);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }

    private static bool HasPositionalParameters(ExecuteStatement exec)
    {
        var spec = exec.ExecuteSpecification;
        if (spec?.ExecutableEntity is not ExecutableProcedureReference procRef) return false;
        var execParams = procRef.Parameters;
        if (execParams == null || execParams.Count == 0) return false;
        // Positional = no variable assignment (Variable == null means it's positional)
        return execParams.All(p => p.Variable == null);
    }

    private sealed class ExecuteStatementCollector : TSqlFragmentVisitor
    {
        public List<ExecuteStatement> Statements { get; } = [];
        public override void Visit(ExecuteStatement node)
        {
            Statements.Add(node);
        }
    }

    private static InsertOptionsSettings ResolveInsertOptions(RefactoringContext context)
    {
        if (context.IntelliSense != null) return context.IntelliSense.InsertOptions;
        try { return ConfigManager.Load().IntelliSense.InsertOptions; }
        catch { return new InsertOptionsSettings(); }
    }
}
