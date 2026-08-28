namespace AkmlSql.Site.Analytics;

/// <summary>
/// Coarse user-agent bucketing for the metrics tables — just enough to answer "what do visitors
/// browse with" without storing identifying UA strings. Order matters: Edge's UA contains
/// "Chrome" and "Safari", Chrome's contains "Safari", so the most specific tokens are tested first.
/// </summary>
public static class UserAgentBuckets
{
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

        if (userAgent.Contains("curl/", StringComparison.OrdinalIgnoreCase))
        {
            return "curl";
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
