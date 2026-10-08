#nullable enable
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Analysis;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>Spec 040 (T016, OPT-01) — "Analyze while typing" gates edit-triggered analysis.</summary>
    public class AnalysisTriggerPolicyTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void Edits_analyse_only_when_enabled_and_run_on_type(bool enabled, bool runOnType, bool expected)
            => Assert.Equal(expected, AnalysisController.ShouldAnalyzeOnEdit(new CodeAnalysisSettings { Enabled = enabled, RunOnType = runOnType }));
    }
}
