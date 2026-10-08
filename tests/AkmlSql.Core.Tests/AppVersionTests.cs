using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace AkmlSql.Core.Tests
{
    /// <summary>
    /// The version and the build instant every build stamps (src/Directory.Build.props): the
    /// build date About and Options show comes from them, never from a constant in source.
    /// </summary>
    public class AppVersionTests
    {
        [Fact]
        public void The_build_date_is_the_instant_the_version_names()
        {
            // The date shown beside the version always agrees with it.
            var fromVersion = AppVersion.FromVersion(AppVersion.Current);
            Assert.NotNull(fromVersion);
            Assert.Equal(fromVersion, AppVersion.BuildTimestampUtc);
        }

        [Fact]
        public void Every_build_also_stamps_its_compile_time()
        {
            // The fallback for a version of another shape: the compile instant, which follows the
            // version's instant (build.ps1 fixes the version when the build starts).
            var stamp = typeof(AppVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(a => a.Key == "BuildTimestamp");
            Assert.NotNull(stamp);
            var compiled = AppVersion.ParseTimestamp(stamp!.Value);
            Assert.NotNull(compiled);
            Assert.InRange(compiled!.Value - AppVersion.BuildTimestampUtc!.Value, TimeSpan.FromMinutes(-1), TimeSpan.FromHours(6));
        }

        [Fact]
        public void An_assembly_version_reads_back_with_all_four_parts()
        {
            Assert.Equal("1.26.1007.0509", AppVersion.FromAssemblyVersion(new Version(1, 26, 1007, 509)));
            Assert.Equal("1.27.0107.0005", AppVersion.FromAssemblyVersion(new Version(1, 27, 107, 5)));
            Assert.Equal("1.0.0", AppVersion.FromAssemblyVersion(new Version(1, 0, 0)));
            Assert.Equal("0.0.0", AppVersion.FromAssemblyVersion(null));
        }

        [Fact]
        public void A_version_reads_back_as_its_build_instant_in_utc()
        {
            // 1.YY.MMDD.HHmm is written at UTC+2.
            var utc = AppVersion.FromVersion("1.26.1007.0429");
            Assert.Equal(new DateTime(2026, 10, 7, 2, 29, 0, DateTimeKind.Utc), utc);
            Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
            Assert.Equal(new DateTime(2026, 12, 31, 22, 15, 0, DateTimeKind.Utc), AppVersion.FromVersion("1.27.0101.0015"));
        }

        [Theory]
        [InlineData("1.0.0")]
        [InlineData("1.265.04051456")]      // the earlier commit-count format
        [InlineData("1.26.1307.0429")]      // no 13th month
        [InlineData("")]
        [InlineData(null)]
        public void Other_version_shapes_have_no_build_instant(string? version)
        {
            Assert.Null(AppVersion.FromVersion(version));
        }

        [Fact]
        public void A_stamp_is_utc_to_the_minute()
        {
            Assert.Equal(new DateTime(2026, 10, 7, 1, 29, 0, DateTimeKind.Utc), AppVersion.ParseTimestamp("2026-10-07T01:29Z"));
            Assert.Equal(DateTimeKind.Utc, AppVersion.ParseTimestamp("2026-10-07T01:29Z")!.Value.Kind);
            Assert.Null(AppVersion.ParseTimestamp("2026-10-07"));
            Assert.Null(AppVersion.ParseTimestamp(null));
        }

        [Fact]
        public void The_build_date_is_shown_in_local_time()
        {
            var local = AppVersion.BuildTimestampUtc!.Value.ToLocalTime();
            Assert.Equal(local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), AppVersion.BuildDate);
            Assert.Equal(local.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), AppVersion.BuildDateTime);
        }
    }
}
