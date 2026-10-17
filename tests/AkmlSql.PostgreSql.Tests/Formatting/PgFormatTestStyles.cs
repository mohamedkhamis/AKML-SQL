using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;

namespace AkmlSql.PostgreSql.Tests.Formatting;

/// <summary>The built-in styles as SQL Prompt style documents, the way the engine hands them to the PostgreSQL formatter.</summary>
internal static class PgFormatTestStyles
{
    /// <summary>The built-in styles the corpus is formatted with.</summary>
    public static readonly string[] Names =
    {
        "default", "compact", "indented", "aligned-left-bracket", "leading-commas", "minimalist", "khamis-style", "collapsed",
    };

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate AKML-SQL.slnx walking up from " + AppContext.BaseDirectory);
    }

    public static SqlPromptStyle Load(string name)
    {
        var path = Path.Combine(RepoRoot(), "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", name + ".akmlstyle");
        var profile = ProfileSerializer.Deserialize(File.ReadAllText(path));
        return new SqlPromptStyle(SqlPromptStyles.ToDocument(profile));
    }
}
