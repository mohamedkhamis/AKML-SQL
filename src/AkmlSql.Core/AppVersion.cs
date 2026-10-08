using System;
using System.Globalization;
using System.Reflection;

namespace AkmlSql.Core
{
    /// <summary>
    /// The version and build instant Directory.Build.props stamps into every assembly at build
    /// time. Version format: 1.YY.MMDD.HHmm (the build's date and time at UTC+2, e.g. 1.26.1007.0429).
    /// </summary>
    public static class AppVersion
    {
        // Static initializers run in declaration order: this must precede BuildTimestampUtc.
        /// <summary>The fixed offset the version's date and time are written in (Directory.Build.props, build.ps1).</summary>
        internal static readonly TimeSpan VersionOffset = TimeSpan.FromHours(2);

        public static string Current { get; } = ResolveVersion();

        /// <summary>
        /// When this build was made, in UTC (to the minute): read back from <see cref="Current"/>,
        /// which every build writes as its date and time, so the date shown always agrees with the
        /// version shown; for a version of another shape, the assembly's <c>BuildTimestamp</c>
        /// metadata (its compile time); null when there is neither.
        /// </summary>
        public static DateTime? BuildTimestampUtc { get; } = ResolveBuildTimestamp();

        /// <summary>The build date in the reader's local time (yyyy-MM-dd), or "unknown".</summary>
        public static string BuildDate =>
            BuildTimestampUtc is DateTime t ? t.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "unknown";

        /// <summary>The build date and time in the reader's local time (yyyy-MM-dd HH:mm), or "unknown".</summary>
        public static string BuildDateTime =>
            BuildTimestampUtc is DateTime t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "unknown";

        /// <summary>The format of the <c>BuildTimestamp</c> metadata (UTC, to the minute).</summary>
        internal const string TimestampFormat = "yyyy-MM-dd'T'HH:mm'Z'";

        private static string ResolveVersion()
        {
            // Primary: InformationalVersion (set by Directory.Build.props)
            var infoVersion = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrEmpty(infoVersion))
            {
                // Strip git hash suffix if present (e.g. "1.26.1007.0429+abc123")
                var plusIndex = infoVersion!.IndexOf('+');
                return plusIndex >= 0 ? infoVersion.Substring(0, plusIndex) : infoVersion;
            }

            // Fallback: AssemblyVersion (survives trimming)
            return FromAssemblyVersion(typeof(AppVersion).Assembly.GetName().Version);
        }

        /// <summary>
        /// An assembly version as the product writes its version: all four parts, MMDD and HHmm
        /// zero-padded (1.26.0107.0509, not 1.26.107.509). Dropping the last part made a same-day
        /// release look newer than the installed build, so the updater offered it to itself.
        /// </summary>
        internal static string FromAssemblyVersion(Version? version) =>
            version == null ? "0.0.0"
            : version.Revision >= 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2:0000}.{3:0000}", version.Major, version.Minor, version.Build, version.Revision)
                : string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", version.Major, version.Minor, version.Build);

        private static DateTime? ResolveBuildTimestamp()
        {
            if (FromVersion(Current) is DateTime fromVersion) return fromVersion;
            foreach (var metadata in typeof(AppVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (metadata.Key == "BuildTimestamp" && ParseTimestamp(metadata.Value) is DateTime stamped)
                    return stamped;
            }
            return null;
        }

        /// <summary>A <c>BuildTimestamp</c> value as a UTC time, or null when it isn't one.</summary>
        internal static DateTime? ParseTimestamp(string? value) =>
            DateTime.TryParseExact(value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
                ? utc
                : (DateTime?)null;

        /// <summary>
        /// The build instant a 1.YY.MMDD.HHmm version encodes, in UTC (the version is written at
        /// <see cref="VersionOffset"/>); null for any other version shape.
        /// </summary>
        internal static DateTime? FromVersion(string? version)
        {
            var parts = (version ?? string.Empty).Split('.');
            if (parts.Length < 4 || parts[1].Length != 2 || parts[2].Length != 4 || parts[3].Length != 4) return null;
            if (!DateTime.TryParseExact("20" + parts[1] + parts[2] + parts[3], "yyyyMMddHHmm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var written))
                return null;
            return DateTime.SpecifyKind(written - VersionOffset, DateTimeKind.Utc);
        }
    }
}
