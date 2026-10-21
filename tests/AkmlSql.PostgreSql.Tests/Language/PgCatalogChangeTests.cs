using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spec 042 (FR-043): a run that created an object reloads the Schema explorer. The server reports CREATE TABLE … AS,
/// CREATE MATERIALIZED VIEW … AS and SELECT … INTO as "SELECT n", which the tag rule cannot see; the statement itself can.
/// </summary>
public class PgCatalogChangeTests
{
    // The shared rule's shape (AkmlSql.Core.Text.PgCommandTags.ChangesSchema), which this project does not reference.
    private static bool TagRule(string? tag) =>
        tag != null && (tag.StartsWith("CREATE ") || tag.StartsWith("ALTER ") || tag.StartsWith("DROP ") || tag == "COMMENT");

    private static bool Changes(string sql, string tag) =>
        PgCatalogChange.Changes(Assert.Single(PgStatementSplitter.Split(sql)), tag, TagRule);

    [Theory]
    [InlineData("CREATE TABLE backup AS SELECT * FROM customers;", "SELECT 12")]
    [InlineData("create materialized view mv as select 1;", "SELECT 1")]
    [InlineData("SELECT * INTO new_t FROM t;", "SELECT 3")]
    [InlineData("select a, b into new_t from t where a > 1;", "SELECT 0")]
    [InlineData("CREATE TABLE t (id int);", "CREATE TABLE")]
    [InlineData("DROP VIEW v;", "DROP VIEW")]
    public void Statements_that_create_or_change_objects_count(string sql, string tag)
    {
        Assert.True(Changes(sql, tag));
    }

    [Theory]
    [InlineData("SELECT * FROM t;", "SELECT 5")]
    [InlineData("SELECT (SELECT max(x) FROM y) AS m FROM t;", "SELECT 1")]
    [InlineData("SELECT 'into' AS word, \"into\" FROM t;", "SELECT 1")]
    [InlineData("INSERT INTO t VALUES (1);", "INSERT 0 1")]
    [InlineData("UPDATE t SET a = 1;", "UPDATE 1")]
    [InlineData("REFRESH MATERIALIZED VIEW mv;", "REFRESH MATERIALIZED VIEW")]
    public void Statements_that_only_read_or_write_rows_do_not(string sql, string tag)
    {
        Assert.False(Changes(sql, tag));
    }
}
