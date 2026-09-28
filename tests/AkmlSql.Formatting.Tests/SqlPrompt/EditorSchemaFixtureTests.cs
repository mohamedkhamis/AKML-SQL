using System;
using System.IO;
using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

/// <summary>
/// Spec 040 (T075) — the net472 shell tests cannot call <see cref="SqlPromptOptionCatalog"/>, so
/// the Format Styles row-layout test reads a snapshot of its editor schema. This test keeps the
/// snapshot equal to the catalog. After changing the catalog, regenerate it with
/// <c>AKML_UPDATE_SNAPSHOTS=1 dotnet test --filter EditorSchemaFixtureTests</c>.
/// </summary>
public sealed class EditorSchemaFixtureTests
{
    internal static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "AkmlSql.Shell.Shared.Tests", "Fixtures", "sqlprompt-editor-schema.json");
    }

    [Fact]
    public void The_shell_test_snapshot_matches_the_catalog()
    {
        var path = FixturePath();
        var json = SqlPromptOptionCatalog.ToEditorSchemaJson();

        if (Environment.GetEnvironmentVariable("AKML_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        Assert.True(File.Exists(path), $"Missing {path}; regenerate it with AKML_UPDATE_SNAPSHOTS=1.");
        Assert.Equal(json, File.ReadAllText(path));
    }
}
