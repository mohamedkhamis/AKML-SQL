using System.Collections.Generic;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs
{
    /// <summary>
    /// Spec 040 (T152, OPT-08, research R8) — a server rule with a non-empty
    /// <see cref="EnvironmentRule.DatabaseName"/> (a "server + database" row in the Queries › Color
    /// grid) matches only that server AND database. An empty DatabaseName keeps the server-only
    /// behaviour pinned by <see cref="EnvironmentMatcherTests"/> and
    /// <see cref="EnvironmentMatcherDatabaseTests"/>.
    /// </summary>
    public class EnvironmentMatcherServerDatabaseTests
    {
        private static EnvironmentRule ServerRule(int order, string server, string database, string label) =>
            new(order, server, EnvironmentMatcher.MatchTargetServerName, database, "#FF4444", label);

        private static readonly List<EnvironmentRule> Rules = new()
        {
            ServerRule(0, "*PROD*", "Sales*", "PROD-SALES"),
            ServerRule(1, "*PROD*", "", "PROD"),
        };

        [Theory]
        [InlineData("SQLPROD01", "SalesDb", "PROD-SALES")]
        [InlineData("sqlprod01", "SALES_ARCHIVE", "PROD-SALES")]   // case-insensitive glob on both
        [InlineData("SQLPROD01", "Inventory", "PROD")]             // database differs → next rule
        [InlineData("SQLPROD01", null, "PROD")]                    // no database → next rule
        [InlineData("SQLPROD01", "", "PROD")]
        public void ServerAndDatabaseRule_RequiresBoth(string server, string? database, string expected)
        {
            var result = EnvironmentMatcher.Match(Rules, server, database);

            Assert.NotNull(result);
            Assert.Equal(expected, result!.Label);
        }

        [Fact]
        public void ServerAndDatabaseRule_DoesNotMatchAnotherServer()
        {
            var rules = new List<EnvironmentRule> { ServerRule(0, "*PROD*", "SalesDb", "PROD-SALES") };

            Assert.Null(EnvironmentMatcher.Match(rules, "dev-box", "SalesDb"));
        }

        [Fact]
        public void ServerAndDatabaseRule_DoesNotMatchAnotherDatabase()
        {
            var rules = new List<EnvironmentRule> { ServerRule(0, "*PROD*", "SalesDb", "PROD-SALES") };

            Assert.Null(EnvironmentMatcher.Match(rules, "SQLPROD01", "Inventory"));
            Assert.Null(EnvironmentMatcher.Match(rules, "SQLPROD01", null));
        }

        [Fact]
        public void ServerAndDatabaseRule_IsSkippedByTheServerOnlyOverload()
        {
            // The one-argument overload has no database, so a rule that needs one can't match.
            var rules = new List<EnvironmentRule> { ServerRule(0, "*PROD*", "SalesDb", "PROD-SALES") };

            Assert.Null(EnvironmentMatcher.Match(rules, "SQLPROD01"));
        }

        [Fact]
        public void ServerRule_WithEmptyDatabaseName_StillMatchesOnServerAlone()
        {
            var rules = new List<EnvironmentRule> { ServerRule(0, "*PROD*", "", "PROD") };

            Assert.Equal("PROD", EnvironmentMatcher.Match(rules, "SQLPROD01", "anything")!.Label);
            Assert.Equal("PROD", EnvironmentMatcher.Match(rules, "SQLPROD01", null)!.Label);
            Assert.Equal("PROD", EnvironmentMatcher.Match(rules, "SQLPROD01")!.Label);
        }

        [Fact]
        public void ServerAndDatabaseRule_AcceptsACommaSeparatedDatabaseList()
        {
            var rules = new List<EnvironmentRule> { ServerRule(0, "*PROD*", "Sales, Orders*", "PROD-APP") };

            Assert.NotNull(EnvironmentMatcher.Match(rules, "SQLPROD01", "Sales"));
            Assert.NotNull(EnvironmentMatcher.Match(rules, "SQLPROD01", "OrdersArchive"));
            Assert.Null(EnvironmentMatcher.Match(rules, "SQLPROD01", "SalesArchive"));
        }
    }
}
