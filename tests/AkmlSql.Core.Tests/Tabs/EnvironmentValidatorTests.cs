using System.Collections.Generic;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs
{
    /// <summary>
    /// Spec 040 (T155, OPT-08, data-model §1.2 "Validation") — the Edit environments dialog's
    /// checks: names unique (case-insensitive) and 1–40 characters, colours <c>#RRGGBB</c>, and
    /// an environment that rules use can't be deleted (the refusal names the rules).
    /// </summary>
    public class EnvironmentValidatorTests
    {
        private static TabEnvironment Env(string name, string color = "#FF4444") => new() { Name = name, Color = color };

        // ── Names ────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("P")]
        [InlineData("PRODUCTION")]
        [InlineData("  Staging  ")]                                  // judged trimmed
        [InlineData("1234567890123456789012345678901234567890")]     // exactly 40
        public void ValidateName_AcceptsOneToFortyCharacters(string name)
        {
            Assert.Null(EnvironmentValidator.ValidateName(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("12345678901234567890123456789012345678901")]    // 41
        public void ValidateName_RejectsEmptyOrLongerThanForty(string? name)
        {
            Assert.NotNull(EnvironmentValidator.ValidateName(name));
        }

        [Fact]
        public void Validate_RejectsDuplicateNames_IgnoringCaseAndSpaces()
        {
            var errors = EnvironmentValidator.Validate(new[] { Env("Production"), Env("DEV"), Env(" PRODUCTION ") });

            var error = Assert.Single(errors);
            Assert.Contains("production", error, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Validate_ReportsADuplicateOnce()
        {
            var errors = EnvironmentValidator.Validate(new[] { Env("DEV"), Env("dev"), Env("Dev") });

            Assert.Single(errors);
        }

        [Fact]
        public void Validate_AcceptsTheDefaults()
        {
            Assert.Empty(EnvironmentValidator.Validate(TabEnvironment.CreateDefaults()));
        }

        [Fact]
        public void Validate_RequiresAtLeastOneEnvironment()
        {
            Assert.Single(EnvironmentValidator.Validate(new List<TabEnvironment>()));
        }

        [Fact]
        public void Validate_ReportsAnEmptyName()
        {
            Assert.Single(EnvironmentValidator.Validate(new[] { Env("DEV"), Env("") }));
        }

        // ── Colours ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData("#FF4444")]
        [InlineData("#ff4444")]
        [InlineData("#00bCd4")]
        public void IsValidColor_AcceptsHashAndSixHexDigits(string color)
        {
            Assert.True(EnvironmentValidator.IsValidColor(color));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("FF4444")]        // no #
        [InlineData("#F44")]          // short form
        [InlineData("#FF44444")]      // seven digits
        [InlineData("#AAFF4444")]     // alpha
        [InlineData("#GG4444")]       // not hex
        [InlineData("red")]
        [InlineData(" #FF4444")]
        [InlineData("#FF4444\n")]
        public void IsValidColor_RejectsAnythingElse(string? color)
        {
            Assert.False(EnvironmentValidator.IsValidColor(color));
        }

        [Fact]
        public void Validate_ReportsAnInvalidColor_NamingTheEnvironment()
        {
            var error = Assert.Single(EnvironmentValidator.Validate(new[] { Env("DEV", "green") }));

            Assert.Contains("DEV", error);
        }

        // ── Delete ───────────────────────────────────────────────────────────

        private static readonly List<ColoringRule> Rules = new()
        {
            new ColoringRule { Pattern = "*PROD*,*LIVE*", Environment = "PRODUCTION" },
            new ColoringRule { Pattern = "SQL01", DatabaseName = "Sales", Environment = "production" },
            new ColoringRule { Pattern = "*DEV*", Environment = "DEV" },
            new ColoringRule { Pattern = "", DatabaseName = "ProdDb", MatchTarget = EnvironmentMatcher.MatchTargetDatabase, Environment = "PRODUCTION" },
        };

        [Fact]
        public void CanDelete_IsFalseWhenARuleUsesTheEnvironment_AndNamesTheRules()
        {
            var canDelete = EnvironmentValidator.CanDelete("Production", Rules, out var usedBy);

            Assert.False(canDelete);
            Assert.Equal(new[] { "*PROD*,*LIVE*", "SQL01 / Sales", "* / ProdDb" }, usedBy);
        }

        [Fact]
        public void CanDelete_IsTrueWhenNoRuleUsesTheEnvironment()
        {
            var canDelete = EnvironmentValidator.CanDelete("AZURE", Rules, out var usedBy);

            Assert.True(canDelete);
            Assert.Empty(usedBy);
        }

        [Fact]
        public void DeleteRefusedMessage_NamesTheEnvironmentAndTheRules()
        {
            EnvironmentValidator.CanDelete("DEV", Rules, out var usedBy);

            var message = EnvironmentValidator.DeleteRefusedMessage("DEV", usedBy);

            Assert.Contains("'DEV'", message);
            Assert.Contains("*DEV*", message);
        }
    }
}
