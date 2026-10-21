using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>Spec 042 (FR-043): which command tags mean the catalog changed, so the explorer reloads.</summary>
public class PgCommandTagsTests
{
    [Theory]
    [InlineData("CREATE TABLE")]
    [InlineData("CREATE INDEX")]
    [InlineData("CREATE MATERIALIZED VIEW")]
    [InlineData("ALTER TABLE")]
    [InlineData("DROP TABLE")]
    [InlineData("DROP SCHEMA")]
    [InlineData("COMMENT")]
    [InlineData("  CREATE FUNCTION")]
    public void Definition_commands_change_the_schema(string tag) => Assert.True(PgCommandTags.ChangesSchema(tag));

    [Theory]
    [InlineData("SELECT 3")]
    [InlineData("INSERT 0 1")]
    [InlineData("UPDATE 12")]
    [InlineData("DELETE 0")]
    [InlineData("TRUNCATE TABLE")]
    [InlineData("REFRESH MATERIALIZED VIEW")]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("SET")]
    [InlineData("DO")]
    [InlineData("CREATED")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_commands_do_not(string? tag) => Assert.False(PgCommandTags.ChangesSchema(tag));
}
