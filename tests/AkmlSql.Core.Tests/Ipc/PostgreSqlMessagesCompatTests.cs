using System;
using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

/// <summary>
/// Spec 042 (contracts/ipc.md): every new key is appended after the last existing key, so an older
/// peer's payload still loads (new fields at their defaults) and a newer peer's payload still loads
/// in an older one. Each test serialises a "legacy" positional array (the pre-042 key count) and reads it
/// with the current type, then round-trips the new keys.
/// </summary>
public class PostgreSqlMessagesCompatTests
{
    private static T Legacy<T>(params object?[] values) =>
        MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(values));

    private static T RoundTrip<T>(T value) =>
        MessagePackSerializer.Deserialize<T>(MessagePackSerializer.Serialize(value));

    [Fact]
    public void ConnectionInfo_defaults_to_SQL_Server_and_keeps_the_new_keys()
    {
        var legacy = Legacy<ConnectionInfo>("s1", "Server=x", 16, 1, "db");
        Assert.Equal((int)DatabaseKind.SqlServer, legacy.Kind);
        Assert.False(legacy.RememberPassword);

        var back = RoundTrip(new ConnectionInfo { SessionId = "s", Kind = (int)DatabaseKind.PostgreSql, RememberPassword = true });
        Assert.Equal((int)DatabaseKind.PostgreSql, back.Kind);
        Assert.True(back.RememberPassword);
    }

    [Fact]
    public void TestSqlConnection_messages_carry_kind_version_and_error_code()
    {
        Assert.Equal(0, Legacy<TestSqlConnectionRequest>("Server=x").Kind);
        var legacyResponse = Legacy<TestSqlConnectionResponse>(true, null);
        Assert.Null(legacyResponse.ServerVersion);
        Assert.Null(legacyResponse.ErrorCode);

        var request = RoundTrip(new TestSqlConnectionRequest { ConnectionString = "Host=h", Kind = 1 });
        Assert.Equal(1, request.Kind);
        var response = RoundTrip(new TestSqlConnectionResponse { Ok = false, ErrorMessage = "bad", ServerVersion = "PostgreSQL 18.1", ErrorCode = DialectErrors.PasswordRequired });
        Assert.Equal("PostgreSQL 18.1", response.ServerVersion);
        Assert.Equal("dialect.password-required", response.ErrorCode);
    }

    [Fact]
    public void ListDatabases_messages_carry_kind_and_error_code()
    {
        Assert.Equal(0, Legacy<ListDatabasesRequest>("Server=x", "sid").Kind);
        Assert.Null(Legacy<ListDatabasesResponse>(true, new[] { "master" }, null).ErrorCode);

        Assert.Equal(1, RoundTrip(new ListDatabasesRequest { Kind = 1 }).Kind);
        Assert.Equal(DialectErrors.CredentialsRequired, RoundTrip(new ListDatabasesResponse { ErrorCode = DialectErrors.CredentialsRequired }).ErrorCode);
    }

    [Fact]
    public void ExecuteMessageDto_carries_sqlstate_detail_hint_and_column()
    {
        var legacy = Legacy<ExecuteMessageDto>(2, "Msg 102", 0, 102, 15, 1, 3, null, -1, -1L);
        Assert.Null(legacy.SqlState);
        Assert.Null(legacy.Detail);
        Assert.Null(legacy.Hint);
        Assert.Equal(0, legacy.Column);

        var back = RoundTrip(new ExecuteMessageDto { Kind = ExecuteMessageKind.Error, SqlState = "42P01", Detail = "d", Hint = "h", Column = 7 });
        Assert.Equal("42P01", back.SqlState);
        Assert.Equal("d", back.Detail);
        Assert.Equal("h", back.Hint);
        Assert.Equal(7, back.Column);
    }

    [Fact]
    public void ExecuteQueryResult_and_ChangeDatabaseResponse_carry_transaction_state()
    {
        var legacy = Legacy<ExecuteQueryResult>("q", 0, null, Array.Empty<object>(), Array.Empty<string>(), 1L, 0, false);
        Assert.Equal((int)TransactionState.None, legacy.TransactionState);
        Assert.Equal((int)TransactionState.Aborted, RoundTrip(new ExecuteQueryResult { TransactionState = (int)TransactionState.Aborted }).TransactionState);

        Assert.Equal(0, Legacy<ChangeDatabaseResponse>(true, "db", null, false).TransactionState);
        Assert.Equal(1, RoundTrip(new ChangeDatabaseResponse { TransactionState = 1 }).TransactionState);
    }

    [Fact]
    public void CrudCellDto_carries_the_type_name_for_casts()
    {
        Assert.Null(Legacy<CrudCellDto>("c", 1, null, null, null, "v").TypeName);
        Assert.Equal("numeric(10,2)", RoundTrip(new CrudCellDto { BaseColumnName = "c", TypeName = "numeric(10,2)" }).TypeName);
    }

    [Fact]
    public void AnalysisRuleInfoDto_and_SafetyCheckRequest_carry_the_kind()
    {
        Assert.Equal(0, Legacy<AnalysisRuleInfoDto>("PE001", "n", "Performance", 2, 2, true, false, false, "d").Kind);
        Assert.Equal(1, RoundTrip(new AnalysisRuleInfoDto { RuleId = "PGPE001", Kind = 1 }).Kind);

        Assert.Equal(0, Legacy<SafetyCheckRequest>("DROP TABLE t", "srv", false).Kind);
        Assert.Equal(1, RoundTrip(new SafetyCheckRequest { SqlText = "DROP TABLE t", Kind = 1 }).Kind);
    }

    [Fact]
    public void History_messages_carry_the_kind()
    {
        var record = RoundTrip(new HistoryRecordRequest { SqlText = "select 1", Kind = 1 });
        Assert.Equal(1, record.Kind);
        Assert.Equal(0, RoundTrip(new HistoryRecordRequest { SqlText = "select 1" }).Kind);

        Assert.Equal(1, RoundTrip(new HistoryEntryDto { Id = 1, Kind = 1 }).Kind);
        Assert.Null(RoundTrip(new HistorySearchRequest()).Kind);
        Assert.Equal(1, RoundTrip(new HistorySearchRequest { Kind = 1 }).Kind);
    }

    [Fact]
    public void SchemaPhasePayload_carries_inaccessible_hidden_and_search_path()
    {
        var legacy = Legacy<SchemaPhasePayload>("db", 1, "sum", Array.Empty<object>(), Array.Empty<object>());
        Assert.Empty(legacy.InaccessibleSchemas);
        Assert.Empty(legacy.HiddenSchemas);
        Assert.Empty(legacy.SearchPath);

        var back = RoundTrip(new SchemaPhasePayload
        {
            InaccessibleSchemas = new[] { "locked" },
            HiddenSchemas = new[] { "pg_catalog", "information_schema" },
            SearchPath = new[] { "public", "sales" },
        });
        Assert.Equal(new[] { "locked" }, back.InaccessibleSchemas);
        Assert.Equal(new[] { "pg_catalog", "information_schema" }, back.HiddenSchemas);
        Assert.Equal(new[] { "public", "sales" }, back.SearchPath);
    }

    [Fact]
    public void The_appended_keys_use_exactly_the_numbers_in_the_contract()
    {
        AssertKey<ConnectionInfo>(nameof(ConnectionInfo.Kind), 5);
        AssertKey<ConnectionInfo>(nameof(ConnectionInfo.RememberPassword), 6);
        AssertKey<TestSqlConnectionRequest>(nameof(TestSqlConnectionRequest.Kind), 1);
        AssertKey<TestSqlConnectionResponse>(nameof(TestSqlConnectionResponse.ServerVersion), 2);
        AssertKey<TestSqlConnectionResponse>(nameof(TestSqlConnectionResponse.ErrorCode), 3);
        AssertKey<ListDatabasesRequest>(nameof(ListDatabasesRequest.Kind), 2);
        AssertKey<ListDatabasesResponse>(nameof(ListDatabasesResponse.ErrorCode), 3);
        AssertKey<ExecuteMessageDto>(nameof(ExecuteMessageDto.SqlState), 10);
        AssertKey<ExecuteMessageDto>(nameof(ExecuteMessageDto.Detail), 11);
        AssertKey<ExecuteMessageDto>(nameof(ExecuteMessageDto.Hint), 12);
        AssertKey<ExecuteMessageDto>(nameof(ExecuteMessageDto.Column), 13);
        AssertKey<ExecuteQueryResult>(nameof(ExecuteQueryResult.TransactionState), 13);
        AssertKey<ChangeDatabaseResponse>(nameof(ChangeDatabaseResponse.TransactionState), 4);
        AssertKey<CrudCellDto>(nameof(CrudCellDto.TypeName), 6);
        AssertKey<AnalysisRuleInfoDto>(nameof(AnalysisRuleInfoDto.Kind), 9);
        AssertKey<SafetyCheckRequest>(nameof(SafetyCheckRequest.Kind), 3);
        AssertKey<HistoryRecordRequest>(nameof(HistoryRecordRequest.Kind), 13);
        AssertKey<HistoryEntryDto>(nameof(HistoryEntryDto.Kind), 18);
        AssertKey<HistorySearchRequest>(nameof(HistorySearchRequest.Kind), 15);
        AssertKey<SchemaPhasePayload>(nameof(SchemaPhasePayload.InaccessibleSchemas), 5);
        AssertKey<SchemaPhasePayload>(nameof(SchemaPhasePayload.HiddenSchemas), 6);
        AssertKey<SchemaPhasePayload>(nameof(SchemaPhasePayload.SearchPath), 7);
    }

    private static void AssertKey<T>(string property, int expected)
    {
        var info = typeof(T).GetProperty(property);
        Assert.NotNull(info);
        var key = (KeyAttribute?)Attribute.GetCustomAttribute(info!, typeof(KeyAttribute));
        Assert.NotNull(key);
        Assert.Equal(expected, key!.IntKey);
    }
}
