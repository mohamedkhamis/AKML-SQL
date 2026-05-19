using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine.Ai.Context;
using AkmlSql.Engine.Ai.Privacy;
using AkmlSql.Engine.Ai.Prompts;
using AkmlSql.Engine.Ai.Providers;
using AkmlSql.Engine.Schema;
using Xunit;

namespace AkmlSql.AI.Tests;

/// <summary>
/// Spec 021 T121-T124. Smoke tests proving the AI extraction works: prompt builders,
/// privacy transformers, schema-context builder, and the provider factory are all
/// reachable from a project that ONLY references AkmlSql.AI (no engine dependency).
/// Functional behaviour of the moved types is covered by their existing engine-side tests.
/// </summary>
public sealed class ExtractionSmokeTests
{
    [Fact]
    public void ExplainPrompt_builds_system_and_user_messages()
    {
        var (system, user) = ExplainPrompt.Build(
            schemaText: "Tables: dbo.Customers (Id, Name)",
            selectedSql: "SELECT Id FROM dbo.Customers;");

        Assert.False(string.IsNullOrWhiteSpace(system));
        Assert.False(string.IsNullOrWhiteSpace(user));
        Assert.Contains("dbo.Customers", user);
    }

    [Fact]
    public void PromptTemplates_BuildSystemPrompt_includes_task_instruction()
    {
        var prompt = PromptTemplates.BuildSystemPrompt(
            schemaText: "tbl.Foo (Id INT)",
            taskInstruction: "Explain the query.");

        Assert.Contains("Explain the query.", prompt);
    }

    [Fact]
    public void AiProviderFactory_KeyDecryptor_defaults_to_identity()
    {
        // Default behaviour: KeyDecryptor returns the input unchanged. The engine
        // wires DPAPI at startup; the web edition leaves the default in place
        // (Web Crypto unwraps the key BEFORE calling the factory).
        Assert.Equal("plaintext-key", AiProviderFactory.KeyDecryptor("plaintext-key"));
        Assert.Equal(string.Empty, AiProviderFactory.KeyDecryptor(null));
    }

    [Fact]
    public async Task SchemaContextBuilder_handles_unknown_session_gracefully()
    {
        // Lookup returns nulls for an unknown session -- builder should produce an
        // empty SchemaContext rather than throw. Spec 021 T121 contract.
        var builder = new SchemaContextBuilder((_, _) => null);

        var ctx = await builder.BuildAsync(
            sessionId: "no-such-session",
            sessionLookup: _ => (null, null),
            prompt: null,
            compressionLevel: 1);

        Assert.NotNull(ctx);
        Assert.Equal(string.Empty, ctx.DatabaseName);
    }

    [Fact]
    public void Privacy_namespace_types_are_reachable_from_extracted_library()
    {
        // Smoke: IdentifierHasher (static), LiteralRedactor + PrivacyTransformer types
        // exist and resolve from a project that does NOT reference AkmlSql.Engine.
        // Full functional coverage stays in tests/AkmlSql.Engine.Tests/Ai/.
        Assert.NotNull(typeof(IdentifierHasher));
        Assert.NotNull(typeof(LiteralRedactor));
        Assert.NotNull(typeof(PrivacyTransformer));
    }
}
