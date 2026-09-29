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
            Assert.True(b.Encrypt);                    // spec 029 security review: encrypt the password-bearing wire
            Assert.True(b.TrustServerCertificate);     // internal self-signed certs; mirrors SSMS 22 default
        }

        // ---- Server aliases: the caption shows a name, the engine needs the server -------------------

        [Fact]
        public void A_custom_connection_name_in_the_caption_connects_to_the_real_server()
        {
            // SSMS 22's custom connection name ("ServerDemo") is what the tab shows; SSMS itself is
            // connected to 192.168.4.5. The engine must connect to the server, the name stays for display.
            var r = SsmsConnectionDetector.ParseCaption(
                @"SQLQuery1.sql - ServerDemo.Northwind (DOMAIN\me (61))",
                SsmsConnectionDetector.AuthMode.Windows, "Windows Authentication", "192.168.4.5");

            Assert.Equal("ServerDemo", r.Server);
            Assert.Equal("192.168.4.5", r.DataSource);
            Assert.Contains("Data Source=192.168.4.5;", r.ConnectionString);
            Assert.DoesNotContain("ServerDemo", r.ConnectionString);
        }

        [Fact]
        public void Without_a_connected_server_the_caption_server_is_used()
        {
            var r = SsmsConnectionDetector.ParseCaption(
                @"SQLQuery1.sql - sql01.Northwind (DOMAIN\me (61))",
                SsmsConnectionDetector.AuthMode.Windows, "Windows Authentication", null);

            Assert.Equal("sql01", r.DataSource);
            Assert.Contains("Data Source=sql01;", r.ConnectionString);
        }

        [Theory]
        [InlineData("ServerDemo", "192.168.4.5", "192.168.4.5")]
        [InlineData("sql01", null, "sql01")]
        [InlineData("sql01", "  ", "sql01")]
        [InlineData("sql01", " sql01 ", "sql01")]
        public void The_connected_server_wins_when_known(string caption, string? connected, string expected)
            => Assert.Equal(expected, SsmsConnectionDetector.ChooseDataSource(caption, connected!));

        [Fact]
        public void A_result_without_a_data_source_connects_to_its_server()
            => Assert.Equal("sql01", new SsmsConnectionDetector.ConnectionResult { Server = "sql01" }.DataSource);

        [Theory]
        [InlineData("sql01", "sql01", "sql01")]
        [InlineData("ServerDemo", "192.168.4.5", "ServerDemo \u2192 192.168.4.5")]
        [InlineData("ServerDemo", "ServerDemo", "ServerDemo (alias for tcp:192.168.4.5,1433)")]
        public void The_credential_popup_says_which_server_it_connects_to(string shown, string dataSource, string expected)
            => Assert.Equal(expected, SqlCredentialDialog.ServerDescription(shown, dataSource,
                name => name == "ServerDemo" ? "DBMSSOCN,192.168.4.5,1433" : null));
    }
}
