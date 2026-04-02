using System.Text.RegularExpressions;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Snippets.Models;

namespace AkmlSql.Engine.Snippets;

public static class PlaceholderParser
{
    // Matches $VarName$ but NOT built-in variables like $CURSOR$, $DATE$, etc.
    private static readonly Regex PlaceholderRegex = new(@"\$([A-Za-z_]\w*)\$", RegexOptions.Compiled);

    private static readonly HashSet<string> BuiltInVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "CURSOR", "SELECTEDTEXT", "CLIPBOARD", "DATE", "DATETIME", "TIME",
        "USER", "MACHINE", "DATABASE", "SERVER", "SCHEMA", "GUID", "YEAR", "FILENAME"
    };

    public static List<PlaceholderInfo> Parse(string expandedText, SnippetVariable[] variables)
    {
        var results = new List<PlaceholderInfo>();
        var groupIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int nextGroup = 0;

        foreach (Match match in PlaceholderRegex.Matches(expandedText))
        {
            var varName = match.Groups[1].Value;
            if (BuiltInVariables.Contains(varName))
                continue; // Built-in vars are already resolved

            if (!groupIndexMap.TryGetValue(varName, out var groupIndex))
            {
                groupIndex = nextGroup++;
                groupIndexMap[varName] = groupIndex;
            }

            var variable = variables.FirstOrDefault(v =>
                v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));

            results.Add(new PlaceholderInfo
            {
                VariableName = varName,
                Offset = match.Index,
                Length = match.Length,
                DefaultText = variable?.Default ?? varName,
                SchemaAwareType = variable?.SchemaAware,
                GroupIndex = groupIndex
            });
        }

        return results;
    }

    public static int FindCursorOffset(string text)
    {
        var idx = text.IndexOf("$CURSOR$", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? idx : -1;
    }

    public static string ReplacePlaceholdersWithDefaults(string text, SnippetVariable[] variables)
    {
        return PlaceholderRegex.Replace(text, match =>
        {
            var varName = match.Groups[1].Value;
            if (BuiltInVariables.Contains(varName))
                return match.Value; // Don't replace built-in vars here
            var variable = variables.FirstOrDefault(v =>
                v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
            return variable?.Default ?? varName;
        });
    }
}
