namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 028 (M6) — shared provider-locality helper. Centralised so the fully-local
/// privacy guard (FR-004/FR-012) is enforced identically everywhere (prompt service,
/// chat panel, settings picker) rather than each surface re-deciding what "local" means.
/// </summary>
internal static class AiProviders
{
    /// <summary>True for providers that run on the user's machine and need no cloud egress.</summary>
    public static bool IsLocal(string? providerId) =>
        providerId is "ollama" or "lmstudio";
}
