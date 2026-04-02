using System.Linq;
using AkmlSql.Engine.Analysis;
using Xunit;

namespace AkmlSql.Core.Tests.Analysis;

public class RuleRegistryTests
{
    [Fact]
    public void Constructor_DiscoversAtLeastOneHundredRules()
    {
        var registry = new RuleRegistry();
        Assert.True(registry.AllRules.Count >= 100,
            $"Expected at least 100 rules, found {registry.AllRules.Count}");
    }

    [Fact]
    public void AllRules_AreSortedByRuleId()
    {
        var registry = new RuleRegistry();
        var ids = registry.AllRules.Select(r => r.RuleId).ToList();
        var sorted = ids.OrderBy(id => id).ToList();
        Assert.Equal(sorted, ids);
    }

    [Fact]
    public void AllRules_HaveUniqueIds()
    {
        var registry = new RuleRegistry();
        var ids = registry.AllRules.Select(r => r.RuleId).ToList();
        var unique = ids.Distinct().ToList();
        Assert.Equal(unique.Count, ids.Count);
    }

    [Fact]
    public void AllRules_HaveNonEmptyCategory()
    {
        var registry = new RuleRegistry();
        Assert.All(registry.AllRules, r => Assert.False(string.IsNullOrWhiteSpace(r.Category)));
    }

    [Fact]
    public void AllRules_HaveNonEmptyRuleId()
    {
        var registry = new RuleRegistry();
        Assert.All(registry.AllRules, r => Assert.False(string.IsNullOrWhiteSpace(r.RuleId)));
    }

    [Fact]
    public void GetEnabledRules_ReturnsAllRules_WhenAllEnabled()
    {
        var registry = new RuleRegistry();
        var settings = new ResolvedAnalysisSettings(); // empty EffectiveRules → all enabled by default
        var enabled = registry.GetEnabledRules(settings);
        Assert.Equal(registry.AllRules.Count, enabled.Count);
    }

    [Fact]
    public void GetEnabledRules_FiltersDisabledRules()
    {
        var registry = new RuleRegistry();
        var settings = new ResolvedAnalysisSettings();
        // Disable a specific rule
        settings.EffectiveRules["PE001"] = new ResolvedRuleConfig { Enabled = false };

        var enabled = registry.GetEnabledRules(settings);
        Assert.DoesNotContain(enabled, r => r.RuleId == "PE001");
        Assert.Equal(registry.AllRules.Count - 1, enabled.Count);
    }

    [Theory]
    [InlineData("PE001")]
    [InlineData("PE003")]
    [InlineData("BP004")]
    [InlineData("SE001")]
    [InlineData("SE002")]
    [InlineData("NM002")]
    [InlineData("DEP001")]
    [InlineData("ST001")]
    [InlineData("DE001")]
    [InlineData("EX001")]
    public void AllRules_ContainsExpectedRuleId(string ruleId)
    {
        var registry = new RuleRegistry();
        Assert.Contains(registry.AllRules, r => r.RuleId == ruleId);
    }
}
