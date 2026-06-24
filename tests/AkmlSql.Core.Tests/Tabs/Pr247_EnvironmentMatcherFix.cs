using System.Collections.Generic;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs
{
    /// <summary>
    /// PR #247 regression guard: <see cref="ColoringRule.DatabaseName"/> must be
    /// threaded into <see cref="EnvironmentRule"/> and honoured during database-target
    /// rule matching — previously it was silently ignored because only
    /// <c>ColoringRule.Pattern</c> was copied into <c>EnvironmentRule</c>.
    /// </summary>
    public class Pr247_EnvironmentMatcherFix
    {
        // Simulates a rule created from ColoringRule.DatabaseName = "Prod*"
        // (the dedicated database-pattern field) with Pattern left empty —
        // which is the broken case before the fix.
        private static EnvironmentRule MakeDbRule(string databaseName, string pattern = "") =>
            new(
                order: 0,
                pattern: pattern,
                matchTarget: EnvironmentMatcher.MatchTargetDatabase,
                databaseName: databaseName,
                color: "#FF4444",
                label: "PRODUCTION");

        [Fact]
        public void DatabaseName_GlobPattern_MatchesTargetDatabase()
        {
            // "Prod*" in DatabaseName should match "ProdDb" on any server.
            var rules = new List<EnvironmentRule> { MakeDbRule("Prod*") };

            var result = EnvironmentMatcher.Match(rules, "any-server", "ProdDb");

            Assert.NotNull(result);
            Assert.Equal("PRODUCTION", result!.Label);
        }

        [Fact]
        public void DatabaseName_GlobPattern_DoesNotMatchDifferentDatabase()
        {
            // "Prod*" should NOT match "StagingDb".
            var rules = new List<EnvironmentRule> { MakeDbRule("Prod*") };

            var result = EnvironmentMatcher.Match(rules, "any-server", "StagingDb");

            Assert.Null(result);
        }

        [Fact]
        public void DatabaseName_MatchesOnAnyServer()
        {
            // The database rule is server-agnostic — it must match regardless of which
            // server name is supplied.
            var rules = new List<EnvironmentRule> { MakeDbRule("Prod*") };

            Assert.NotNull(EnvironmentMatcher.Match(rules, "dev-sql01", "ProdDb"));
            Assert.NotNull(EnvironmentMatcher.Match(rules, null, "ProdDb"));
            Assert.NotNull(EnvironmentMatcher.Match(rules, "localhost", "ProdDb"));
        }

        [Fact]
        public void DatabaseName_TakesPrecedenceOverPattern()
        {
            // When DatabaseName is set, it must be used instead of Pattern.
            // Pattern = "*Staging*" would match "StagingDb" — but DatabaseName = "Prod*"
            // should win, so "StagingDb" must NOT match.
            var rule = MakeDbRule(databaseName: "Prod*", pattern: "*Staging*");
            var rules = new List<EnvironmentRule> { rule };

            Assert.Null(EnvironmentMatcher.Match(rules, "any-server", "StagingDb"));
            Assert.NotNull(EnvironmentMatcher.Match(rules, "any-server", "ProdDb"));
        }

        [Fact]
        public void DatabaseName_FallsBackToPattern_WhenDatabaseNameIsEmpty()
        {
            // Backward-compat: rules without a DatabaseName (empty string) must still
            // match via the Pattern field, as they did before the fix.
            var rule = MakeDbRule(databaseName: "", pattern: "Prod*");
            var rules = new List<EnvironmentRule> { rule };

            Assert.NotNull(EnvironmentMatcher.Match(rules, "any-server", "ProdDb"));
        }

        [Fact]
        public void DatabaseName_CaseInsensitiveMatch()
        {
            // Pattern matching is case-insensitive (glob contract).
            var rules = new List<EnvironmentRule> { MakeDbRule("Prod*") };

            Assert.NotNull(EnvironmentMatcher.Match(rules, "any-server", "proddb"));
            Assert.NotNull(EnvironmentMatcher.Match(rules, "any-server", "PRODORDERS"));
        }
    }
}
