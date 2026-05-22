using System.Collections;
using System.Reflection;
using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Spec 020 T041 — drift-guard for the SQL Prompt import/export key map.
///
/// <para>
/// Every key in <see cref="SqlPromptImporter"/>'s private <c>OptionMap</c> must have a
/// matching entry in <see cref="SqlPromptExporter"/>'s private <c>ReverseMap</c>, and
/// vice versa. This catches the failure mode where someone adds an import binding
/// without the export inverse (or vice versa), which would silently break the
/// AKML ↔ SQL Prompt XML round-trip for that setting. Per-value enum normalisation
/// is covered by the dedicated round-trip and XML-token tests in
/// <see cref="SqlPromptExporterTests"/>; this test guards the key-set parity only.
/// </para>
/// </summary>
public class SqlPromptKeyMapTests
{
    [Fact]
    public void OptionMapAndReverseMapHaveSameKeys()
    {
        var importerKeys = GetPrivateStaticDictionaryKeys(typeof(SqlPromptImporter), "OptionMap");
        var exporterKeys = GetPrivateStaticDictionaryKeys(typeof(SqlPromptExporter), "ReverseMap");

        var importerOnly = importerKeys.Except(exporterKeys, StringComparer.OrdinalIgnoreCase).ToList();
        var exporterOnly = exporterKeys.Except(importerKeys, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(importerOnly.Count == 0,
            $"SqlPromptImporter.OptionMap has key(s) with no matching SqlPromptExporter.ReverseMap entry: {string.Join(", ", importerOnly)}");
        Assert.True(exporterOnly.Count == 0,
            $"SqlPromptExporter.ReverseMap has key(s) with no matching SqlPromptImporter.OptionMap entry: {string.Join(", ", exporterOnly)}");
    }

    [Fact]
    public void OptionMapAndReverseMapAreNonEmpty()
    {
        // Sanity check: the reflection lookup must actually find dictionaries with entries.
        // Otherwise OptionMapAndReverseMapHaveSameKeys would trivially pass with two empty sets.
        var importerKeys = GetPrivateStaticDictionaryKeys(typeof(SqlPromptImporter), "OptionMap");
        var exporterKeys = GetPrivateStaticDictionaryKeys(typeof(SqlPromptExporter), "ReverseMap");

        Assert.True(importerKeys.Count > 0, "SqlPromptImporter.OptionMap reflection returned 0 keys");
        Assert.True(exporterKeys.Count > 0, "SqlPromptExporter.ReverseMap reflection returned 0 keys");
    }

    private static HashSet<string> GetPrivateStaticDictionaryKeys(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException(
                        $"{type.Name}.{fieldName} not found — has the dictionary been renamed or made public?");

        if (field.GetValue(null) is not IDictionary map)
            throw new InvalidOperationException($"{type.Name}.{fieldName} is not an IDictionary");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in map.Keys)
        {
            if (key is string s) keys.Add(s);
        }
        return keys;
    }
}
