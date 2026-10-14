using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spec 042 (FR-012, task T058): the driver reports a statement's kind and row count but not the server's command tag, so
/// the tag is composed: the counted forms from kind and rows, every other command from its leading words. These pin the
/// tags psql prints for the common commands.
/// </summary>
public class PgCommandTagTests
{
    [Theory]
    [InlineData("SELECT 1", PgStatementType.Select, 3, "SELECT 3", 3)]
    [InlineData("INSERT INTO t VALUES (1)", PgStatementType.Insert, 2, "INSERT 0 2", 2)]
    [InlineData("UPDATE t SET a = 1", PgStatementType.Update, 12, "UPDATE 12", 12)]
    [InlineData("DELETE FROM t", PgStatementType.Delete, 0, "DELETE 0", 0)]
    [InlineData("MERGE INTO t USING s ON true WHEN MATCHED THEN DELETE", PgStatementType.Merge, 4, "MERGE 4", 4)]
    [InlineData("COPY t TO '/tmp/x'", PgStatementType.Copy, 7, "COPY 7", 7)]
    [InlineData("FETCH 5 FROM c", PgStatementType.Fetch, 5, "FETCH 5", 5)]
    [InlineData("MOVE 5 IN c", PgStatementType.Move, 5, "MOVE 5", 5)]
    [InlineData("CREATE TABLE t2 AS SELECT 1", PgStatementType.CreateTableAs, 1, "SELECT 1", 1)]
    public void Counted_statements_use_the_kind_and_the_row_count(string sql, PgStatementType type, long rows, string tag, long count)
    {
        var result = PgCommandTag.For(sql, type, rows);

        Assert.Equal(tag, result.Tag);
        Assert.Equal(count, result.RowCount);
    }

    [Theory]
    [InlineData("CREATE TABLE t (a int)", "CREATE TABLE")]
    [InlineData("create temp table t (a int)", "CREATE TABLE")]
    [InlineData("CREATE UNLOGGED TABLE t (a int)", "CREATE TABLE")]
    [InlineData("CREATE UNIQUE INDEX CONCURRENTLY i ON t (a)", "CREATE INDEX")]
    [InlineData("CREATE OR REPLACE FUNCTION f() RETURNS int AS $$ SELECT 1 $$ LANGUAGE sql", "CREATE FUNCTION")]
    [InlineData("CREATE OR REPLACE VIEW v AS SELECT 1", "CREATE VIEW")]
    [InlineData("CREATE SCHEMA s", "CREATE SCHEMA")]
    [InlineData("CREATE SEQUENCE s", "CREATE SEQUENCE")]
    [InlineData("CREATE EXTENSION IF NOT EXISTS pgcrypto", "CREATE EXTENSION")]
    [InlineData("CREATE TYPE mood AS ENUM ('a')", "CREATE TYPE")]
    [InlineData("CREATE DATABASE d", "CREATE DATABASE")]
    [InlineData("CREATE ROLE r", "CREATE ROLE")]
    [InlineData("ALTER TABLE t ADD COLUMN b int", "ALTER TABLE")]
    [InlineData("DROP TABLE IF EXISTS t", "DROP TABLE")]
    [InlineData("DROP INDEX i", "DROP INDEX")]
    [InlineData("CREATE MATERIALIZED VIEW m AS SELECT 1", "CREATE MATERIALIZED VIEW")]
    [InlineData("REFRESH MATERIALIZED VIEW m", "REFRESH MATERIALIZED VIEW")]
    [InlineData("TRUNCATE t", "TRUNCATE TABLE")]
    [InlineData("TRUNCATE TABLE t, u", "TRUNCATE TABLE")]
    [InlineData("LOCK TABLE t IN SHARE MODE", "LOCK TABLE")]
    [InlineData("CREATE FOREIGN TABLE ft (a int) SERVER s", "CREATE FOREIGN TABLE")]
    public void Definition_commands_are_the_verb_and_the_object_kind(string sql, string tag)
    {
        var result = PgCommandTag.For(sql, PgStatementType.Other, 0);

        Assert.Equal(tag, result.Tag);
        Assert.Equal(-1, result.RowCount);
    }

    [Theory]
    [InlineData("BEGIN", "BEGIN")]
    [InlineData("START TRANSACTION", "START TRANSACTION")]
    [InlineData("COMMIT", "COMMIT")]
    [InlineData("END", "COMMIT")]
    [InlineData("ROLLBACK", "ROLLBACK")]
    [InlineData("ABORT", "ROLLBACK")]
    [InlineData("ROLLBACK TO SAVEPOINT s", "ROLLBACK")]
    [InlineData("SAVEPOINT s", "SAVEPOINT")]
    [InlineData("RELEASE SAVEPOINT s", "RELEASE")]
    [InlineData("SET search_path = a", "SET")]
    [InlineData("SET LOCAL x = 1", "SET")]
    [InlineData("RESET ALL", "RESET")]
    [InlineData("SHOW search_path", "SHOW")]
    [InlineData("DO $$ BEGIN END $$", "DO")]
    [InlineData("GRANT SELECT ON t TO r", "GRANT")]
    [InlineData("REVOKE ALL ON t FROM r", "REVOKE")]
    [InlineData("COMMENT ON TABLE t IS 'x'", "COMMENT")]
    [InlineData("VACUUM t", "VACUUM")]
    [InlineData("ANALYZE t", "ANALYZE")]
    [InlineData("EXPLAIN SELECT 1", "EXPLAIN")]
    [InlineData("PREPARE p AS SELECT 1", "PREPARE")]
    [InlineData("DEALLOCATE p", "DEALLOCATE")]
    [InlineData("DECLARE c CURSOR FOR SELECT 1", "DECLARE CURSOR")]
    [InlineData("CLOSE c", "CLOSE CURSOR")]
    [InlineData("NOTIFY ch", "NOTIFY")]
    [InlineData("LISTEN ch", "LISTEN")]
    [InlineData("DISCARD ALL", "DISCARD ALL")]
    [InlineData("CALL p()", "CALL")]
    [InlineData("CHECKPOINT", "CHECKPOINT")]
    public void Other_commands_are_their_leading_words(string sql, string tag)
    {
        Assert.Equal(tag, PgCommandTag.For(sql, PgStatementType.Other, 0).Tag);
    }

    [Fact]
    public void Leading_comments_are_skipped()
    {
        Assert.Equal("DROP TABLE", PgCommandTag.For("-- tidy up\n/* really */ DROP TABLE t", PgStatementType.Other, 0).Tag);
    }

    [Fact]
    public void An_unknown_command_is_its_first_word_and_an_empty_statement_is_empty()
    {
        Assert.Equal("FROBNICATE", PgCommandTag.For("frobnicate widgets", PgStatementType.Other, 0).Tag);
        Assert.Equal(string.Empty, PgCommandTag.For("   ", PgStatementType.Other, 0).Tag);
    }

    [Theory]
    [InlineData("Select", PgStatementType.Select)]
    [InlineData("Insert", PgStatementType.Insert)]
    [InlineData("CreateTableAs", PgStatementType.CreateTableAs)]
    [InlineData("SomethingNew", PgStatementType.Other)]
    [InlineData(null, PgStatementType.Other)]
    public void The_drivers_statement_type_names_map_with_Other_as_the_fallback(string? name, PgStatementType expected)
    {
        Assert.Equal(expected, PgCommandTag.TypeFromDriverName(name));
    }
}
