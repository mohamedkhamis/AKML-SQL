using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.PostgreSql;
using AkmlSql.Engine.Productivity;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T062): "execute current statement" and next / previous statement find PostgreSQL statements with the
/// splitter a run uses, and pick the one a cursor belongs to by the same rule as for SQL Server.
/// </summary>
public class PgStatementBoundariesTests
{
    private const string Script = "SELECT 1;\nUPDATE t SET a = 1\n  WHERE b = 2;\n\\dt\nSELECT 3";

    [Fact]
    public void Every_statement_has_its_offsets_zero_based_lines_and_a_type_name()
    {
        var all = PgStatementBoundaries.GetAll(Script);

        Assert.Equal(4, all.Length);
        Assert.Equal(("SelectStatement", 0, 0), (all[0].StatementType, all[0].StartLine, all[0].EndLine));
        Assert.Equal(("UpdateStatement", 1, 2), (all[1].StatementType, all[1].StartLine, all[1].EndLine));
        Assert.Equal(("MetaCommand", 3, 3), (all[2].StatementType, all[2].StartLine, all[2].EndLine));
        Assert.Equal(("SelectStatement", 4, 4), (all[3].StatementType, all[3].StartLine, all[3].EndLine));
        Assert.Equal("SELECT 1", Script.Substring(all[0].StartOffset, all[0].EndOffset - all[0].StartOffset));
        Assert.Equal("SELECT 3", Script.Substring(all[3].StartOffset, all[3].EndOffset - all[3].StartOffset));
    }

    [Fact]
    public void The_cursor_selects_the_statement_that_contains_it_or_the_nearest_before()
    {
        var inUpdate = PgStatementBoundaries.FindAtOffset(Script, Script.IndexOf("WHERE", StringComparison.Ordinal));
        Assert.Equal("UpdateStatement", inUpdate!.StatementType);

        // On the blank position right after the first semicolon: still the first statement (the nearest before it).
        var afterFirst = PgStatementBoundaries.FindAtOffset(Script, Script.IndexOf(';') + 1);
        Assert.Equal("SelectStatement", afterFirst!.StatementType);
        Assert.Equal(0, afterFirst.StartLine);
    }

    [Fact]
    public void A_dollar_quoted_body_with_semicolons_is_one_statement()
    {
        var sql = "CREATE FUNCTION f() RETURNS int AS $$ BEGIN RETURN 1; END; $$ LANGUAGE plpgsql;\nSELECT f();";

        var all = PgStatementBoundaries.GetAll(sql);

        Assert.Equal(2, all.Length);
        Assert.Equal("CreateStatement", all[0].StatementType);
    }

    [Fact]
    public void An_empty_script_has_no_statements()
    {
        Assert.Empty(PgStatementBoundaries.GetAll("  \n -- nothing\n"));
        Assert.Null(PgStatementBoundaries.FindAtOffset("", 0));
    }

    [Fact]
    public async Task The_handler_uses_the_PostgreSQL_splitter_for_a_PostgreSQL_session_only()
    {
        var handler = new ProductivityRequestHandler(
            new TsqlParserService(), sessionId => sessionId == "pg" ? DatabaseKind.PostgreSql : DatabaseKind.SqlServer);
        var sql = "SELECT $$a;b$$;\nSELECT 2";

        async Task<StatementBoundaryResponse> AskAsync(string session)
        {
            var reply = await handler.HandleStatementBoundaryAsync(new RpcMessage
            {
                MessageType = MessageTypes.StatementBoundary,
                RequestId = 1,
                Payload = MessagePackSerializer.Serialize(new StatementBoundaryRequest
                {
                    SessionId = session, SqlText = sql, CursorOffset = 3, AllStatements = true,
                }),
            });
            return MessagePackSerializer.Deserialize<StatementBoundaryResponse>(reply!.Payload!);
        }

        var pg = await AskAsync("pg");
        Assert.True(pg.Success);
        Assert.Equal(2, pg.AllStatements!.Length);
        Assert.Equal(0, pg.CurrentStatement!.StartLine);
    }
}
