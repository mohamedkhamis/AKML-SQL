using System.Collections.Generic;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs
{
    public class EnvironmentMatcherTests
    {
        private static readonly List<EnvironmentRule> DefaultRules = new()
        {
            new(0, "*PROD*,*LIVE*", "serverName", "#FF4444", "PRODUCTION"),
            new(1, "*STG*,*UAT*,*STAGING*", "serverName", "#FFB800", "STAGING"),
            new(2, "*DEV*,*LOCAL*,localhost,(local)", "serverName", "#44BB44", "DEV"),
            new(3, "*.database.windows.net", "serverName", "#4488FF", "AZURE"),
        };

        [Theory]
        [InlineData("SQLPROD01", "PRODUCTION")]
        [InlineData("LIVE-SERVER", "PRODUCTION")]
        [InlineData("prod-sql.corp.net", "PRODUCTION")]
        [InlineData("STG-SQL", "STAGING")]
        [InlineData("UAT-DB", "STAGING")]
        [InlineData("DEV-SQL01", "DEV")]
        [InlineData("localhost", "DEV")]
        [InlineData("(local)", "DEV")]
        [InlineData("myserver.database.windows.net", "AZURE")]
        public void Match_DefaultRules_ReturnsExpectedLabel(string server, string expectedLabel)
        {
            var result = EnvironmentMatcher.Match(DefaultRules, server);
            Assert.NotNull(result);
            Assert.Equal(expectedLabel, result!.Label);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("UNKNOWN-SERVER")]
        [InlineData("my-custom-server")]
        public void Match_NoMatchingRule_ReturnsNull(string? server)
        {
            var result = EnvironmentMatcher.Match(DefaultRules, server);
            Assert.Null(result);
        }

        [Fact]
        public void Match_PriorityOrder_LowestOrderWins()
        {
            var result = EnvironmentMatcher.Match(DefaultRules, "PRODDEV");
            Assert.NotNull(result);
            Assert.Equal("PRODUCTION", result!.Label);
        }

        [Fact]
        public void Match_EmptyRules_ReturnsNull()
        {
            var result = EnvironmentMatcher.Match(new List<EnvironmentRule>(), "PROD-SQL");
            Assert.Null(result);
        }

        [Theory]
        [InlineData("*PROD*", "SQLPROD01", true)]
        [InlineData("*PROD*", "prod-sql", true)]
        [InlineData("*.database.windows.net", "x.database.windows.net", true)]
        [InlineData("DEV*", "DEV-SQL", true)]
        [InlineData("DEV*", "PRODUCTION", false)]
        [InlineData("localhost", "localhost", true)]
        [InlineData("localhost", "LOCALHOST", true)]
        [InlineData("localhost", "localhost2", false)]
        [InlineData("*", "anything", true)]
        public void GlobMatch_VariousPatterns(string glob, string value, bool expected)
        {
            Assert.Equal(expected, EnvironmentMatcher.GlobMatch(glob, value));
        }

        [Theory]
        [InlineData("*PROD*,*LIVE*", "SQLPROD01", true)]
        [InlineData("*PROD*,*LIVE*", "LIVE-SERVER", true)]
        [InlineData("*PROD*,*LIVE*", "DEV-SQL", false)]
        [InlineData("localhost,(local)", "(local)", true)]
        public void MatchesPattern_CommaDelimited(string pattern, string value, bool expected)
        {
            Assert.Equal(expected, EnvironmentMatcher.MatchesPattern(pattern, value));
        }
    }
}
