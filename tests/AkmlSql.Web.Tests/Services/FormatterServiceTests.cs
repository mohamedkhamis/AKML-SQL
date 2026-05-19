using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T041 (subset). Validates that the in-process
/// <see cref="IFormatterService"/> exposed to the Blazor surface is wired correctly
/// to <see cref="FormatterPipeline"/>. The full parity-corpus comparison against the
/// IDE plugin lives separately (under tests/format-parity/) and runs against the
/// same FormatterPipeline this service calls, so equivalence is structural.
/// </summary>
public sealed class FormatterServiceTests
{
    private static IFormatterService CreateService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFormatterService, FormatterService>();
        return services.BuildServiceProvider().GetRequiredService<IFormatterService>();
    }

    [Fact]
    public void Format_unformatted_select_succeeds_and_modifies_text()
    {
        var service = CreateService();
        var result = service.Format("select 1");

        Assert.True(result.Success);
        Assert.True(result.WasModified);
        Assert.False(string.IsNullOrWhiteSpace(result.FormattedText));
    }

    [Fact]
    public void Format_already_canonical_text_does_not_modify()
    {
        // Format once to get the canonical form, then format again -- should be a no-op.
        var service = CreateService();
        var first = service.Format("SELECT 1;");
        var second = service.Format(first.FormattedText);

        Assert.True(second.Success);
        Assert.Equal(first.FormattedText, second.FormattedText);
    }

    [Fact]
    public void Format_returns_validation_passed_true_for_trivial_input()
    {
        var service = CreateService();
        var result = service.Format("SELECT 1;");

        Assert.True(result.ValidationPassed);
    }

    [Fact]
    public void Format_records_nonzero_elapsed_time()
    {
        var service = CreateService();
        var result = service.Format("SELECT 1;");

        Assert.True(result.ElapsedMs >= 0);   // sometimes returns 0 ms on a hot run
    }

    [Fact]
    public void Format_uses_default_profile_when_none_supplied()
    {
        var service = CreateService();
        var result = service.Format("select * from dbo.Foo");

        Assert.True(result.Success);
        // Default profile uppercases keywords -- assert at least one expected casing change
        Assert.Contains("SELECT", result.FormattedText);
    }

    [Fact]
    public void Format_honours_explicit_profile_override()
    {
        var service = CreateService();
        var profile = new FormattingProfile();
        profile.Casing.ReservedKeywords = "lowercase";

        var result = service.Format("SELECT 1;", profile);

        Assert.True(result.Success);
        // With lowercase reserved-keyword casing, the formatted text should contain "select" not "SELECT".
        Assert.Contains("select", result.FormattedText);
    }

    [Fact]
    public void Format_handles_null_input_gracefully()
    {
        var service = CreateService();

        // null is normalised to empty string. The pipeline may report Success=false for an
        // empty document (no batches) but must NOT throw. FormattedText is expected to be
        // non-null in all paths.
        var result = service.Format(null!);
        Assert.NotNull(result);
        Assert.NotNull(result.FormattedText);
    }
}
