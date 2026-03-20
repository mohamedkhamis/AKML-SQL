using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Parser;

/// <summary>
/// T073: Walks AST for DECLARE @name type statements to extract variable declarations.
/// Returns a dictionary of variable name → type name.
/// </summary>
public class VariableTracker
{
    /// <summary>
    /// Track all declared variables in the script visible at the cursor offset.
    /// Returns a dictionary of variable name (including @) → type name.
    /// </summary>
    public Dictionary<string, string> TrackVariables(TSqlScript? script, int cursorOffset)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (script == null)
        {
            return result;
        }

        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
            {
                continue;
            }

            var visitor = new VariableVisitor(cursorOffset);
            batch.Accept(visitor);

            foreach (var (name, typeName) in visitor.Variables)
                result[name] = typeName;
        }

        return result;
    }

    private class VariableVisitor : TSqlFragmentVisitor
    {
        private readonly int _cursorOffset;
        public List<(string Name, string TypeName)> Variables { get; } = [];

        public VariableVisitor(int cursorOffset)
        {
            _cursorOffset = cursorOffset;
        }

        /// <summary>
        /// Handle DECLARE @var type, @var2 type2 statements.
        /// </summary>
        public override void Visit(DeclareVariableElement node)
        {
            // Only include declarations that appear before the cursor
            if (node.StartOffset > _cursorOffset)
            {
                return;
            }

            var varName = node.VariableName?.Value;
            if (string.IsNullOrEmpty(varName))
            {
                return;
            }

            // Ensure @ prefix
            if (!varName.StartsWith("@"))
            {
                varName = "@" + varName;
            }

            var typeName = FormatDataType(node.DataType);
            Variables.Add((varName, typeName));
        }

        /// <summary>
        /// Format a DataTypeReference to a display string.
        /// </summary>
        private static string FormatDataType(DataTypeReference? dataType)
        {
            if (dataType == null)
            {
                return "unknown";
            }

            return dataType switch
            {
                SqlDataTypeReference sqlType => FormatSqlDataType(sqlType),
                UserDataTypeReference userType => userType.Name?.BaseIdentifier?.Value ?? "unknown",
                XmlDataTypeReference => "xml",
                _ => dataType.ToString() ?? "unknown"
            };
        }

        private static string FormatSqlDataType(SqlDataTypeReference sqlType)
        {
            var name = sqlType.SqlDataTypeOption.ToString().ToLowerInvariant();

            if (sqlType.Parameters == null || sqlType.Parameters.Count == 0)
            {
                return name;
            }

            if (sqlType.Parameters.Count == 1)
            {
                var p = sqlType.Parameters[0];
                var val = p is MaxLiteral ? "MAX" : p.Value;
                return $"{name}({val})";
            }

            if (sqlType.Parameters.Count == 2)
            {
                return $"{name}({sqlType.Parameters[0].Value},{sqlType.Parameters[1].Value})";
            }

            return name;
        }
    }
}
