using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services.Settings;

/// <summary>What an import did: the sections applied and the settings in the file this build did not understand.</summary>
public sealed record SettingsImportResult(bool Ok, string? Error, int SectionsApplied, int Skipped)
{
    /// <summary>The notification text: "Imported 5 sections." / "… 3 settings skipped."</summary>
    public string Summary
    {
        get
        {
            if (!Ok) return Error ?? "The file could not be imported.";
            var text = SectionsApplied == 1 ? "Imported 1 section." : $"Imported {SectionsApplied.ToString(CultureInfo.InvariantCulture)} sections.";
            if (Skipped > 0) text += Skipped == 1 ? " 1 setting skipped." : $" {Skipped.ToString(CultureInfo.InvariantCulture)} settings skipped.";
            return text;
        }
    }
}

/// <summary>
/// Spec 041 (research R46–R49, FR-058, FR-059, SC-013) — Restore all defaults, Export and Import over the
/// exportable sections. The envelope is built from the sections only, and each section reads only its
/// preference store, so the file holds no key, password, pairing token, connection, cache, snippet, history,
/// chat, document or log — by construction.
/// </summary>
public interface ISettingsPortability
{
    /// <summary>The exportable sections, in envelope order.</summary>
    IReadOnlyList<ISettingsSection> Sections { get; }

    ISettingsSection? Find(string id);

    /// <summary>Every section's reset lines, for the one Restore all confirmation.</summary>
    Task<IReadOnlyList<string>> DescribeRestoreAllAsync();

    Task RestoreAllAsync();

    /// <summary>The export file's text.</summary>
    Task<string> ExportAsync(DateTimeOffset now);

    /// <summary><c>akmlsql-web-settings-yyyyMMdd.json</c>.</summary>
    string ExportFileName(DateTimeOffset now);

    Task<SettingsImportResult> ImportAsync(string json);
}

internal sealed class SettingsPortabilityService : ISettingsPortability
{
    public const string Format = "akmlsql-web-settings";
    public const int Version = 1;

    private readonly IReadOnlyList<ISettingsSection> _sections;

    public SettingsPortabilityService(IEnumerable<ISettingsSection> sections)
    {
        _sections = (sections ?? throw new ArgumentNullException(nameof(sections))).ToList();
    }

    public IReadOnlyList<ISettingsSection> Sections => _sections;

    public ISettingsSection? Find(string id) => _sections.FirstOrDefault(s => s.Id == id);

    public async Task<IReadOnlyList<string>> DescribeRestoreAllAsync()
    {
        var lines = new List<string>();
        foreach (var s in _sections) lines.AddRange(await s.DescribeResetAsync().ConfigureAwait(false));
        lines.Add("Connections, the schema cache, snippets, history, chat and your document are kept.");
        return lines;
    }

    public async Task RestoreAllAsync()
    {
        foreach (var s in _sections) await s.ResetAsync().ConfigureAwait(false);
    }

    public string ExportFileName(DateTimeOffset now) =>
        $"akmlsql-web-settings-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.json";

    public async Task<string> ExportAsync(DateTimeOffset now)
    {
        var sections = new JsonObject();
        foreach (var s in _sections)
        {
            var dto = await s.ExportAsync().ConfigureAwait(false);
            sections[s.ExportKey] = JsonSerializer.SerializeToNode(dto, dto.GetType(), SettingsJson.Options);
        }
        var envelope = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["exportedAt"] = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["appVersion"] = AppVersion(),
            ["sections"] = sections,
        };
        return envelope.ToJsonString(SettingsJson.Options);
    }

    public async Task<SettingsImportResult> ImportAsync(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json ?? string.Empty); }
        catch (JsonException) { return new SettingsImportResult(false, "That file is not a settings export (it is not JSON).", 0, 0); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format)
                || format.ValueKind != JsonValueKind.String
                || format.GetString() != Format)
            {
                return new SettingsImportResult(false, "That file is not an AKML SQL web settings export.", 0, 0);
            }
            // Any version is accepted: a newer file's unknown sections and properties are counted as skipped.
            if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Object)
                return new SettingsImportResult(true, null, 0, 0);

            int applied = 0, skipped = 0;
            foreach (var prop in sections.EnumerateObject())
            {
                var section = _sections.FirstOrDefault(s => string.Equals(s.ExportKey, prop.Name, StringComparison.Ordinal));
                if (section == null)
                {
                    skipped += prop.Value.ValueKind == JsonValueKind.Object ? Math.Max(1, prop.Value.EnumerateObject().Count()) : 1;
                    continue;
                }
                skipped += await section.ImportAsync(prop.Value).ConfigureAwait(false);
                applied++;
            }
            return new SettingsImportResult(true, null, applied, skipped);
        }
    }

    private static string AppVersion()
    {
        var asm = typeof(SettingsPortabilityService).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            int plus = info!.IndexOf('+');
            return plus > 0 ? info.Substring(0, plus) : info;
        }
        return asm.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}
