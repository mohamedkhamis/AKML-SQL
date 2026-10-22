using AkmlSql.Core;
using AkmlSql.Engine.Pairing;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 043 follow-up (reported 2026-10-22): a new query tab takes the password of the tab it was opened from — only for the
/// same server and login, only into a string without one, and nothing but the password.
/// </summary>
public class SessionPasswordCopyTests
{
    private static string Sql(string server, string login, string? password, string db = "Northwind") =>
        new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = db, UserID = login, Password = password ?? string.Empty }.ConnectionString;

    private static string Pg(string host, int port, string user, string? password, string db = "postgres") =>
        new NpgsqlConnectionStringBuilder { Host = host, Port = port, Username = user, Password = password, Database = db }.ConnectionString;

    [Fact]
    public void The_same_server_and_login_take_the_password_and_keep_their_own_database()
    {
        var copied = new SqlConnectionStringBuilder(SessionPasswordCopy.Apply(
            Sql("LOCALHOST", "Sales", null, db: "master"), DatabaseKind.SqlServer, Sql("localhost", "sales", "pw"), DatabaseKind.SqlServer));

        Assert.Equal("pw", copied.Password);
        Assert.Equal("master", copied.InitialCatalog);
    }

    [Theory]
    [InlineData("other-server", "sales")]
    [InlineData("localhost", "other-login")]
    public void Another_server_or_login_gets_nothing(string server, string login)
    {
        var target = Sql(server, login, null);
        Assert.Equal(target, SessionPasswordCopy.Apply(target, DatabaseKind.SqlServer, Sql("localhost", "sales", "pw"), DatabaseKind.SqlServer));
    }

    [Fact]
    public void A_typed_password_windows_authentication_and_another_kind_are_left_alone()
    {
        var typed = Sql("localhost", "sales", "typed");
        var windows = new SqlConnectionStringBuilder { DataSource = "localhost", IntegratedSecurity = true }.ConnectionString;
        var source = Sql("localhost", "sales", "pw");

        Assert.Equal(typed, SessionPasswordCopy.Apply(typed, DatabaseKind.SqlServer, source, DatabaseKind.SqlServer));
        Assert.Equal(windows, SessionPasswordCopy.Apply(windows, DatabaseKind.SqlServer, source, DatabaseKind.SqlServer));
        Assert.Equal(Sql("localhost", "sales", null), SessionPasswordCopy.Apply(Sql("localhost", "sales", null), DatabaseKind.SqlServer, source, DatabaseKind.PostgreSql));
    }

    [Fact]
    public void PostgreSQL_matches_host_port_and_user()
    {
        var source = Pg("db1", 5432, "app", "pw");

        Assert.Equal("pw", new NpgsqlConnectionStringBuilder(SessionPasswordCopy.Apply(Pg("DB1", 5432, "app", null, "sales"), DatabaseKind.PostgreSql, source, DatabaseKind.PostgreSql)).Password);
        Assert.Null(new NpgsqlConnectionStringBuilder(SessionPasswordCopy.Apply(Pg("db1", 5433, "app", null), DatabaseKind.PostgreSql, source, DatabaseKind.PostgreSql)).Password);
        Assert.Null(new NpgsqlConnectionStringBuilder(SessionPasswordCopy.Apply(Pg("db1", 5432, "App", null), DatabaseKind.PostgreSql, source, DatabaseKind.PostgreSql)).Password);
    }
}
