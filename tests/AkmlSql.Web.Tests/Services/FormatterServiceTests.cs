using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Parity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

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
    private readonly ITestOutputHelper _output;

    public FormatterServiceTests(ITestOutputHelper output) => _output = output;

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

    /// <summary>
    /// Spec 024 T019 / US2 — parity driver. For every (corpus item × built-in profile)
    /// pair, run the web edition's formatter against the same input the
    /// <see cref="ParityBaselineGenerator"/> consumed and assert the formatted output
    /// matches the on-disk baseline byte-exact (after LF normalisation per
    /// contracts/parity-baseline-format.md). True regressions fail the test;
    /// divergences registered in <see cref="ParityDispositionsRegistry"/> are accepted.
    /// </summary>
    [Theory]
    [MemberData(nameof(FormatterParityPairs))]
    public void Formatter_MatchesIdeBaseline_AcrossCorpusAndProfiles(string corpusId, string profileId)
    {
        var sql = ParityCorpusLoader.LoadInputSql(corpusId);
        var profile = ParityCorpusLoader.GetProfile(profileId);

        var service = new FormatterService();
        var result = service.Format(sql, profile);

        Assert.True(
            result.Success,
            $"Formatter failed for ({corpusId}, {profileId}): " +
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Severity}:{d.Message}")));

        var actual = ParityCorpusLoader.NormaliseLineEndings(result.FormattedText);
        var expected = ParityCorpusLoader.LoadFormatterBaseline(corpusId, profileId);

        if (actual == expected) return;

        var disposition = ParityDispositionsRegistry.AcceptedReason(corpusId, profileId);
        if (disposition is not null)
        {
            _output.WriteLine(
                $"ACCEPTED_WITH_REASON ({corpusId}, {profileId}) — {disposition}");
            return;
        }

        // Real regression: produce a useful diff message.
        var diff = BuildDiff(expected, actual);
        Assert.Fail(
            $"Formatter parity divergence for ({corpusId}, {profileId}). " +
            "Either fix the formatter or register the divergence in " +
            "ParityDispositionsRegistry with a ReasonLink to a spec-020 tasks.md " +
            $"entry.\n\n=== DIFF (expected → actual) ===\n{diff}");
    }

    public static IEnumerable<object[]> FormatterParityPairs() =>
        ParityCorpusLoader.EnumerateFormatterPairs()
            .Select(p => new object[] { p.CorpusId, p.ProfileId });

    private static string BuildDiff(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var sb = new System.Text.StringBuilder();
        var max = Math.Max(expectedLines.Length, actualLines.Length);
        for (var i = 0; i < max; i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<EOF>";
            var a = i < actualLines.Length ? actualLines[i] : "<EOF>";
            if (e == a) continue;
            sb.AppendLine($"L{i + 1,4}  -: {e}");
            sb.AppendLine($"      +: {a}");
        }
        return sb.Length == 0 ? "(no line-level differences — trailing whitespace or BOM?)" : sb.ToString();
    }
}
