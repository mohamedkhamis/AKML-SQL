#nullable enable
using System.Collections.Generic;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Analysis;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T014, OPT-01) — "Show in Error List" is honoured, and an Options change is
    /// re-applied to every live reporter without an edit.
    /// </summary>
    public class ErrorListGateTests
    {
        private sealed class FakeReporter : ErrorListReporter.IReapplicable
        {
            public List<string> Calls { get; } = new List<string>();
            public void Reapply() => Calls.Add("reapply");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ShouldPublish_follows_the_setting(bool on)
            => Assert.Equal(on, ErrorListReporter.ShouldPublish(new CodeAnalysisSettings { ShowInErrorList = on }));

        [Fact]
        public void ReapplyAll_reaches_every_registered_reporter()
        {
            var first = new FakeReporter();
            var second = new FakeReporter();
            ErrorListReporter.Register(first);
            ErrorListReporter.Register(second);
            try
            {
                ErrorListReporter.ReapplyAll();

                Assert.Equal(new[] { "reapply" }, first.Calls);
                Assert.Equal(new[] { "reapply" }, second.Calls);
            }
            finally
            {
                ErrorListReporter.Unregister(first);
                ErrorListReporter.Unregister(second);
            }
        }

        [Fact]
        public void Unregistered_reporters_are_not_reapplied()
        {
            var reporter = new FakeReporter();
            ErrorListReporter.Register(reporter);
            ErrorListReporter.Unregister(reporter);

            ErrorListReporter.ReapplyAll();

            Assert.Empty(reporter.Calls);
        }
    }
}
