using AkmlSql.Core;
using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spec 042 (FR-016, research R9, data-model §9): Npgsql exposes no transaction status, so the engine tracks it
/// per statement. The tracker is a pure function of (state, statement, outcome).
/// </summary>
public class PgTransactionTrackerTests
{
    private static TransactionState Next(TransactionState state, string sql, bool ok = true, string? sqlState = null) =>
        PgTransactionTracker.Next(state, sql, ok, sqlState);

    [Theory]
    [InlineData("BEGIN")]
    [InlineData("begin transaction isolation level serializable")]
    [InlineData("START TRANSACTION")]
    [InlineData("-- comment first\nBEGIN")]
    public void BEGIN_and_START_TRANSACTION_open_a_transaction(string sql)
    {
        Assert.Equal(TransactionState.Open, Next(TransactionState.None, sql));
    }

    [Theory]
    [InlineData("COMMIT")]
    [InlineData("END")]
    [InlineData("ROLLBACK")]
    [InlineData("ABORT")]
    [InlineData("commit work")]
    public void COMMIT_END_ROLLBACK_and_ABORT_close_it(string sql)
    {
        Assert.Equal(TransactionState.None, Next(TransactionState.Open, sql));
    }

    [Theory]
    [InlineData("COMMIT AND CHAIN")]
    [InlineData("ROLLBACK AND CHAIN")]
    public void AND_CHAIN_starts_the_next_transaction_immediately(string sql)
    {
        Assert.Equal(TransactionState.Open, Next(TransactionState.Open, sql));
        Assert.Equal(TransactionState.None, Next(TransactionState.Open, sql.Replace("AND CHAIN", "AND NO CHAIN")));
    }

    [Fact]
    public void An_error_inside_a_transaction_aborts_it()
    {
        Assert.Equal(TransactionState.Aborted, Next(TransactionState.Open, "SELECT 1/0", ok: false, sqlState: "22012"));
    }

    [Fact]
    public void An_error_with_no_transaction_leaves_autocommit_alone()
    {
        Assert.Equal(TransactionState.None, Next(TransactionState.None, "SELECT 1/0", ok: false, sqlState: "22012"));
    }

    [Fact]
    public void In_failed_sql_transaction_always_means_aborted()
    {
        // Self-healing: even if the tracker thought the session was idle, 25P02 is the server saying otherwise.
        Assert.Equal(TransactionState.Aborted, Next(TransactionState.None, "SELECT 1", ok: false, sqlState: "25P02"));
        Assert.Equal(TransactionState.Aborted, Next(TransactionState.Aborted, "SELECT 1", ok: false, sqlState: "25P02"));
    }

    [Theory]
    [InlineData("ROLLBACK")]
    [InlineData("ABORT")]
    [InlineData("COMMIT")]   // PostgreSQL turns COMMIT in an aborted transaction into a ROLLBACK
    [InlineData("END")]
    public void An_aborted_transaction_ends_with_ROLLBACK_or_COMMIT(string sql)
    {
        Assert.Equal(TransactionState.None, Next(TransactionState.Aborted, sql));
    }

    [Fact]
    public void ROLLBACK_TO_SAVEPOINT_recovers_an_aborted_transaction_but_does_not_close_an_open_one()
    {
        Assert.Equal(TransactionState.Open, Next(TransactionState.Aborted, "ROLLBACK TO SAVEPOINT sp1"));
        Assert.Equal(TransactionState.Open, Next(TransactionState.Open, "ROLLBACK TO sp1"));
    }

    [Theory]
    [InlineData("SAVEPOINT sp1")]
    [InlineData("RELEASE SAVEPOINT sp1")]
    [InlineData("SELECT 1")]
    [InlineData("UPDATE t SET a = 1")]
    public void Ordinary_statements_and_savepoints_do_not_change_an_open_transaction(string sql)
    {
        Assert.Equal(TransactionState.Open, Next(TransactionState.Open, sql));
    }

    [Fact]
    public void PREPARE_TRANSACTION_detaches_the_session_from_the_transaction()
    {
        Assert.Equal(TransactionState.None, Next(TransactionState.Open, "PREPARE TRANSACTION 'tx1'"));
        Assert.Equal(TransactionState.None, Next(TransactionState.None, "COMMIT PREPARED 'tx1'"));
        Assert.Equal(TransactionState.None, Next(TransactionState.None, "ROLLBACK PREPARED 'tx1'"));
    }

    [Fact]
    public void A_failed_BEGIN_does_not_open_a_transaction()
    {
        Assert.Equal(TransactionState.None, Next(TransactionState.None, "BEGIN", ok: false, sqlState: "08006"));
    }

    [Fact]
    public void A_failed_COMMIT_inside_a_transaction_aborts_it()
    {
        Assert.Equal(TransactionState.Aborted, Next(TransactionState.Open, "COMMIT", ok: false, sqlState: "40001"));
    }

    [Fact]
    public void BEGIN_ATOMIC_inside_a_function_definition_is_not_a_transaction_start()
    {
        Assert.Equal(TransactionState.None, Next(TransactionState.None, "CREATE FUNCTION f() RETURNS int LANGUAGE sql BEGIN ATOMIC SELECT 1; END"));
    }
}
