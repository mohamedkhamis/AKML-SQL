using Xunit;
using AkmlSql.Core.Update;

namespace AkmlSql.Core.Tests.Update
{
    /// <summary>
    /// Spec 036 US5 / FR-037: a version is an update only when it is strictly newer; equal and
    /// older versions report "no update". SemVer pre-release suffixes are stripped before the
    /// <see cref="System.Version"/> comparison (the updater's historical behaviour, promoted
    /// to <see cref="VersionComparer"/> so it is testable).
    /// </summary>
    public class VersionComparerTests
    {
        [Theory]
        [InlineData("1.26.0903.0900", "1.26.0901.1502")] // same major, newer build stamp
        [InlineData("2.0.0", "1.999.9999.9999")]
        [InlineData("1.0.1", "1.0.0")]
        public void IsNewer_StrictlyNewer_ReturnsTrue(string latest, string current)
        {
            Assert.True(VersionComparer.IsNewer(latest, current));
        }

        [Theory]
        [InlineData("1.26.0901.1502", "1.26.0901.1502")] // equal is not an update
        [InlineData("1.26.0901.1502", "1.26.0903.0900")] // older is not an update
        [InlineData("1.0.0", "1.0.1")]
        [InlineData("0.9.9", "1.0.0")]
        public void IsNewer_EqualOrOlder_ReturnsFalse(string latest, string current)
        {
            Assert.False(VersionComparer.IsNewer(latest, current));
        }

        [Theory]
        [InlineData("1.2.4-beta", "1.2.3", true)]   // newer after the suffix is stripped
        [InlineData("1.2.3-beta", "1.2.3", false)]  // equal after stripping -> no update
        [InlineData("1.2.3-rc.1", "1.2.3", false)]
        [InlineData("1.2.3", "1.2.4-beta", false)]  // current newer than stripped latest
        public void IsNewer_PreReleaseSuffixes_AreStrippedBeforeComparison(
            string latest, string current, bool expected)
        {
            Assert.Equal(expected, VersionComparer.IsNewer(latest, current));
        }

        [Theory]
        [InlineData("not-a-version", "1.0.0")]
        [InlineData("1.0.0", "junk")]
        [InlineData("", "1.0.0")]
        [InlineData("1.0.0", "")]
        public void IsNewer_UnparseableInput_ReturnsFalse(string latest, string current)
        {
            // A garbage manifest must never present as an update.
            Assert.False(VersionComparer.IsNewer(latest, current));
        }

        [Theory]
        [InlineData("1.2.3-beta.1", "1.2.3")]
        [InlineData("1.2.3", "1.2.3")]
        [InlineData("1.2.3.4-nightly", "1.2.3.4")]
        public void StripPreRelease_RemovesTheDashSuffix(string version, string expected)
        {
            Assert.Equal(expected, VersionComparer.StripPreRelease(version));
        }
    }
}
