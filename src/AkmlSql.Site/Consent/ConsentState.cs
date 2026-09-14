namespace AkmlSql.Site.Consent;

/// <summary>
/// Whether a visitor has agreed to being identified across visits (spec 038 US5, FR-043).
/// <para>
/// This exists because the owner's 2026-09-12 decisions — store the full client address, issue a
/// persistent identifier, keep both for 365 days — are exactly the collection that requires asking
/// first. The enum is deliberately three-valued: "has not answered" is a real state and it is
/// <b>not</b> consent.
/// </para>
/// </summary>
public enum ConsentState
{
    /// <summary>
    /// No choice recorded. A first-time visitor. Treated as "do not identify" — silence is never
    /// consent (FR-043b) — while still being asked, unlike <see cref="Denied"/>.
    /// </summary>
    Unknown,

    /// <summary>The visitor agreed. The only state in which <c>ip</c> and <c>visitor_id</c> are written.</summary>
    Granted,

    /// <summary>
    /// The visitor declined, or explicitly dismissed the request. Recorded so they are never asked
    /// again (FR-046), and stored independently of the identity cookie so refusing does not require
    /// accepting the very thing being refused.
    /// </summary>
    Denied,
}

/// <summary>Serialisation of <see cref="ConsentState"/> for the cookie and the stored column.</summary>
public static class ConsentStates
{
    /// <summary>Cookie/column value for <see cref="ConsentState.Granted"/>.</summary>
    public const string Granted = "granted";

    /// <summary>Cookie/column value for <see cref="ConsentState.Denied"/>.</summary>
    public const string Denied = "denied";

    /// <summary>Column value for <see cref="ConsentState.Unknown"/> (never written to the cookie).</summary>
    public const string Unknown = "unknown";

    /// <summary>Parses a stored or cookie value; anything unrecognised is <see cref="ConsentState.Unknown"/>.</summary>
    public static ConsentState Parse(string? value) => value switch
    {
        Granted => ConsentState.Granted,
        Denied => ConsentState.Denied,
        _ => ConsentState.Unknown,
    };

    /// <summary>The value written to the <c>consent</c> column for every recorded event.</summary>
    public static string ToStorageValue(ConsentState state) => state switch
    {
        ConsentState.Granted => Granted,
        ConsentState.Denied => Denied,
        _ => Unknown,
    };
}
