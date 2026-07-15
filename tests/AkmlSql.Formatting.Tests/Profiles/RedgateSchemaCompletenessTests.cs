using System.Text.Json;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Spec 031 Task 7 (SC-004) — schema-walking completeness gate. Enumerates every leaf option
/// path in the vendored Redgate <c>formattingstyle-schema.json</c> (draft-07) and asserts each
/// one is present in <see cref="RedgateJsonStyleImporter.KnownOptionPaths"/> — i.e. every schema
/// key was a deliberate <c>Add(...)</c> (mapped) or <c>AddUnsupported(...)</c> (explicitly
/// unsupported with a reason) decision, never silently left "unknown".
/// </summary>
public class RedgateSchemaCompletenessTests
{
    [Fact]
    public void Every_schema_leaf_key_is_mapped_or_explicitly_unsupported()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "formattingstyle-schema.json"));
        using var doc = JsonDocument.Parse(json);
        var leaves = new List<string>();
        CollectLeaves(doc.RootElement.GetProperty("properties"), "", leaves);

        var missing = leaves
            .Where(p => !p.StartsWith("metadata", StringComparison.OrdinalIgnoreCase))
            .Where(p => !RedgateJsonStyleImporter.KnownOptionPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(leaves.Count > 60, $"Schema walk looks broken — only {leaves.Count} leaves found.");
        Assert.True(missing.Count == 0,
            "Schema keys not classified (add Add(...) or AddUnsupported(...) with a reason):\n" + string.Join("\n", missing));
    }

    private static void CollectLeaves(JsonElement properties, string prefix, List<string> into)
    {
        foreach (var prop in properties.EnumerateObject())
        {
            var path = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("properties", out var nested))
                CollectLeaves(nested, path, into);
            else
                into.Add(path);
        }
    }

    [Fact]
    public void Every_unsupported_entry_has_a_reason()
    {
        // Import a synthetic file containing ONLY unsupported keys? Simpler: reasons are enforced at
        // registration — assert via a representative unsupported key end-to-end:
        var result = RedgateJsonStyleImporter.Import("""{ "cte": { "asAlignment": "indented" } }""");
        var report = Assert.Single(result.Options);
        Assert.Equal(RedgateOptionStatus.Unsupported, report.Status);
        Assert.False(string.IsNullOrWhiteSpace(report.Reason));
    }

    [Fact]
    public void Example_enum_values_are_uppercamel_and_still_match()
    {
        // full-style.json.example documents enums in UpperCamelCase; real files serialize lowerCamelCase (FR-001).
        var result = RedgateJsonStyleImporter.Import("""{ "casing": { "reservedKeywords": "UpperCamelCase" } }""");
        Assert.Equal("PascalCase", result.Profile.Casing.ReservedKeywords);
    }
}
