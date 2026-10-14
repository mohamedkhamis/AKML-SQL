using Xunit;

namespace AkmlSql.Core.Tests;

/// <summary>Spec 042 (data-model §1): the kind enum's wire values and its string forms.</summary>
public class DatabaseKindTests
{
    [Fact]
    public void Wire_values_are_stable_and_SQL_Server_is_zero()
    {
        Assert.Equal(0, (int)DatabaseKind.SqlServer);
        Assert.Equal(1, (int)DatabaseKind.PostgreSql);
        Assert.Equal(0, (int)TransactionState.None);
        Assert.Equal(1, (int)TransactionState.Open);
        Assert.Equal(2, (int)TransactionState.Aborted);
    }

    [Theory]
    [InlineData("postgresql", DatabaseKind.PostgreSql)]
    [InlineData("PostgreSQL", DatabaseKind.PostgreSql)]
    [InlineData("  pg ", DatabaseKind.PostgreSql)]
    [InlineData("postgres", DatabaseKind.PostgreSql)]
    [InlineData("sqlserver", DatabaseKind.SqlServer)]
    [InlineData("mssql", DatabaseKind.SqlServer)]
    [InlineData("T-SQL", DatabaseKind.SqlServer)]
    public void Names_and_aliases_parse(string text, DatabaseKind expected)
    {
        Assert.True(DatabaseKindNames.TryParse(text, out var kind));
        Assert.Equal(expected, kind);
        Assert.Equal(expected, DatabaseKindNames.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("oracle")]
    public void Unknown_text_is_SQL_Server_so_pre_042_data_keeps_its_meaning(string? text)
    {
        Assert.False(DatabaseKindNames.TryParse(text, out _));
        Assert.Equal(DatabaseKind.SqlServer, DatabaseKindNames.Parse(text));
    }

    [Fact]
    public void ToName_and_FromInt_round_trip()
    {
        Assert.Equal("postgresql", DatabaseKindNames.ToName(DatabaseKind.PostgreSql));
        Assert.Equal("sqlserver", DatabaseKindNames.ToName(DatabaseKind.SqlServer));
        Assert.Equal(DatabaseKind.PostgreSql, DatabaseKindNames.FromInt(1));
        Assert.Equal(DatabaseKind.SqlServer, DatabaseKindNames.FromInt(0));
        Assert.Equal(DatabaseKind.SqlServer, DatabaseKindNames.FromInt(99));
    }
}
