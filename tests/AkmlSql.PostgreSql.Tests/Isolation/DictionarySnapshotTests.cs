using System.Reflection;
using System.Text.Json;
using AkmlSql.Engine.Completion.Dictionaries;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Isolation;

/// <summary>
/// Spec 042 (FR-054, FR-091, task T039): the T-SQL keyword, function and system-procedure dictionaries must not gain a
/// PostgreSQL item and must not lose a T-SQL one. This pins them: the snapshot was taken from the dictionaries as they
/// were before the feature, and any difference fails here. A deliberate change to the T-SQL dictionaries (a new SQL
/// Server version, say) regenerates it with <c>AKML_UPDATE_SNAPSHOTS=1</c>, in a change that is about that.
/// </summary>
[Trait("Category", "Isolation")]
public class DictionarySnapshotTests
{
    private const string SnapshotFile = "tsql-dictionaries.snapshot.json";

    private static SortedDictionary<string, string[]> Capture()
    {
        var snapshot = new SortedDictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var field in typeof(KeywordDictionary).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f.FieldType == typeof(string[])))
        {
            snapshot["Keywords." + field.Name] = ((string[])field.GetValue(null)!).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        snapshot["BuiltinFunctions"] = BuiltinFunctionDictionary.GetAllFunctionNames().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        snapshot["SystemProcedures"] = SystemProcDictionary.GetCompletionItems().Select(i => i.DisplayText).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return snapshot;
    }

    private static string SourcePath()
    {
        // The snapshot is a source file; find it from the test assembly's folder up to the project.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AkmlSql.PostgreSql.Tests.csproj")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? AppContext.BaseDirectory, "Isolation", SnapshotFile);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [Fact]
    public void The_T_SQL_dictionaries_are_exactly_what_they_were_before_the_feature()
    {
        var current = Capture();

        if (Environment.GetEnvironmentVariable("AKML_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(SourcePath(), JsonSerializer.Serialize(current, Json) + Environment.NewLine);
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Isolation", SnapshotFile);
        Assert.True(File.Exists(path), "Missing " + path + "; regenerate with AKML_UPDATE_SNAPSHOTS=1.");
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, string[]>>(File.ReadAllText(path))!;

        Assert.Equal(expected.Keys, current.Keys);
        foreach (var (name, items) in expected)
        {
            var added = current[name].Except(items, StringComparer.Ordinal).ToArray();
            var removed = items.Except(current[name], StringComparer.Ordinal).ToArray();
            Assert.True(added.Length == 0 && removed.Length == 0,
                $"{name}: added [{string.Join(", ", added)}] removed [{string.Join(", ", removed)}]");
        }
    }

    [Fact]
    public void The_snapshot_covers_every_keyword_category_and_is_not_trivially_small()
    {
        var current = Capture();

        Assert.True(current.Count >= 20, "keyword categories + functions + procedures: " + current.Count);
        Assert.Contains("Keywords.Dml", current.Keys);
        Assert.Contains("BuiltinFunctions", current.Keys);
        Assert.Contains("SELECT", current["Keywords.Dml"]);
        Assert.NotEmpty(current["SystemProcedures"]);
    }
}
