using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Actions;

/// <summary>
/// Defines a standalone format action that can be applied independently
/// without running the full formatting pipeline.
/// </summary>
public interface IFormatAction
{
    FormatResult Execute(string sql, FormattingProfile profile);
}
