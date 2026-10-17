using System.Text.Json;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Diagnostics;

/// <summary>
/// Re-review REV-05 (from the reviewer's acceptance probe, assertions kept): a browser upgraded from the build before the
/// 2026-10-17 fixes can hold diagnostics with whole provider responses — echoed keys, prompts, schema. They must not survive
/// restore, persistence or export. Legacy AI entries are purged (no pattern can find an echoed prompt); every other entry is
/// masked as a new one is. Synthetic markers only.
/// </summary>
public sealed class DiagnosticsLegacyMigrationTests
{
    private const string KeyMarker = "sk-review0123456789abcdef0123456789";
    private const string PromptMarker = "SYNTHETIC PROMPT: list the payroll of every employee";
    private const string SchemaMarker = "dbo.SyntheticSecretPayroll";

    private static void SeedLegacy(InMemoryIndexedDbAdapter adapter, params DiagnosticEntry[] entries) =>
        adapter.Seed(StoreNames.Diagnostics, "ring", JsonSerializer.Serialize(entries));

    [Fact]
    public async Task Restored_diagnostics_must_not_export_legacy_credentials()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        SeedLegacy(adapter, new DiagnosticEntry(DateTimeOffset.UtcNow, DiagnosticLevel.Error, "ai-chat", "Provider echoed api_key=" + KeyMarker, null));
        var ring = new DiagnosticsRingBuffer(adapter);
        await ring.RestoreAsync();
        await ring.FlushAsync();
        Assert.DoesNotContain(KeyMarker, JsonSerializer.Serialize(ring.Snapshot()));
        Assert.DoesNotContain(KeyMarker, await adapter.GetAsync(StoreNames.Diagnostics, "ring"));
    }

    [Fact]
    public async Task A_legacy_AI_entry_loses_an_echoed_prompt_and_schema_too_and_the_store_is_rewritten_at_once()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        SeedLegacy(adapter, new DiagnosticEntry(DateTimeOffset.UtcNow, DiagnosticLevel.Error, "ai",
            $"anthropic returned 400: Bad Request. {{\"echo\":\"{PromptMarker}\",\"schema\":\"{SchemaMarker}\"}}", new { Body = PromptMarker }));
        var ring = new DiagnosticsRingBuffer(adapter);

        await ring.RestoreAsync();   // no flush: the migration persists on its own

        var stored = await adapter.GetAsync(StoreNames.Diagnostics, "ring");
        foreach (var text in new[] { JsonSerializer.Serialize(ring.Snapshot()), stored! })
        {
            Assert.DoesNotContain(PromptMarker, text);
            Assert.DoesNotContain(SchemaMarker, text);
        }
        var entry = Assert.Single(ring.Snapshot());
        Assert.Equal(DiagnosticsRingBuffer.PurgedLegacyAiMessage, entry.Message);
        Assert.Equal(DiagnosticLevel.Error, entry.Level);   // the safe facts stay: when, how bad, where from
        Assert.Equal("ai", entry.Source);
    }

    [Fact]
    public async Task A_legacy_entry_from_elsewhere_is_kept_and_masked()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        SeedLegacy(adapter, new DiagnosticEntry(DateTimeOffset.UtcNow, DiagnosticLevel.Warn, "sql-connect",
            "Connect failed: Server=db;User ID=u;Password=hunter2-synthetic;", null));
        var ring = new DiagnosticsRingBuffer(adapter);

        await ring.RestoreAsync();

        var message = Assert.Single(ring.Snapshot()).Message;
        Assert.StartsWith("Connect failed:", message);
        Assert.DoesNotContain("hunter2-synthetic", message);
    }

    [Fact]
    public async Task The_current_format_round_trips()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        var first = new DiagnosticsRingBuffer(adapter);
        first.Log(DiagnosticLevel.Info, "ai", "Explain completed (120 chars).");
        await first.FlushAsync();

        var second = new DiagnosticsRingBuffer(adapter);
        await second.RestoreAsync();

        Assert.Equal("Explain completed (120 chars).", Assert.Single(second.Snapshot()).Message);   // a current AI entry is kept
    }
}
