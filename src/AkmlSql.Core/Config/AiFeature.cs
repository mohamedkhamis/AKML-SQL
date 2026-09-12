#nullable enable

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 037 (data-model E4) — the seven assignable AI features, used only in memory (never
    /// serialised). Each engine handler declares its own value;
    /// <see cref="AiAgentResolver.ResolveFor"/> maps it to the matching field of
    /// <see cref="FeatureAgentAssignments"/>.
    /// </summary>
    public enum AiFeature
    {
        Chat,
        TextToSql,
        Explain,
        Fix,
        Optimize,
        IndexSuggestions,
        GhostText
    }
}
