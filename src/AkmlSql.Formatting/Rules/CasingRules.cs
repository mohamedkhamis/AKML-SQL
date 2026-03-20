using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Applies all casing rules to layout nodes: reservedKeywords, builtInFunctions,
/// builtInDataTypes, systemObjects, globalVariables, localVariables, identifiers.
/// Delegates keyword/function/dataType casing to CasingEngine and applies additional
/// rules for system objects, global variables, and identifiers.
/// </summary>
public class CasingRules : IRuleSet
{
    // Well-known system stored procedures and functions
    private static readonly HashSet<string> SystemObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "sp_executesql", "sp_helptext", "sp_help", "sp_who", "sp_who2",
        "sp_lock", "sp_columns", "sp_tables", "sp_databases",
        "sp_rename", "sp_depends", "sp_helpdb", "sp_helpindex",
        "sp_configure", "sp_addrolemember", "sp_droprolemember",
        "sp_addlinkedserver", "sp_droplinkedserver",
        "sp_addlinkedsrvlogin", "sp_droplinkedsrvlogin",
        "sp_addsrvrolemember", "sp_dropsrvrolemember",
        "sp_addextendedproc", "sp_dropextendedproc",
        "sp_msforeachtable", "sp_msforeachdb",
        "sp_spaceused", "sp_updatestats", "sp_recompile",
        "xp_cmdshell", "xp_logininfo", "xp_msver",
        "sys.objects", "sys.tables", "sys.columns", "sys.indexes",
        "sys.types", "sys.schemas", "sys.databases", "sys.views",
        "sys.procedures", "sys.parameters", "sys.foreign_keys",
        "sys.key_constraints", "sys.check_constraints",
        "sys.default_constraints", "sys.dm_exec_requests",
        "sys.dm_exec_sessions", "sys.dm_exec_query_stats",
        "sys.dm_exec_cached_plans", "sys.dm_os_wait_stats",
        "sys.dm_db_index_usage_stats", "sys.dm_db_index_physical_stats",
        "INFORMATION_SCHEMA.TABLES", "INFORMATION_SCHEMA.COLUMNS",
        "INFORMATION_SCHEMA.ROUTINES", "INFORMATION_SCHEMA.VIEWS",
        "INFORMATION_SCHEMA.KEY_COLUMN_USAGE",
        "INFORMATION_SCHEMA.TABLE_CONSTRAINTS",
    };

    // Global variables (@@variables)
    private static readonly HashSet<string> GlobalVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "@@ROWCOUNT", "@@ERROR", "@@IDENTITY", "@@TRANCOUNT",
        "@@FETCH_STATUS", "@@CURSOR_ROWS", "@@VERSION",
        "@@SERVERNAME", "@@SERVICENAME", "@@SPID",
        "@@LANGUAGE", "@@DATEFIRST", "@@DBTS",
        "@@MAX_CONNECTIONS", "@@MAX_PRECISION",
        "@@NESTLEVEL", "@@OPTIONS", "@@PROCID",
        "@@REMSERVER", "@@CONNECTIONS", "@@CPU_BUSY",
        "@@IDLE", "@@IO_BUSY", "@@PACK_RECEIVED",
        "@@PACK_SENT", "@@PACKET_ERRORS", "@@TIMETICKS",
        "@@TOTAL_ERRORS", "@@TOTAL_READ", "@@TOTAL_WRITE",
        "@@LOCK_TIMEOUT", "@@TEXTSIZE",
    };

    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        // First, delegate to CasingEngine for keywords, functions, and data types
        var engine = new CasingEngine();
        engine.ApplyCasing(nodes, profile);

        // Then apply additional casing for system objects, global variables
        ApplyExtendedCasing(nodes, profile.Casing);
    }

    private static void ApplyExtendedCasing(List<LayoutNode> nodes, CasingOptions casing)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            // Skip non-textual tokens
            if (node.TokenType == TSqlTokenType.SingleLineComment ||
                node.TokenType == TSqlTokenType.MultilineComment ||
                node.TokenType == TSqlTokenType.AsciiStringLiteral ||
                node.TokenType == TSqlTokenType.UnicodeStringLiteral ||
                node.TokenType == TSqlTokenType.QuotedIdentifier)
                continue;

            // Global variables (@@xxx)
            if (node.TokenType == TSqlTokenType.Variable &&
                node.FormattedText.StartsWith("@@", StringComparison.Ordinal))
            {
                if (casing.GlobalVariables != "AsIs" && GlobalVariables.Contains(node.FormattedText))
                {
                    node.FormattedText = ApplyCasing(node.FormattedText, casing.GlobalVariables);
                }
                continue;
            }

            // Local variables (@xxx) — skip the @ prefix when applying casing
            if (node.TokenType == TSqlTokenType.Variable &&
                node.FormattedText.StartsWith("@", StringComparison.Ordinal) &&
                !node.FormattedText.StartsWith("@@", StringComparison.Ordinal))
            {
                if (casing.LocalVariables != "AsIs")
                {
                    var prefix = "@";
                    var name = node.FormattedText[1..];
                    node.FormattedText = prefix + ApplyCasing(name, casing.LocalVariables);
                }
                continue;
            }

            // System objects: check if identifier matches a known system object (simple name part)
            if (node.TokenType == TSqlTokenType.Identifier && casing.SystemObjects != "AsIs")
            {
                // Check the full qualified name by looking at dot-separated sequences
                var fullName = BuildQualifiedName(nodes, i);
                if (SystemObjects.Contains(fullName))
                {
                    node.FormattedText = ApplyCasing(node.FormattedText, casing.SystemObjects);
                }
                else if (IsSystemObjectPart(node.FormattedText))
                {
                    node.FormattedText = ApplyCasing(node.FormattedText, casing.SystemObjects);
                }
            }
        }
    }

    /// <summary>
    /// Builds a dot-separated qualified name starting from the given index.
    /// Looks forward through dot-separated identifiers: sys.objects, INFORMATION_SCHEMA.TABLES, etc.
    /// </summary>
    private static string BuildQualifiedName(List<LayoutNode> nodes, int startIndex)
    {
        var parts = new List<string> { nodes[startIndex].FormattedText };

        int i = startIndex + 1;
        while (i + 1 < nodes.Count &&
               nodes[i].TokenType == TSqlTokenType.Dot &&
               nodes[i + 1].TokenType == TSqlTokenType.Identifier)
        {
            parts.Add(nodes[i + 1].FormattedText);
            i += 2;
        }

        // Also look backward for schema prefix
        int j = startIndex - 1;
        while (j >= 1 &&
               nodes[j].TokenType == TSqlTokenType.Dot &&
               nodes[j - 1].TokenType == TSqlTokenType.Identifier)
        {
            parts.Insert(0, nodes[j - 1].FormattedText);
            j -= 2;
        }

        return string.Join(".", parts);
    }

    /// <summary>
    /// Checks if an identifier is a known system object name fragment
    /// (e.g., "sys", "INFORMATION_SCHEMA", well-known sp_ or xp_ prefixes).
    /// </summary>
    private static bool IsSystemObjectPart(string text)
    {
        if (text.Equals("sys", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("xp_", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string ApplyCasing(string text, string mode)
    {
        return CasingEngine.ApplyCasingMode(text, mode);
    }
}
