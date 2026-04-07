using System.Reflection;

namespace AkmlSql.Core
{
    /// <summary>
    /// Reads the assembly version set by Directory.Build.props at build time.
    /// InformationalVersion format: 1.{commitCount}.{MMddHHmm}
    /// </summary>
    public static class AppVersion
    {
        public static string Current { get; } = ResolveVersion();

        private static string ResolveVersion()
        {
            // Primary: InformationalVersion (set by Directory.Build.props)
            var infoVersion = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrEmpty(infoVersion))
            {
                // Strip git hash suffix if present (e.g. "1.265.04050456+abc123")
                var plusIndex = infoVersion!.IndexOf('+');
                return plusIndex >= 0 ? infoVersion.Substring(0, plusIndex) : infoVersion;
            }

            // Fallback: AssemblyVersion (survives trimming)
            var asmVersion = typeof(AppVersion).Assembly.GetName().Version;
            return asmVersion != null
                ? $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}"
                : "0.0.0";
        }
    }
}
