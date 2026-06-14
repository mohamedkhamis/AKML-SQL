using System.Collections.Generic;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs
{
    /// <summary>
    /// Covers the database match target (FR-038, R9): a rule with
    /// <c>matchTarget=database</c> evaluates against the connected database name
    /// regardless of the server it lives on ("database-on-any-server").
    /// </summary>
    public class EnvironmentMatcherDatabaseTests
    {
        private static readonly List<EnvironmentRule> DatabaseRules = new()
        {
            new(0, "ProdDb", EnvironmentMatcher.MatchTargetDatabase, "#FF4444", "PRODUCTION"),
            new(1, "*_live", EnvironmentMatcher.MatchTargetDatabase, "#FFB800", "LIVE"),
        };

        // Mixed rule set: a server rule and a database rule coexist; neither must
        // leak into the other's match target.
        private static readonly List<EnvironmentRule> MixedRules = new()
        {
            new(0, "*prod*", EnvironmentMatcher.MatchTargetServerName, "#FF4444", "PROD-SERVER"),
            new(1, "*prod*", EnvironmentMatcher.MatchTargetDatabase, "#FFB800", "PROD-DB"),
        };

        [Theory]
        // Exact database match.
        [InlineData("any-server", "ProdDb", "PRODUCTION")]
        [InlineData("any-server", "proddb", "PRODUCTION")] // case-insensitive
        // Glob database match.
        [InlineData("any-server", "sales_live", "LIVE")]
        [InlineData("any-server", "ORDERS_LIVE", "LIVE")]
        public void Match_DatabaseRules_ReturnsExpectedLabel(string server, string database, string expectedLabel)
        {
            var result = EnvironmentMatcher.Match(DatabaseRules, server, database);
            Assert.NotNull(result);
            Assert.Equal(expectedLabel, result!.Label);
        }

        [Theory]
        // Database-on-any-server: the same database rule matches no matter which
        // server (or no server) the connection lives on.
        [InlineData("SQLPROD01")]
        [InlineData("dev-box.corp.net")]
        [InlineData("localhost")]
        [InlineData(null)]
        public void Match_DatabaseRule_MatchesOnAnyServer(string? server)
        {
            var result = EnvironmentMatcher.Match(DatabaseRules, server, "ProdDb");
            Assert.NotNull(result);
            Assert.Equal("PRODUCTION", result!.Label);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("StagingDb")]
        [InlineData("sales_test")]
        public void Match_NoMatchingDatabase_ReturnsNull(string? database)
        {
            var result = EnvironmentMatcher.Match(DatabaseRules, "any-server", database);
            Assert.Null(result);
        }

        [Fact]
        public void Match_DatabaseRulesIgnoredWhenNoDatabaseProvided()
        {
            // The single-arg overload (server only) must skip database rules entirely.
            var result = EnvironmentMatcher.Match(DatabaseRules, "ProdDb");
            Assert.Null(result);
        }

        [Fact]
        public void Match_DatabaseRule_DoesNotLeakIntoServerString()
        {
            // Guard A: a database rule whose pattern would match the server string must
            // still return null when the actual database does not match.
            var result = EnvironmentMatcher.Match(DatabaseRules, "ProdDb", "salesdb");
            Assert.Null(result);
        }

        [Fact]
        public void Match_DatabaseRule_PicksDatabaseTarget_NotServer()
        {
            // The database rule matches on the database even though a server rule with
            // an identical pattern exists — the connected server here does not match.
            var result = EnvironmentMatcher.Match(MixedRules, "dev-box", "prod-orders");
            Assert.NotNull(result);
            Assert.Equal("PROD-DB", result!.Label);
        }

        [Fact]
        public void Match_ServerRule_UnaffectedByDatabaseName()
        {
            // Guard B: the server rule still resolves on the server regardless of the
            // database name passed in.
            var result = EnvironmentMatcher.Match(MixedRules, "prod-sql", "anything");
            Assert.NotNull(result);
            Assert.Equal("PROD-SERVER", result!.Label);
        }

        [Fact]
        public void MatchTargetDatabase_HasExpectedConstantValue()
        {
            Assert.Equal("database", EnvironmentMatcher.MatchTargetDatabase);
        }
    }
}
