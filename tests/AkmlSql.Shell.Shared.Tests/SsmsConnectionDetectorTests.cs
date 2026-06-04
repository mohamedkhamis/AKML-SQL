using AkmlSql.Shell.Shared.Editor;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Regression for "any remote server cannot load schema, just local only".
    ///
    /// <para>
    /// <see cref="SsmsConnectionDetector.ParseCaption(string)"/> derives the SQL server + database
    /// from the SSMS window caption ("Server.Database"). It split at the <b>first</b> dot, which is
    /// correct only for <i>dotless</i> server names — `(local)`, `localhost`, `MACHINE\INSTANCE` —
    /// i.e. the local case. For a <b>remote</b> server addressed by FQDN (`srv.corp.contoso.com`) or
    /// IP (`10.0.0.5`), the first dot is INSIDE the server name, so the server was truncated to
    /// `srv` / `10` and the rest leaked into the database name. The engine then built a connection
    /// string to a bogus host and silently loaded no schema — hence "remote fails, local works".
    /// The fix splits at the <b>last</b> dot (the database is the final token; the server may contain
    /// dots).
    /// </para>
    /// </summary>
    public class SsmsConnectionDetectorTests
    {
        [Theory]
        // --- Local / dotless servers (must keep working) ---
        [InlineData("q.sql - (local).StockProduction (DOMAIN\\me (52))", "(local)", "StockProduction")]
        [InlineData("q.sql - localhost.MyDb (DOMAIN\\me (52))", "localhost", "MyDb")]
        [InlineData("q.sql - MACHINE\\SQLEXPRESS.MyDb (DOMAIN\\me (52))", "MACHINE\\SQLEXPRESS", "MyDb")]
        [InlineData("q.sql - SQLPROD01.Sales (DOMAIN\\me (52))", "SQLPROD01", "Sales")]
        // --- Remote servers with dots in the name (the bug) ---
        [InlineData("q.sql - srv.corp.contoso.com.Sales (DOMAIN\\me (52))", "srv.corp.contoso.com", "Sales")]
        [InlineData("q.sql - 10.0.0.5.Sales (DOMAIN\\me (52))", "10.0.0.5", "Sales")]
        [InlineData("q.sql - srv.corp.contoso.com\\SQL2019.Sales (DOMAIN\\me (52))", "srv.corp.contoso.com\\SQL2019", "Sales")]
        // --- SSMS 20 caption order ("Server.Database - file.sql") ---
        [InlineData("srv.corp.contoso.com.Sales - q.sql", "srv.corp.contoso.com", "Sales")]
        [InlineData("(local).StockProduction - q.sql", "(local)", "StockProduction")]
        public void ParseCaption_SplitsServerDatabaseAtLastDot(string caption, string expectedServer, string expectedDatabase)
        {
            var result = SsmsConnectionDetector.ParseCaption(caption);

            Assert.NotNull(result);
            Assert.Equal(expectedServer, result.Server);
            Assert.Equal(expectedDatabase, result.Database);
        }

        [Theory]
        [InlineData("1", (int)SsmsConnectionDetector.AuthMode.SqlPassword)]                         // numeric SqlPassword
        [InlineData("SqlPassword", (int)SsmsConnectionDetector.AuthMode.SqlPassword)]               // string form
        [InlineData("SQL Server Authentication", (int)SsmsConnectionDetector.AuthMode.SqlPassword)] // SSMS label
        [InlineData("2", (int)SsmsConnectionDetector.AuthMode.Unsupported)]                          // AAD Password stays unsupported
        [InlineData("4", (int)SsmsConnectionDetector.AuthMode.Unsupported)]                          // AAD Interactive stays unsupported
        [InlineData("3", (int)SsmsConnectionDetector.AuthMode.AzureAdIntegrated)]
        [InlineData("0", (int)SsmsConnectionDetector.AuthMode.Windows)]
        public void ClassifyAuth_MapsSqlLoginToSqlPassword(string raw, int expectedInt)
        {
            var expected = (SsmsConnectionDetector.AuthMode)expectedInt;
            Assert.Equal(expected, SsmsConnectionDetector.ClassifyAuth(raw));
        }

        [Fact]
        public void ParseCaption_BareLogin_ClassifiesSqlPassword_CapturesLogin_NotEngineUsableYet()
        {
            var r = SsmsConnectionDetector.ParseCaption("q.sql - 192.168.5.123.NatGas_G2_Testing (sa (53))");
            Assert.NotNull(r);
            Assert.Equal("192.168.5.123", r.Server);
            Assert.Equal("NatGas_G2_Testing", r.Database);
            Assert.Equal("sa", r.Login);
            Assert.Equal(SsmsConnectionDetector.AuthMode.SqlPassword, r.AuthMode);
            Assert.False(r.IsEngineUsable);   // no password at parse time
            Assert.Null(r.ConnectionString);
        }

        [Fact]
        public void BuildSqlAuthConnectionString_EscapesSpecialChars_AndSetsFields()
        {
            var cs = SsmsConnectionDetector.BuildSqlAuthConnectionString("10.0.0.5", "MyDb", "sa", "P@ss;w'd\"x");
            var b = new System.Data.SqlClient.SqlConnectionStringBuilder(cs);
            Assert.Equal("10.0.0.5", b.DataSource);
            Assert.Equal("MyDb", b.InitialCatalog);
            Assert.Equal("sa", b.UserID);
            Assert.Equal("P@ss;w'd\"x", b.Password);   // round-trips intact despite ; ' "
            Assert.False(b.Encrypt);
            Assert.True(b.TrustServerCertificate);
        }
    }
}
