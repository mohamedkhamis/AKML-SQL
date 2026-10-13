using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services.Settings;

/// <summary>
/// Spec 041 (research R46–R48, FR-058, FR-059) — a section of settings that can be restored to its defaults,
/// exported and imported. One per exportable section (General, Editor, Format, Queries, Code analysis, AI
/// assistance) plus the workspace Layout. Connections, Schema cache and Diagnostics are not exported.
/// </summary>
public interface ISettingsSection
{
    /// <summary>The Settings page section id (<c>general</c> … ; <c>layout</c> for the workspace).</summary>
    string Id { get; }

    string Title { get; }

    /// <summary>The section's property name in the export envelope (<c>codeAnalysis</c>, <c>aiAssistance</c> …).</summary>
    string ExportKey { get; }

    /// <summary>What Restore defaults would remove, one line each, for the confirmation.</summary>
    Task<IReadOnlyList<string>> DescribeResetAsync();

    Task ResetAsync();

    /// <summary>The section's settings as an export DTO (serialised with <see cref="SettingsJson.Options"/>).</summary>
    Task<object> ExportAsync();

    /// <summary>Apply an imported section; returns how many settings in it were not understood.</summary>
    Task<int> ImportAsync(JsonElement section);
}

/// <summary>A DTO of the export envelope: properties this build does not know are kept here and counted.</summary>
public abstract class SettingsDto
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>Spec 041 (R48) — the envelope's JSON: camelCase, enums as strings.</summary>
public static class SettingsJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,   // rule ids and feature ids stay as stored
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Read a section DTO; null when the element is not an object of that shape.</summary>
    public static T? Read<T>(JsonElement element) where T : class
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        try { return element.Deserialize<T>(Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>The unknown properties of a DTO and every DTO nested in it (lists included).</summary>
    public static int CountUnknown(object? dto)
    {
        if (dto == null) return 0;
        int count = 0;
        if (dto is SettingsDto s) count += s.Unknown?.Count ?? 0;
        if (dto is System.Collections.IEnumerable list && dto is not string && dto is not System.Collections.IDictionary)
        {
            foreach (var item in list) count += CountUnknown(item);
            return count;
        }
        if (dto is not SettingsDto) return count;
        foreach (var p in dto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == nameof(SettingsDto.Unknown) || p.GetIndexParameters().Length > 0) continue;
            var t = p.PropertyType;
            if (typeof(SettingsDto).IsAssignableFrom(t)
                || (t.IsGenericType && t.GetGenericArguments().Any(a => typeof(SettingsDto).IsAssignableFrom(a))))
            {
                count += CountUnknown(p.GetValue(dto));
            }
        }
        return count;
    }
}
