using System;

namespace AkmlSql.Core.Update
{
    /// <summary>
    /// Version comparison for the update channel (spec 036 US5 / FR-037). Promoted out of the
    /// updater's <c>Program.cs</c> so the rule is testable: a version is an update only when
    /// strictly newer, and SemVer pre-release suffixes are stripped before the
    /// <see cref="Version"/> comparison (data-model V17). Unparseable input is never an update.
    /// </summary>
    public static class VersionComparer
    {
        /// <summary>True only when <paramref name="latest"/> is strictly newer than <paramref name="current"/>.</summary>
        public static bool IsNewer(string latest, string current)
        {
            try
            {
                var latestVersion = new Version(StripPreRelease(latest));
                var currentVersion = new Version(StripPreRelease(current));
                return latestVersion > currentVersion;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Drops a SemVer pre-release suffix (<c>"1.2.3-beta.1"</c> → <c>"1.2.3"</c>).</summary>
        public static string StripPreRelease(string version)
        {
            var dashIndex = version.IndexOf('-');
            return dashIndex >= 0 ? version.Substring(0, dashIndex) : version;
        }
    }
}
