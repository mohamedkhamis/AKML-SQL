namespace AkmlSql.Site.Analytics;

/// <summary>
/// Coarse user-agent bucketing for the metrics tables — just enough to answer "what do visitors
/// browse with" without storing identifying UA strings. Order matters: Edge's UA contains
/// "Chrome" and "Safari", Chrome's contains "Safari", so the most specific tokens are tested first.
/// </summary>
public static class UserAgentBuckets
{
    /// <summary>
    /// Non-browser HTTP clients, mapped to a family name. Matched before the browser tokens
    /// because several of these also send a browser-shaped UA.
    /// </summary>
    private static readonly (string Token, string Family)[] ScriptedClients =
    [
        ("curl/", "curl"),
        ("Wget", "wget"),
        ("python-requests", "python"),
        ("python-urllib", "python"),
        ("aiohttp", "python"),
        ("PowerShell", "powershell"),
        ("WindowsPowerShell", "powershell"),
        ("Go-http-client", "go"),
        ("okhttp", "okhttp"),
        ("Java/", "java"),
        ("libwww-perl", "perl"),
        ("HTTPie", "httpie"),
        ("PostmanRuntime", "postman"),
        ("insomnia", "insomnia"),
    ];

    /// <summary>
    /// Families that are automation rather than a person reading a page: crawlers plus every
    /// scripted client above.
    /// <para>
    /// This matters because "visits" should mean people. On the live site, scripted traffic was
    /// 38% of the browser table — it inflated visit counts, unique visitors, session shape and
    /// the top-pages ranking, exactly as crawler traffic did before it was excluded.
    /// </para>
    /// <para>
    /// Downloads are treated differently: fetching an installer with curl or PowerShell is a real
    /// acquisition, so only crawlers are excluded there. See AnalyticsStore.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> AutomationFamilies =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "bot", "curl", "wget", "python", "powershell", "go", "okhttp", "java", "perl",
            "httpie", "postman", "insomnia",
        };

    /// <summary>True when a family is automation rather than a human-driven browser.</summary>
    public static bool IsAutomation(string? family) =>
        family is not null && AutomationFamilies.Contains(family);

    /// <summary>Maps a UA string to "Chrome" / "Firefox" / "Safari" / "Edge" / "curl" / "bot" / "other".</summary>
    public static string FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "other";
        }

        if (userAgent.Contains("bot", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("crawler", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("spider", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("slurp", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("headless", StringComparison.OrdinalIgnoreCase))
        {
            return "bot";
        }

        // Scripted clients. Like crawlers these are automation, not readers — see
        // AutomationFamilies for why that distinction matters to the visit figures.
        foreach (var (token, family) in ScriptedClients)
        {
            if (userAgent.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return family;
            }
        }

        // Desktop Edge ("Edg/"), Android ("EdgA/"), iOS ("EdgiOS/").
        if (userAgent.Contains("Edg/", StringComparison.Ordinal)
            || userAgent.Contains("EdgA/", StringComparison.Ordinal)
            || userAgent.Contains("EdgiOS/", StringComparison.Ordinal))
        {
            return "Edge";
        }

        if (userAgent.Contains("Chrome/", StringComparison.Ordinal)
            || userAgent.Contains("Chromium/", StringComparison.Ordinal))
        {
            return "Chrome";
        }

        if (userAgent.Contains("Firefox/", StringComparison.Ordinal))
        {
            return "Firefox";
        }

        if (userAgent.Contains("Safari/", StringComparison.Ordinal))
        {
            return "Safari";
        }

        return "other";
    }
}
