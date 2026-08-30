using System.Text.RegularExpressions;

namespace AkmlSql.Site.Analytics;

/// <summary>Parsed shape of a user-agent string. Every field falls back to a safe default.</summary>
/// <param name="Browser">Browser family: Chrome, Edge, Firefox, Safari, Opera, Samsung, curl, bot, other.</param>
/// <param name="BrowserVersion">Major version as text ("120"), or null when not stated.</param>
/// <param name="Os">OS family: Windows, macOS, iOS, Android, Linux, ChromeOS, other.</param>
/// <param name="OsVersion">OS version as text ("10", "14.2"), or null when not stated.</param>
/// <param name="Device">Form factor: desktop, mobile, tablet, bot.</param>
public sealed record UserAgentDetails(
    string Browser,
    string? BrowserVersion,
    string Os,
    string? OsVersion,
    string Device);

/// <summary>
/// User-agent parsing beyond the single coarse family bucket the site recorded before.
/// <para>
/// Deliberately a focused heuristic rather than a UA-parsing dependency: this needs to answer
/// "how much of my traffic is phones", "do I still need to support old Safari" and "which OS",
/// not to identify every crawler ever shipped. Order matters throughout — Edge's UA contains
/// both "Chrome" and "Safari", Chrome's contains "Safari", and most mobile UAs also name a
/// desktop OS token.
/// </para>
/// </summary>
public static class UserAgentDetailsParser
{
    private static readonly Regex VersionAfter = new(
        @"(?:Edg(?:A|iOS)?|Chrome|CriOS|Firefox|FxiOS|Version|OPR|Opera|SamsungBrowser)/(?<v>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WindowsNt = new(@"Windows NT (?<v>\d+\.\d+)", RegexOptions.Compiled);
    private static readonly Regex MacOs = new(@"Mac OS X (?<v>\d+([._]\d+)*)", RegexOptions.Compiled);
    private static readonly Regex IosVersion = new(@"OS (?<v>\d+([._]\d+)*) like Mac OS X", RegexOptions.Compiled);
    private static readonly Regex AndroidVersion = new(@"Android (?<v>\d+(\.\d+)*)", RegexOptions.Compiled);

    /// <summary>Windows NT kernel version → the name people actually use.</summary>
    private static readonly Dictionary<string, string> WindowsNames = new(StringComparer.Ordinal)
    {
        ["10.0"] = "10/11",
        ["6.3"] = "8.1",
        ["6.2"] = "8",
        ["6.1"] = "7",
    };

    /// <summary>Values used when the header is absent — kept distinct from a parsed "other".</summary>
    public static readonly UserAgentDetails Unknown = new("other", null, "other", null, "desktop");

    /// <summary>Parses a user-agent header into its family, versions and form factor.</summary>
    public static UserAgentDetails Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return Unknown;
        }

        // Bots first: a crawler's UA usually also claims a browser and an OS, and misreading one
        // as a visitor is what inflated the old metrics.
        var browser = UserAgentBuckets.FromUserAgent(userAgent);
        if (browser == "bot")
        {
            return new UserAgentDetails("bot", null, DetectOs(userAgent, out var botOsVersion), botOsVersion, "bot");
        }

        var os = DetectOs(userAgent, out var osVersion);
        return new UserAgentDetails(
            DetectBrowser(userAgent, browser),
            DetectBrowserVersion(userAgent),
            os,
            osVersion,
            DetectDevice(userAgent, os));
    }

    /// <summary>
    /// Refines the coarse bucket with the families it does not distinguish (Opera and Samsung
    /// Internet both otherwise read as Chrome; iOS Chrome/Firefox report as CriOS/FxiOS).
    /// </summary>
    private static string DetectBrowser(string ua, string bucket)
    {
        if (ua.Contains("OPR/", StringComparison.Ordinal) || ua.Contains("Opera", StringComparison.Ordinal))
        {
            return "Opera";
        }

        if (ua.Contains("SamsungBrowser/", StringComparison.Ordinal))
        {
            return "Samsung";
        }

        if (ua.Contains("CriOS/", StringComparison.Ordinal))
        {
            return "Chrome";
        }

        if (ua.Contains("FxiOS/", StringComparison.Ordinal))
        {
            return "Firefox";
        }

        return bucket;
    }

    private static string? DetectBrowserVersion(string ua)
    {
        // Later tokens win: Safari reports its real version in "Version/17.1", and Edge's "Edg/"
        // appears after the Chrome token it shadows.
        string? version = null;
        foreach (Match match in VersionAfter.Matches(ua))
        {
            version = match.Groups["v"].Value;
        }

        return version;
    }

    private static string DetectOs(string ua, out string? version)
    {
        version = null;

        // iOS/iPadOS before macOS: an iPad UA contains "Mac OS X".
        if (ua.Contains("iPhone", StringComparison.Ordinal)
            || ua.Contains("iPad", StringComparison.Ordinal)
            || ua.Contains("iPod", StringComparison.Ordinal))
        {
            version = Normalize(IosVersion.Match(ua));
            return "iOS";
        }

        // Android before Linux: every Android UA also says "Linux".
        if (ua.Contains("Android", StringComparison.Ordinal))
        {
            version = Normalize(AndroidVersion.Match(ua));
            return "Android";
        }

        if (ua.Contains("CrOS", StringComparison.Ordinal))
        {
            return "ChromeOS";
        }

        if (ua.Contains("Windows NT", StringComparison.Ordinal))
        {
            var raw = WindowsNt.Match(ua);
            if (raw.Success)
            {
                // 10 and 11 are indistinguishable from the UA string alone (both report 10.0).
                version = WindowsNames.GetValueOrDefault(raw.Groups["v"].Value, raw.Groups["v"].Value);
            }

            return "Windows";
        }

        if (ua.Contains("Mac OS X", StringComparison.Ordinal) || ua.Contains("Macintosh", StringComparison.Ordinal))
        {
            version = Normalize(MacOs.Match(ua));
            return "macOS";
        }

        if (ua.Contains("Linux", StringComparison.Ordinal) || ua.Contains("X11", StringComparison.Ordinal))
        {
            return "Linux";
        }

        return "other";
    }

    /// <summary>Mac and iOS report versions with underscores ("10_15_7"); show them with dots.</summary>
    private static string? Normalize(Match match) =>
        match.Success ? match.Groups["v"].Value.Replace('_', '.') : null;

    private static string DetectDevice(string ua, string os)
    {
        if (ua.Contains("iPad", StringComparison.Ordinal)
            || ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
            // Android phones say "Mobile"; Android tablets omit it.
            || (os == "Android" && !ua.Contains("Mobile", StringComparison.Ordinal)))
        {
            return "tablet";
        }

        if (os is "iOS" or "Android" || ua.Contains("Mobile", StringComparison.Ordinal))
        {
            return "mobile";
        }

        return "desktop";
    }
}
