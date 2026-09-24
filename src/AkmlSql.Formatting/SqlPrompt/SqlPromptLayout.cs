using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt.Layout;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>
/// The layout stage for styles written in SQL Prompt's model: lays a parsed script out from a
/// <see cref="SqlPromptStyle"/>, option by option. It replaces the rule-based layout stage for
/// such styles; parsing, casing, formatting-off regions, semantic validation and the idempotency
/// check stay the shared pipeline stages.
/// </summary>
internal static class SqlPromptLayout
{
    public static string Layout(TSqlScript script, IList<TSqlParserToken> tokens, List<NoformatRegion> noformat, SqlPromptStyle style)
    {
        var noFormatToken = new bool[tokens.Count];
        var cased = new string?[tokens.Count];
        var nodes = new List<LayoutNode>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (noformat.Count > 0 && NoformatScanner.IsInNoformatRegion(noformat, t.Offset)) noFormatToken[i] = true;
            if (t.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile) continue;
            nodes.Add(new LayoutNode
            {
                TokenIndex = i,
                TokenType = t.TokenType,
                OriginalText = t.Text,
                FormattedText = t.Text,
                IsInNoformatRegion = noFormatToken[i],
            });
        }

        new CasingEngine().ApplyCasing(nodes, CasingProfile(style));
        foreach (var node in nodes) cased[node.TokenIndex] = node.FormattedText;

        var writer = new SqlWriter(tokens, cased, style);
        new SqlPromptPrinter(writer, style, i => noFormatToken[i]).Script(script);
        return writer.ToText();
    }

    /// <summary>The casing engine's settings for the four SQL Prompt casing options; everything else as written.</summary>
    private static FormattingProfile CasingProfile(SqlPromptStyle style)
    {
        var profile = new FormattingProfile();
        var c = profile.Casing;
        c.ReservedKeywords = Map(style.Casing.ReservedKeywords);
        c.BuiltInFunctions = Map(style.Casing.BuiltInFunctions);
        c.BuiltInDataTypes = Map(style.Casing.BuiltInDataTypes);
        c.GlobalVariables = Map(style.Casing.GlobalVariables);
        c.SystemObjects = "AsIs";
        c.LocalVariables = "AsIs";
        c.Identifiers = "AsIs";
        c.SyncWithDatabase = false;
        c.CamelCaseDictionary = false;
        return profile;
    }

    private static string Map(string sqlPromptCasing) => sqlPromptCasing switch
    {
        "uppercase" => "UPPERCASE",
        "lowercase" => "lowercase",
        "upperCamelCase" => "PascalCase",
        "lowerCamelCase" => "camelCase",
        _ => "AsIs",
    };
}
